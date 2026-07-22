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
    private readonly Dictionary<string, LobbyRoom> _rooms = new();

    public ConcurrentDictionary<NetPeer, GameServerInfo> GameServers { get; set; } = new();

    public RoomManager(NetManager netManager)
    {
        _netManager = netManager;
    }

    /// <summary>
    /// 创建新房间并分配到负载最低的 GameServer
    /// </summary>
    public (CreateRoomResponse Response, ReturnCode Code) CreateRoom(NetPeer peer, PlayerInfo player, CreateRoomRequest request)
    {
        Log.Information("[RoomManager] 创建房间 userId={UserId} roomId={RoomId}", player.UserId, request.RoomId);

        var gs = PickGameServer();
        if (gs == null)
        {
            Log.Warning("[RoomManager] 创建房间失败：无可用GameServer userId={UserId}", player.UserId);
            return (new CreateRoomResponse(), ReturnCode.NoGameServerAvailable);
        }

        var gsValue = gs.Value;
        var roomId = request.RoomId ?? Guid.NewGuid().ToString("N")[..8];

        if (_rooms.ContainsKey(roomId))
        {
            Log.Warning("[RoomManager] 创建房间失败：房间已存在 roomId={RoomId}", roomId);
            return (new CreateRoomResponse(), ReturnCode.Error);
        }
        var room = new LobbyRoom
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

    /// <summary>
    /// 玩家加入已有房间，自动离开当前房间并通知其他玩家
    /// </summary>
    public (JoinRoomResponse Response, ReturnCode Code) JoinRoom(NetPeer peer, PlayerInfo player, JoinRoomRequest request)
    {
        Log.Information("[RoomManager] 加入房间 userId={UserId} roomId={RoomId}", player.UserId, request.RoomId);

        if (!_rooms.TryGetValue(request.RoomId, out var room))
        {
            Log.Warning("[RoomManager] 加入房间失败：房间未找到 roomId={RoomId} userId={UserId}", request.RoomId, player.UserId);
            return (new JoinRoomResponse { Room = new RoomInfo { RoomId = request.RoomId } }, ReturnCode.RoomNotFound);
        }

        if (room.PlayerPeers.ContainsKey(peer))
        {
            Log.Warning("[RoomManager] 加入房间失败：已在房间中 roomId={RoomId} userId={UserId}", request.RoomId, player.UserId);
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

    /// <summary>
    /// 玩家离开当前房间，无玩家时自动移除房间
    /// </summary>
    public (LeaveRoomResponse Response, ReturnCode Code) LeaveRoom(NetPeer peer)
    {
        Log.Information("[RoomManager] 离开房间");

        foreach (var (roomId, room) in _rooms)
        {
            if (room.PlayerPeers.TryGetValue(peer, out var player) && room.PlayerPeers.Remove(peer))
            {
                room.ReadyPeers.Remove(peer);
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
                    var leaveNotify = new LeaveRoomNotify { RoomId = roomId, UserId = player.UserId };
                    foreach (var otherPeer in room.PlayerPeers.Keys)
                    {
                        Send(otherPeer, MessageIds.LeaveRoomNotify, ReturnCode.Success, leaveNotify);
                    }
                }

                return (new LeaveRoomResponse { RoomId = roomId }, ReturnCode.Success);
            }
        }
        Log.Warning("[RoomManager] 离开房间失败：玩家不在任何房间中");
        return (new LeaveRoomResponse(), ReturnCode.NotInRoom);
    }

    /// <summary>
    /// 玩家准备
    /// </summary>
    public (GameReadyResponse Response, ReturnCode Code) SetReady(NetPeer peer)
    {
        foreach (var (roomId, room) in _rooms)
        {
            if (room.PlayerPeers.ContainsKey(peer))
            {
                room.ReadyPeers.Add(peer);
                var ready = room.ReadyPeers.Count;
                var total = room.PlayerPeers.Count;
                var allReady = ready >= total;
                Log.Information("[RoomManager] 玩家准备 roomId={RoomId} ready={Ready}/{Total} allReady={AllReady}",
                    roomId, ready, total, allReady);
                return (new GameReadyResponse { ReadyCount = ready, TotalCount = total, AllReady = allReady }, ReturnCode.Success);
            }
        }
        return (new GameReadyResponse(), ReturnCode.NotInRoom);
    }

    /// <summary>
    /// 玩家取消准备
    /// </summary>
    public (GameUnreadyResponse Response, ReturnCode Code) SetUnready(NetPeer peer)
    {
        foreach (var (roomId, room) in _rooms)
        {
            if (room.PlayerPeers.ContainsKey(peer))
            {
                room.ReadyPeers.Remove(peer);
                var ready = room.ReadyPeers.Count;
                var total = room.PlayerPeers.Count;
                Log.Information("[RoomManager] 玩家取消准备 roomId={RoomId} ready={Ready}/{Total}",
                    roomId, ready, total);
                return (new GameUnreadyResponse { ReadyCount = ready, TotalCount = total }, ReturnCode.Success);
            }
        }
        return (new GameUnreadyResponse(), ReturnCode.NotInRoom);
    }

    /// <summary>
    /// 房主开始游戏
    /// </summary>
    public (ReturnCode Code, GameStartNotify? Notify) StartGame(NetPeer peer)
    {
        foreach (var (roomId, room) in _rooms)
        {
            if (room.PlayerPeers.ContainsKey(peer))
            {
                if (room.OwnerPeer != peer)
                {
                    Log.Warning("[RoomManager] 开始游戏失败：不是房主 roomId={RoomId}", roomId);
                    return (ReturnCode.NotRoomOwner, null);
                }

                if (room.ReadyPeers.Count < room.PlayerPeers.Count)
                {
                    Log.Warning("[RoomManager] 开始游戏失败：玩家未全部准备 roomId={RoomId} ready={Ready}/{Total}",
                        roomId, room.ReadyPeers.Count, room.PlayerPeers.Count);
                    return (ReturnCode.NotAllReady, null);
                }

                var gs = GameServers[room.GameServerPeer];

                // 先通知 GameServer 创建游戏房间
                Send(room.GameServerPeer, MessageIds.CreateGameRoom, ReturnCode.Success, new CreateGameRoomRequest
                {
                    RoomId = roomId,
                    RoomType = room.Info.RoomType,
                    OwnerUserId = room.Info.OwnerUserId
                });

                // 再通知所有玩家连接 GameServer
                var notify = new GameStartNotify
                {
                    RoomId = roomId,
                    GameServerAddress = gs.Address,
                    GameServerPort = gs.Port
                };

                foreach (var p in room.PlayerPeers.Keys)
                {
                    Send(p, MessageIds.GameStartNotify, ReturnCode.Success, notify);
                }

                room.ReadyPeers.Clear();

                Log.Information("[RoomManager] 游戏开始 roomId={RoomId} GameServer={Addr}:{Port}",
                    roomId, gs.Address, gs.Port);
                return (ReturnCode.Success, notify);
            }
        }
        return (ReturnCode.NotInRoom, null);
    }

    /// <summary>
    /// 获取当前所有房间列表
    /// </summary>
    public RoomListResponse GetRoomList()
    {
        Log.Information("[RoomManager] 获取房间列表");

        var list = _rooms.Values.Select(r => new RoomListInfo
        {
            RoomId = r.Info.RoomId,
            RoomType = r.Info.RoomType,
            PlayerCount = r.PlayerPeers.Count
        }).ToList();

        Log.Information("[RoomManager] 查询房间列表 房间数={Count}", list.Count);
        return new RoomListResponse { Rooms = list };
    }

    /// <summary>
    /// 玩家断线时从所有房间中移除
    /// </summary>
    public void RemovePlayer(NetPeer peer)
    {
        Log.Information("[RoomManager] 移除玩家");

        foreach (var (roomId, room) in _rooms.ToList())
        {
            if (room.PlayerPeers.TryGetValue(peer, out var player) && room.PlayerPeers.Remove(peer))
            {
                room.ReadyPeers.Remove(peer);
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
                    var leaveNotify = new LeaveRoomNotify { RoomId = roomId, UserId = player.UserId };
                    foreach (var otherPeer in room.PlayerPeers.Keys)
                    {
                        Send(otherPeer, MessageIds.LeaveRoomNotify, ReturnCode.Success, leaveNotify);
                    }
                }
            }
        }
    }

    /// <summary>
    /// 房主离开时顺延给下一位玩家
    /// </summary>
    private void ReassignOwner(string roomId, LobbyRoom room)
    {
        Log.Information("[RoomManager] 重新分配房主 roomId={RoomId}", roomId);

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

    /// <summary>
    /// 选择玩家数最少的可用 GameServer
    /// </summary>
    private KeyValuePair<NetPeer, GameServerInfo>? PickGameServer()
    {
        Log.Information("[RoomManager] 选择GameServer");

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
            Log.Warning("[RoomManager] 选择GameServer失败：无可用GameServer");
        }

        return result;
    }

    /// <summary>
    /// 向指定 Peer 发送序列化的消息
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
