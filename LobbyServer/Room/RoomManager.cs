using System.Collections.Concurrent;
using LiteNetLib;
using LiteNetLib.Utils;
using MessagePack;
using Serilog;
using SharedLib.Models;
using SharedLib.Protocol;

namespace LobbyServer.Room;

/// <summary>
/// 房间管理器，负责房间的创建、加入、离开以及游戏服务器负载均衡分配
/// </summary>
public class RoomManager
{
    private readonly NetManager _netManager;
    private readonly Dictionary<string, GameServerRoom> _rooms = new();

    /// <summary>
    /// 已注册的游戏服务器字典，Key 为网络对等体，Value 为服务器信息
    /// </summary>
    public ConcurrentDictionary<NetPeer, GameServerInfo> GameServers { get; set; } = new();

    /// <summary>
    /// 初始化房间管理器
    /// </summary>
    public RoomManager(NetManager netManager)
    {
        _netManager = netManager;
    }

    /// <summary>
    /// 创建新房间并分配到负载最低的可用游戏服务器
    /// </summary>
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
            OwnerPeer = peer,
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

    /// <summary>
    /// 玩家加入指定房间，自动离开当前所在房间并通知同房间其他玩家
    /// </summary>
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
        var ownerUserId = room.Players.GetValueOrDefault(room.OwnerPeer!)?.UserId ?? 0;

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
            GameServerPort = gs.Port,
            Players = room.Players.Values.ToList(),
            OwnerUserId = ownerUserId
        }, ReturnCode.Success);
    }

    /// <summary>
    /// 玩家离开当前所在房间，房间为空时自动删除
    /// </summary>
    private void ReassignOwner(GameServerRoom room)
    {
        if (room.OwnerPeer == null || !room.Players.ContainsKey(room.OwnerPeer))
        {
            room.OwnerPeer = room.Players.Keys.FirstOrDefault();
        }
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
                else
                    ReassignOwner(room);

                return (new LeaveRoomResponse { RoomId = roomId }, ReturnCode.Success);
            }
        }
        return (new LeaveRoomResponse(), ReturnCode.NotInRoom);
    }

    /// <summary>
    /// 获取当前所有房间列表
    /// </summary>
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

    /// <summary>
    /// 从所有房间中移除断线玩家，房间为空时自动删除
    /// </summary>
    public void RemovePlayer(NetPeer peer)
    {
        foreach (var (roomId, room) in _rooms.ToList())
        {
            if (room.Players.Remove(peer))
            {
                Log.Information("断线离开房间 roomId={RoomId} 剩余={Count}", roomId, room.Players.Count);
                if (room.Players.Count == 0)
                    _rooms.Remove(roomId);
                else
                    ReassignOwner(room);
            }
        }
    }

    /// <summary>
    /// 选择负载最低的可用游戏服务器
    /// </summary>
    private KeyValuePair<NetPeer, GameServerInfo>? PickGameServer()
    {
        var result = GameServers
            .Where(gs => gs.Key.ConnectionState == ConnectionState.Connected)
            .MinBy(gs => gs.Value.PlayerCount);

        return result;
    }

    /// <summary>
    /// 向指定对等体发送序列化后的消息
    /// </summary>
    private void Send(NetPeer peer, ushort messageId, ReturnCode code, object data)
    {
        var writer = new NetDataWriter();
        writer.Put(messageId);
        writer.Put((byte)code);
        writer.Put(MessagePackSerializer.Serialize(data));
        peer.Send(writer, DeliveryMethod.ReliableOrdered);
    }
}
