using LiteNetLib;
using LiteNetLib.Utils;
using MessagePack;
using Serilog;
using SharedLib.Models;
using SharedLib.Protocol;
using System.Collections.Concurrent;

namespace LobbyServer.Lobby;

/// <summary>
/// 大厅用户管理 + 消息广播
/// </summary>
public class LobbyManager
{
    private readonly NetManager _netManager;
    private readonly ConcurrentDictionary<long, NetPeer> _users = new();

    public int UserCount => _users.Count;

    public LobbyManager(NetManager netManager)
    {
        _netManager = netManager;
    }

    /// <summary>
    /// 加入大厅
    /// </summary>
    public void Join(NetPeer peer, JoinLobbyRequest request)
    {
        _users[request.UserId] = peer;

        Log.Information("大厅加入 userId={UserId} nickname={Nickname} 在线人数={Count}",
            request.UserId, request.Nickname, _users.Count);

        // 回复自己
        Send(peer, MessageIds.JoinLobby, new JoinLobbyResponse
        {
            UserId = request.UserId,
            Nickname = request.Nickname
        });
    }

    /// <summary>
    /// 离开大厅
    /// </summary>
    public void Leave(NetPeer peer, LeaveLobbyRequest request)
    {
        _users.TryRemove(request.UserId, out _);

        Log.Information("大厅离开 userId={UserId} 在线人数={Count}",
            request.UserId, _users.Count);

        // 回复自己
        Send(peer, MessageIds.LeaveLobby, new LeaveLobbyResponse
        {
            UserId = request.UserId
        });
    }

    /// <summary>
    /// 聊天消息：全大厅广播（需已加入大厅）
    /// </summary>
    public void Chat(NetPeer peer, ChatRequest request)
    {
        if (!_users.ContainsKey(request.UserId))
        {
            Log.Warning("聊天拒绝 userId={UserId} 未加入大厅", request.UserId);
            return;
        }

        Log.Information("聊天 userId={UserId} nickname={Nickname}: {Content}",
            request.UserId, request.Nickname, request.Content);

        Broadcast(MessageIds.ChatNotify, new ChatNotify
        {
            UserId = request.UserId,
            Nickname = request.Nickname,
            Content = request.Content
        });
    }

    /// <summary>
    /// 用户断开时清理
    /// </summary>
    public void RemoveByPeer(NetPeer peer)
    {
        var kv = _users.FirstOrDefault(kv => kv.Value == peer);
        if (kv.Value != null)
        {
            _users.TryRemove(kv.Key, out _);
            Log.Information("用户断线清理 userId={UserId} 在线人数={Count}", kv.Key, _users.Count);
        }
    }

    /// <summary>
    /// 发送消息给指定 Peer（MessageId + MessagePack 序列化）
    /// </summary>
    private void Send(NetPeer peer, ushort messageId, object data)
    {
        var writer = new NetDataWriter();
        writer.Put(messageId);
        writer.Put(MessagePackSerializer.Serialize(data));
        peer.Send(writer, DeliveryMethod.ReliableOrdered);
    }

    /// <summary>
    /// 广播消息给所有大厅内用户
    /// </summary>
    private void Broadcast(ushort messageId, object data)
    {
        var writer = new NetDataWriter();
        writer.Put(messageId);
        writer.Put(MessagePackSerializer.Serialize(data));

        foreach (var peer in _users.Values)
        {
            peer.Send(writer, DeliveryMethod.ReliableOrdered);
        }
    }
}
