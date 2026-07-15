using System.Collections.Concurrent;
using LiteNetLib;
using LiteNetLib.Utils;
using MessagePack;
using Serilog;
using SharedLib.Models;
using SharedLib.Protocol;

namespace LobbyServer.Room;

public class RoomManager
{
    private readonly NetManager _netManager;
    private readonly Dictionary<string, GameRoom> _rooms = new();

    public ConcurrentDictionary<NetPeer, GameServerInfo> GameServers { get; set; } = new();

    public RoomManager(NetManager netManager)
    {
        _netManager = netManager;
    }

    public (CreateRoomResponse Response, ReturnCode Code) CreateRoom(NetPeer peer, PlayerInfo player, CreateRoomRequest request)
    {
        Log.Information("[RoomManager] CreateRoom userId={UserId} roomId={RoomId}", player.UserId, request.RoomId);

        var gs = PickGameServer();
        if (gs == null)
        {
            Log.Warning("[RoomManager] CreateRoom 失败：无可用 GameServer userId={UserId}", player.UserId);
            return (new CreateRoomResponse(), ReturnCode.NoGameServerAvailable);
        }

        var gsValue = gs.Value;
        var roomId = request.RoomId ?? Guid.NewGuid().ToString("N")[..8];
        var room = new GameRoom
        {
            Info = new RoomInfo
            {
                RoomId = roomId,
                RoomType = request.RoomType,
                GameServerAddress = gsValue.Value.Address,
                GameServerPort = gsValue.Value.Port,
                OwnerUserId = player.UserId,
                Players = new List<PlayerInfo> { player }
            },
            GameServerPeer = gsValue.Key,
            OwnerPeer = peer,
            PlayerPeers = new Dictionary<NetPeer, PlayerInfo> { { peer, player } }
        };
        _rooms[roomId] = room;

        Log.Information("[RoomManager] 房间创建成功 roomId={RoomId} GameServer={Addr}:{Port} 房主={UserId} 总房间数={TotalRooms}",
            roomId, gsValue.Value.Address, gsValue.Value.Port, player.UserId, _rooms.Count);
        return (new CreateRoomResponse { Room = room.Info }, ReturnCode.Success);
    }

    public (JoinRoomResponse Response, ReturnCode Code) JoinRoom(NetPeer peer, PlayerInfo player, JoinRoomRequest request)
    {
        Log.Information("[RoomManager] JoinRoom userId={UserId} roomId={RoomId}", player.UserId, request.RoomId);

        if (!_rooms.TryGetValue(request.RoomId, out var room))
        {
            Log.Warning("[RoomManager] JoinRoom 失败：房间不存在 roomId={RoomId} userId={UserId}", request.RoomId, player.UserId);
            return (new JoinRoomResponse { Room = new RoomInfo { RoomId = request.RoomId } }, ReturnCode.RoomNotFound);
        }

        if (room.PlayerPeers.ContainsKey(peer))
        {
            Log.Warning("[RoomManager] JoinRoom 失败：已在房间 roomId={RoomId} userId={UserId}", request.RoomId, player.UserId);
            return (new JoinRoomResponse { Room = new RoomInfo { RoomId = request.RoomId } }, ReturnCode.AlreadyInRoom);
        }

        LeaveRoom(peer);

        room.PlayerPeers[peer] = player;
        room.Info.Players.Add(player);
        room.Info.OwnerUserId = room.PlayerPeers.GetValueOrDefault(room.OwnerPeer!)?.UserId ?? 0;

        var gs = GameServers[room.GameServerPeer];

        var notify = new JoinRoomNotify { RoomId = request.RoomId, Player = player };
        foreach (var otherPeer in room.PlayerPeers.Keys.Where(p => p != peer))
        {
            Send(otherPeer, MessageIds.JoinRoomNotify, ReturnCode.Success, notify);
        }

        Log.Information("[RoomManager] 加入房间成功 roomId={RoomId} userId={UserId} 房主={Owner} 房间人数={Count}",
            request.RoomId, player.UserId, room.Info.OwnerUserId, room.PlayerPeers.Count);
        return (new JoinRoomResponse { Room = room.Info }, ReturnCode.Success);
    }

    public (LeaveRoomResponse Response, ReturnCode Code) LeaveRoom(NetPeer peer)
    {
        foreach (var (roomId, room) in _rooms)
        {
            if (room.PlayerPeers.TryGetValue(peer, out var player) && room.PlayerPeers.Remove(peer))
            {
                room.Info.Players.Remove(player);
                var remaining = room.PlayerPeers.Count;
                Log.Information("[RoomManager] 离开房间 roomId={RoomId} userId={UserId} 剩余人数={Count}",
                    roomId, player.UserId, remaining);

                if (remaining == 0)
                {
                    _rooms.Remove(roomId);
                    Log.Information("[RoomManager] 房间关闭 roomId={RoomId}（无玩家）", roomId);
                }
                else
                {
                    ReassignOwner(roomId, room);
                }

                return (new LeaveRoomResponse { RoomId = roomId }, ReturnCode.Success);
            }
        }
        Log.Warning("[RoomManager] LeaveRoom 失败：不在任何房间");
        return (new LeaveRoomResponse(), ReturnCode.NotInRoom);
    }

    public RoomListResponse GetRoomList()
    {
        var list = _rooms.Values.Select(r => new RoomListInfo
        {
            RoomId = r.Info.RoomId,
            RoomType = r.Info.RoomType,
            PlayerCount = r.PlayerPeers.Count
        }).ToList();

        Log.Information("[RoomManager] 查询房间列表 房间数={Count}", list.Count);
        return new RoomListResponse { Rooms = list };
    }

    public void RemovePlayer(NetPeer peer)
    {
        foreach (var (roomId, room) in _rooms.ToList())
        {
            if (room.PlayerPeers.TryGetValue(peer, out var player) && room.PlayerPeers.Remove(peer))
            {
                room.Info.Players.Remove(player);
                var remaining = room.PlayerPeers.Count;
                Log.Information("[RoomManager] 断线离开房间 roomId={RoomId} userId={UserId} 剩余人数={Count}",
                    roomId, player.UserId, remaining);

                if (remaining == 0)
                {
                    _rooms.Remove(roomId);
                    Log.Information("[RoomManager] 房间关闭 roomId={RoomId}（全部断线）", roomId);
                }
                else
                {
                    ReassignOwner(roomId, room);
                }
            }
        }
    }

    private void ReassignOwner(string roomId, GameRoom room)
    {
        var oldOwner = room.OwnerPeer;
        room.OwnerPeer = room.PlayerPeers.Keys.FirstOrDefault();
        room.Info.OwnerUserId = room.PlayerPeers.GetValueOrDefault(room.OwnerPeer!)?.UserId ?? 0;

        var oldUserId = oldOwner != null && room.PlayerPeers.TryGetValue(oldOwner, out var oldP) ? oldP.UserId : 0;
        if (oldUserId != room.Info.OwnerUserId)
        {
            Log.Information("[RoomManager] 房主转移 roomId={RoomId} {OldOwner}→{NewOwner}",
                roomId, oldUserId, room.Info.OwnerUserId);
        }
    }

    private KeyValuePair<NetPeer, GameServerInfo>? PickGameServer()
    {
        var result = GameServers
            .Where(gs => gs.Key.ConnectionState == ConnectionState.Connected)
            .MinBy(gs => gs.Value.PlayerCount);

        if (result.Key != null)
        {
            Log.Information("[RoomManager] 选择 GameServer {Addr}:{Port} 负载={Players}",
                result.Value.Address, result.Value.Port, result.Value.PlayerCount);
        }
        else
        {
            Log.Warning("[RoomManager] 无可用的 GameServer");
        }

        return result;
    }

    private void Send(NetPeer peer, ushort messageId, ReturnCode code, object data)
    {
        var writer = new NetDataWriter();
        writer.Put(messageId);
        writer.Put((byte)code);
        writer.Put(MessagePackSerializer.Serialize(data));
        peer.Send(writer, DeliveryMethod.ReliableOrdered);
    }
}
