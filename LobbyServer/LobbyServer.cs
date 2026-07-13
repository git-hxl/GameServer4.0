using LiteNetLib;
using MessagePack;
using Serilog;
using SharedLib.Config;
using SharedLib.Models;
using SharedLib.Protocol;
using LobbyServer.Lobby;

namespace LobbyServer;

public class LobbyServer
{
    private readonly NetManager _netManager;
    private readonly EventBasedNetListener _listener;
    private readonly string _connectionKey;
    private LobbyManager _lobbyManager = null!;

    public LobbyServer(NetworkConfig networkConfig)
    {
        _listener = new EventBasedNetListener();
        _netManager = new NetManager(_listener)
        {
            UpdateTime = networkConfig.UpdateTime,
            PingInterval = networkConfig.PingInterval,
            DisconnectTimeout = networkConfig.DisconnectTimeout,
            ChannelsCount = networkConfig.ChannelsCount
        };
        _connectionKey = networkConfig.ConnectionKey;
    }

    public void Start(int port)
    {
        _lobbyManager = new LobbyManager(_netManager);

        _listener.ConnectionRequestEvent += OnConnectionRequest;
        _listener.PeerConnectedEvent += OnPeerConnected;
        _listener.PeerDisconnectedEvent += OnPeerDisconnected;
        _listener.NetworkReceiveEvent += OnNetworkReceive;

        _netManager.Start(port);
        Log.Information("Lobby server started on port {Port}", port);
    }

    public void Stop()
    {
        _netManager.Stop();
        Log.Information("Lobby server stopped");
    }

    public void PollEvents()
    {
        _netManager.PollEvents();
    }

    private void OnConnectionRequest(ConnectionRequest request)
    {
        request.AcceptIfKey(_connectionKey);
    }

    private void OnPeerConnected(NetPeer peer)
    {
        Log.Information("Client connected: {EndPoint}", peer.Address);
    }

    private void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
    {
        Log.Information("Client disconnected: {EndPoint}, reason: {Reason}",
            peer.Address, disconnectInfo.Reason);

        _lobbyManager.RemoveByPeer(peer);
    }

    private void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod deliveryMethod)
    {
        try
        {
            var messageId = reader.GetUShort();
            var payload   = reader.GetRemainingBytes();

            switch (messageId)
            {
                case MessageIds.JoinLobby:
                    var joinReq = MessagePackSerializer.Deserialize<JoinLobbyRequest>(payload);
                    if (joinReq != null)
                        _lobbyManager.Join(peer, joinReq);
                    break;

                case MessageIds.LeaveLobby:
                    var leaveReq = MessagePackSerializer.Deserialize<LeaveLobbyRequest>(payload);
                    if (leaveReq != null)
                        _lobbyManager.Leave(peer, leaveReq);
                    break;

                case MessageIds.Chat:
                    var chatReq = MessagePackSerializer.Deserialize<ChatRequest>(payload);
                    if (chatReq != null)
                        _lobbyManager.Chat(peer, chatReq);
                    break;

                default:
                    Log.Warning("未知消息 ID: {MessageId}", messageId);
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "消息处理失败");
        }
        finally
        {
            reader.Recycle();
        }
    }
}
