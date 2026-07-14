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
    private readonly Dictionary<string, GameServerRoom> _rooms = new();

    public ConcurrentDictionary<NetPeer, LobbyServer.GameServerInfo> GameServers { get; set; } = new();

    public RoomManager(NetManager netManager)
    {
        _netManager = netManager;
    }

    public (CreateRoomResponse Response, ReturnCode Code) CreateRoom(NetPeer peer, PlayerInfo player, CreateRoomRequest request)
    {
        var gs = PickGameServer();
        if (gs == null)
        {
            Log.Warning("创建房间失败：无可用 GameServer");
            return (new CreateRoomResponse(), ReturnCode.NoGameServerAvailable);
        }

        var gsValue = gs.Value;
        var roomId = request.RoomId ?? Guid.NewGuid().ToString("N")[..8];
        var room = new GameServerRoom
        {
            RoomId = roomId,
            RoomType = request.RoomType,
            GameServerPeer = gsValue.Key,
            Players = new Dictionary<NetPeer, PlayerInfo> { { peer, player } }
        };
        _rooms[roomId] = room;

        Log.Information("房间创建 roomId={RoomId} GameServer={Port} userId={UserId}",
            roomId, gsValue.Value.Port, player.UserId);
        return (new CreateRoomResponse
        {
            RoomId = roomId,
            RoomType = request.RoomType,
            GameServerAddress = gsValue.Value.Address,
            GameServerPort = gsValue.Value.Port
        }, ReturnCode.Success);
    }

    public (JoinRoomResponse Response, ReturnCode Code) JoinRoom(NetPeer peer, PlayerInfo player, JoinRoomRequest request)
    {
        if (!_rooms.TryGetValue(request.RoomId, out var room))
        {
            Log.Warning("加入房间失败 roomId={RoomId} 不存在", request.RoomId);
            return (new JoinRoomResponse { RoomId = request.RoomId }, ReturnCode.RoomNotFound);
        }

        if (room.Players.ContainsKey(peer))
        {
            Log.Warning("加入房间失败 userId={UserId} 已在房间 roomId={RoomId}", player.UserId, request.RoomId);
            return (new JoinRoomResponse { RoomId = request.RoomId }, ReturnCode.AlreadyInRoom);
        }

        LeaveRoom(peer);

        room.Players[peer] = player;

        var gs = GameServers[room.GameServerPeer];

        var notify = new JoinRoomNotify { RoomId = request.RoomId, Player = player };
        foreach (var otherPeer in room.Players.Keys.Where(p => p != peer))
        {
            Send(otherPeer, MessageIds.JoinRoomNotify, ReturnCode.Success, notify);
        }

        Log.Information("加入房间 roomId={RoomId} userId={UserId} GameServer={Port}",
            request.RoomId, player.UserId, gs.Port);
        return (new JoinRoomResponse
        {
            RoomId = request.RoomId,
            GameServerAddress = gs.Address,
            GameServerPort = gs.Port
        }, ReturnCode.Success);
    }

    public (LeaveRoomResponse Response, ReturnCode Code) LeaveRoom(NetPeer peer)
    {
        foreach (var (roomId, room) in _rooms)
        {
            if (room.Players.Remove(peer))
            {
                Log.Information("离开房间 roomId={RoomId} 剩余={Count}", roomId, room.Players.Count);
                if (room.Players.Count == 0)
                    _rooms.Remove(roomId);

                return (new LeaveRoomResponse { RoomId = roomId }, ReturnCode.Success);
            }
        }
        return (new LeaveRoomResponse(), ReturnCode.NotInRoom);
    }

    public RoomListResponse GetRoomList()
    {
        var list = _rooms.Values.Select(r => new RoomListInfo
        {
            RoomId = r.RoomId,
            RoomType = r.RoomType,
            PlayerCount = r.Players.Count
        }).ToList();

        return new RoomListResponse { Rooms = list };
    }

    public void RemovePlayer(NetPeer peer)
    {
        foreach (var (roomId, room) in _rooms.ToList())
        {
            if (room.Players.Remove(peer))
            {
                Log.Information("断线离开房间 roomId={RoomId} 剩余={Count}", roomId, room.Players.Count);
                if (room.Players.Count == 0)
                    _rooms.Remove(roomId);
            }
        }
    }

    private KeyValuePair<NetPeer, LobbyServer.GameServerInfo>? PickGameServer()
    {
        var result = GameServers
            .Where(gs => gs.Key.ConnectionState == ConnectionState.Connected)
            .MinBy(gs => gs.Value.PlayerCount);

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
