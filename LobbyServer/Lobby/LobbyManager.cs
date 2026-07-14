using System.Collections.Concurrent;
using LiteNetLib;
using LiteNetLib.Utils;
using MessagePack;
using Serilog;
using SharedLib.Models;
using SharedLib.Protocol;

namespace LobbyServer.Lobby;

public class LobbyManager
{
    private readonly NetManager _netManager;
    private readonly ConcurrentDictionary<long, NetPeer> _users = new();

    public int UserCount => _users.Count;

    public LobbyManager(NetManager netManager)
    {
        _netManager = netManager;
    }

    public void Join(NetPeer peer, JoinLobbyRequest request)
    {
        _users[request.Player.UserId] = peer;

        Log.Information("大厅加入 userId={UserId} nickname={Nickname} 在线人数={Count}",
            request.Player.UserId, request.Player.Nickname, _users.Count);

        Send(peer, MessageIds.JoinLobby, ReturnCode.Success, new JoinLobbyResponse
        {
            Player = request.Player
        });
    }

    public void Leave(NetPeer peer, LeaveLobbyRequest request)
    {
        _users.TryRemove(request.UserId, out _);

        Log.Information("大厅离开 userId={UserId} 在线人数={Count}",
            request.UserId, _users.Count);

        Send(peer, MessageIds.LeaveLobby, ReturnCode.Success, new LeaveLobbyResponse
        {
            UserId = request.UserId
        });
    }

    public void Chat(NetPeer peer, ChatRequest request)
    {
        if (!_users.ContainsKey(request.UserId))
        {
            Log.Warning("聊天拒绝 userId={UserId} 未加入大厅", request.UserId);
            return;
        }

        Log.Information("聊天 userId={UserId} nickname={Nickname}: {Content}",
            request.UserId, request.Nickname, request.Content);

        Broadcast(MessageIds.ChatNotify, ReturnCode.Success, new ChatNotify
        {
            UserId = request.UserId,
            Nickname = request.Nickname,
            Content = request.Content
        });
    }

    public void RemoveByPeer(NetPeer peer)
    {
        var kv = _users.FirstOrDefault(kv => kv.Value == peer);
        if (kv.Value != null)
        {
            _users.TryRemove(kv.Key, out _);
            Log.Information("用户断线清理 userId={UserId} 在线人数={Count}", kv.Key, _users.Count);
        }
    }

    private void Send(NetPeer peer, ushort messageId, ReturnCode code, object data)
    {
        var writer = new NetDataWriter();
        writer.Put(messageId);
        writer.Put((byte)code);
        writer.Put(MessagePackSerializer.Serialize(data));
        peer.Send(writer, DeliveryMethod.ReliableOrdered);
    }

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
