using LiteNetLib;
using LiteNetLib.Utils;
using MessagePack;
using Serilog;
using SharedLib.Config;
using SharedLib.Models;
using SharedLib.Protocol;

namespace GameServer;

/// <summary>
/// GameServer：管理实际游戏房间逻辑，由 LobbyServer 调度
/// </summary>
public class GameServer
{
    private readonly NetManager _netManager;
    private readonly EventBasedNetListener _listener;

    // 连接 LobbyServer 的客户端
    private readonly NetManager _lobbyClient;
    private readonly EventBasedNetListener _lobbyListener;
    private NetPeer? _lobbyPeer;

    private readonly string _connectionKey;
    private readonly string _lobbyAddress;
    private readonly int _lobbyPort;
    private readonly int _serverPort;

    public GameServer(GameServerConfig config)
    {
        _connectionKey = config.ConnectionKey;
        _lobbyAddress = config.LobbyAddress;
        _lobbyPort = config.LobbyPort;
        _serverPort = config.Port;

        _listener = new EventBasedNetListener();
        _netManager = new NetManager(_listener)
        {
            UpdateTime = config.UpdateTime,
            PingInterval = config.PingInterval,
            DisconnectTimeout = config.DisconnectTimeout,
            ChannelsCount = config.ChannelsCount
        };

        _lobbyListener = new EventBasedNetListener();
        _lobbyClient = new NetManager(_lobbyListener)
        {
            UpdateTime = config.UpdateTime,
            PingInterval = config.PingInterval,
            DisconnectTimeout = config.DisconnectTimeout,
            ChannelsCount = config.ChannelsCount
        };
    }

    public void Start()
    {
        // ── 游戏客户端监听 ──
        _listener.ConnectionRequestEvent += OnConnectionRequest;
        _listener.PeerConnectedEvent += OnPeerConnected;
        _listener.PeerDisconnectedEvent += OnPeerDisconnected;
        _listener.NetworkReceiveEvent += OnNetworkReceive;
        _netManager.Start(_serverPort);

        // ── 连接 LobbyServer ──
        _lobbyListener.PeerConnectedEvent += OnLobbyConnected;
        _lobbyListener.PeerDisconnectedEvent += OnLobbyDisconnected;
        _lobbyListener.NetworkReceiveEvent += OnLobbyReceive;
        _lobbyClient.Start();
        _lobbyClient.Connect(_lobbyAddress, _lobbyPort, _connectionKey);

        Log.Information("GameServer 启动 端口 {Port}, 连接 Lobby {Addr}:{LobbyPort}",
            _serverPort, _lobbyAddress, _lobbyPort);
    }

    public void Stop()
    {
        if (_lobbyPeer != null)
            _lobbyClient.DisconnectPeer(_lobbyPeer);
        _lobbyClient.Stop();
        _netManager.Stop();
        Log.Information("GameServer 已停止");
    }

    public void PollEvents()
    {
        _netManager.PollEvents();
        _lobbyClient.PollEvents();
    }

    // ── 游戏客户端事件 ─────────────────────────────────────────────

    private void OnConnectionRequest(ConnectionRequest request)
    {
        request.AcceptIfKey(_connectionKey);
    }

    private void OnPeerConnected(NetPeer peer)
    {
        Log.Information("游戏客户端连接: {EndPoint}", peer.Address);
    }

    private void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
    {
        Log.Information("游戏客户端断开: {EndPoint}, 原因: {Reason}",
            peer.Address, disconnectInfo.Reason);
    }

    private void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod deliveryMethod)
    {
        Log.Information("游戏客户端消息 {ByteCount} 字节", reader.AvailableBytes);
        reader.Recycle();
    }

    // ── LobbyServer 通信 ───────────────────────────────────────────

    private void OnLobbyConnected(NetPeer peer)
    {
        _lobbyPeer = peer;
        Log.Information("已连接到 LobbyServer");

        var writer = new NetDataWriter();
        writer.Put(MessageIds.GameServerRegister);
        writer.Put(MessagePackSerializer.Serialize(new GameServerRegisterRequest
        {
            Port = _serverPort,
            PlayerCount = 0,
            RoomCount = 0
        }));
        peer.Send(writer, DeliveryMethod.ReliableOrdered);
        Log.Information("已向 LobbyServer 发送注册");
    }

    private void OnLobbyDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
    {
        _lobbyPeer = null;
        Log.Warning("与 LobbyServer 断开: {Reason}", disconnectInfo.Reason);
    }

    private void OnLobbyReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod deliveryMethod)
    {
        Log.Information("LobbyServer 消息 {ByteCount} 字节", reader.AvailableBytes);
        reader.Recycle();
    }
}
