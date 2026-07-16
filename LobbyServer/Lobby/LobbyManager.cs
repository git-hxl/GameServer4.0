using System.Collections.Concurrent;
using LiteNetLib;
using LiteNetLib.Utils;
using MessagePack;
using Serilog;
using SharedLib.Models;
using SharedLib.Protocol;

namespace LobbyServer.Lobby;

/// <summary>
/// 大厅管理器，负责玩家加入/离开大厅、聊天广播等功能
/// </summary>
public class LobbyManager
{
    private readonly NetManager _netManager;
    private readonly ConcurrentDictionary<long, NetPeer> _users = new();

    /// <summary>
    /// 当前在线用户数
    /// </summary>
    public int UserCount => _users.Count;

    /// <summary>
    /// 初始化大厅管理器
    /// </summary>
    public LobbyManager(NetManager netManager)
    {
        _netManager = netManager;
    }

    /// <summary>
    /// 处理玩家加入大厅请求
    /// </summary>
    public (JoinLobbyResponse Response, ReturnCode Code) Join(NetPeer peer, JoinLobbyRequest request)
    {
        var userId = request.Player.UserId;
        Log.Information("[LobbyManager] 加入大厅 userId={UserId}", userId);

        if (_users.TryGetValue(userId, out var existingPeer) && existingPeer != peer)
        {
            _users.TryRemove(userId, out _);
            Log.Warning("[LobbyManager] 替换已有连接 userId={UserId}", userId);
        }

        _users[userId] = peer;

        Log.Information("[LobbyManager] 大厅加入 userId={UserId} nickname={Nickname} 在线人数={Count}",
            userId, request.Player.Nickname, _users.Count);

        return (new JoinLobbyResponse { Player = request.Player }, ReturnCode.Success);
    }

    /// <summary>
    /// 处理玩家离开大厅请求
    /// </summary>
    public (LeaveLobbyResponse Response, ReturnCode Code) Leave(NetPeer peer, LeaveLobbyRequest request)
    {
        Log.Information("[LobbyManager] 离开大厅 userId={UserId}", request.UserId);

        if (!_users.ContainsKey(request.UserId))
        {
            Log.Warning("[LobbyManager] 离开大厅失败：用户不在大厅中 userId={UserId}", request.UserId);
            return (new LeaveLobbyResponse { UserId = request.UserId }, ReturnCode.NotInLobby);
        }

        _users.TryRemove(request.UserId, out _);

        Log.Information("[LobbyManager] 大厅离开 userId={UserId} 在线人数={Count}",
            request.UserId, _users.Count);

        return (new LeaveLobbyResponse { UserId = request.UserId }, ReturnCode.Success);
    }

    /// <summary>
    /// 处理大厅聊天消息并广播给所有在线玩家
    /// </summary>
    public void Chat(NetPeer peer, ChatRequest request)
    {
        Log.Information("[LobbyManager] 聊天消息 userId={UserId}", request.UserId);

        if (!_users.ContainsKey(request.UserId))
        {
            Log.Warning("[LobbyManager] 聊天被拒绝：用户不在大厅中 userId={UserId}", request.UserId);
            return;
        }

        Log.Information("[LobbyManager] 聊天 userId={UserId} nickname={Nickname} content={Content}",
            request.UserId, request.Nickname, request.Content);

        Broadcast(MessageIds.ChatNotify, ReturnCode.Success, new ChatNotify
        {
            UserId = request.UserId,
            Nickname = request.Nickname,
            Content = request.Content
        });
    }

    /// <summary>
    /// 根据网络对等体移除断线用户
    /// </summary>
    public void RemoveByPeer(NetPeer peer)
    {
        Log.Information("[LobbyManager] 按Peer移除用户");

        var kv = _users.FirstOrDefault(kv => kv.Value == peer);
        if (kv.Value != null)
        {
            _users.TryRemove(kv.Key, out _);
            Log.Information("[LobbyManager] 用户断线清理 userId={UserId} 在线人数={Count}", kv.Key, _users.Count);
        }
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

    /// <summary>
    /// 向所有在线用户广播消息
    /// </summary>
    private void Broadcast(ushort messageId, ReturnCode code, object data)
    {
        var writer = new NetDataWriter();
        writer.Put(messageId);
        writer.Put((byte)code);
        writer.Put(MessagePackSerializer.Serialize(data));

        foreach (var peer in _users.Values)
        {
            peer.Send(writer, DeliveryMethod.ReliableOrdered);
        }
    }
}
