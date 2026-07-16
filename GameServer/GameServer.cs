using LiteNetLib;
using LiteNetLib.Utils;
using MessagePack;
using Serilog;
using SharedLib.Config;
using SharedLib.Models;
using SharedLib.Protocol;
using SharedLib.Utils;
using GameServer.Room;

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
    private volatile NetPeer? _lobbyPeer;

    private readonly string _connectionKey;
    private readonly string _lobbyAddress;
    private readonly int _lobbyPort;
    private readonly int _serverPort;

    private CancellationTokenSource _heartbeatCts = new();
    private readonly PerformanceMonitor _perf = new();
    private GameRoomManager _roomManager = null!;

    /// <summary>
    /// 根据配置初始化 NetManager 和 LobbyClient
    /// </summary>
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

    /// <summary>
    /// 启动游戏客户端监听、连接 LobbyServer 并开始心跳
    /// </summary>
    public void Start()
    {
        _roomManager = new GameRoomManager(_netManager);

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

        // ── 心跳 ──
        _heartbeatCts = new CancellationTokenSource();
        _ = HeartbeatLoop(_heartbeatCts.Token);

        Log.Information("[GameServer] 启动 port={Port} lobbyAddress={Addr}:{LobbyPort}",
            _serverPort, _lobbyAddress, _lobbyPort);
    }

    /// <summary>
    /// 停止心跳、断开 LobbyServer 连接并停止 NetManager
    /// </summary>
    public void Stop()
    {
        _heartbeatCts.Cancel();
        if (_lobbyPeer != null)
            _lobbyClient.DisconnectPeer(_lobbyPeer);
        _lobbyClient.Stop();
        _netManager.Stop();
        Log.Information("[GameServer] 已停止");
    }

    /// <summary>
    /// 轮询 NetManager 和 LobbyClient 的网络事件
    /// </summary>
    public void PollEvents()
    {
        _netManager.PollEvents();
        _lobbyClient.PollEvents();
    }

    // ── 游戏客户端事件 ─────────────────────────────────────────────

    /// <summary>
    /// 验证客户端连接密钥，通过则接受连接
    /// </summary>
    private void OnConnectionRequest(ConnectionRequest request)
    {
        request.AcceptIfKey(_connectionKey);
    }

    /// <summary>
    /// 游戏客户端连接成功时记录日志
    /// </summary>
    private void OnPeerConnected(NetPeer peer)
    {
        Log.Information("[GameServer] 游戏客户端连接 endpoint={EndPoint}", peer.Address);
    }

    /// <summary>
    /// 游戏客户端断开连接时记录日志
    /// </summary>
    private void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
    {
        Log.Information("[GameServer] 游戏客户端断开 endpoint={EndPoint} reason={Reason}",
            peer.Address, disconnectInfo.Reason);
        _roomManager.RemovePlayer(peer);
    }

    /// <summary>
    /// 接收游戏客户端网络消息并回收 reader
    /// </summary>
    private void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod deliveryMethod)
    {
        try
        {
            var messageId = reader.GetUShort();
            var code      = reader.GetByte();
            var payload   = reader.GetRemainingBytes();

            switch (messageId)
            {
                case MessageIds.JoinGame:
                    var joinReq = MessagePackSerializer.Deserialize<JoinGameRequest>(payload);
                    if (joinReq != null)
                    {
                        var (joinRes, joinCode) = _roomManager.JoinGame(peer, joinReq);
                        Send(peer, MessageIds.JoinGame, joinCode, joinRes);
                    }
                    else
                    {
                        Log.Warning("[GameServer] JoinGame 反序列化失败");
                        Send(peer, MessageIds.JoinGame, ReturnCode.DeserializeFailed, new JoinGameResponse());
                    }
                    break;

                case MessageIds.LeaveGame:
                    var (leaveCode, leaveRoomId) = _roomManager.LeaveGame(peer);
                    Send(peer, MessageIds.LeaveGame, leaveCode, new { RoomId = leaveRoomId ?? "" });
                    break;

                default:
                    Log.Warning("[GameServer] 未知游戏客户端消息ID messageId={MessageId}", messageId);
                    Send(peer, messageId, ReturnCode.Error, new { });
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[GameServer] 游戏客户端消息处理异常");
        }
        finally
        {
            reader.Recycle();
        }
    }

    // ── LobbyServer 通信 ───────────────────────────────────────────

    /// <summary>
    /// 连接 LobbyServer 成功后发送注册请求
    /// </summary>
    private void OnLobbyConnected(NetPeer peer)
    {
        _lobbyPeer = peer;
        Log.Information("[GameServer] 已连接到LobbyServer");

        var writer = new NetDataWriter();
        writer.Put(MessageIds.GameServerRegister);
        writer.Put((byte)ReturnCode.Success);
        writer.Put(MessagePackSerializer.Serialize(new GameServerInfo
        {
            Port = _serverPort,
            PlayerCount = 0,
            RoomCount = 0
        }));
        peer.Send(writer, DeliveryMethod.ReliableOrdered);
        Log.Information("[GameServer] 已向LobbyServer发送注册");
    }

    /// <summary>
    /// 与 LobbyServer 断开连接时清理 peer 引用并记录日志
    /// </summary>
    private void OnLobbyDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
    {
        _lobbyPeer = null;
        Log.Warning("[GameServer] 断开LobbyServer连接 reason={Reason}", disconnectInfo.Reason);
    }

    /// <summary>
    /// 接收 LobbyServer 网络消息并回收 reader
    /// </summary>
    private void OnLobbyReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod deliveryMethod)
    {
        try
        {
            var messageId = reader.GetUShort();
            var code      = reader.GetByte();
            var payload   = reader.GetRemainingBytes();

            switch (messageId)
            {
                case MessageIds.CreateGameRoom:
                    var roomReq = MessagePackSerializer.Deserialize<CreateGameRoomRequest>(payload);
                    if (roomReq != null)
                    {
                        _roomManager.CreateRoom(roomReq);
                    }
                    else
                    {
                        Log.Warning("[GameServer] CreateGameRoom 反序列化失败");
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[GameServer] LobbyServer消息处理异常");
        }
        finally
        {
            reader.Recycle();
        }
    }

    // ── 心跳 ─────────────────────────────────────────────────────────

    /// <summary>
    /// 每 5 秒发送一次心跳到 LobbyServer
    /// </summary>
    private async Task HeartbeatLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(5000, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            _perf.Update();

            if (_lobbyPeer?.ConnectionState == ConnectionState.Connected)
            {
                SendHeartbeat();
            }
        }
    }

    /// <summary>
    /// 组装并发送 GameServerHeartbeat 消息到 LobbyServer
    /// </summary>
    private void SendHeartbeat()
    {
        var writer = new NetDataWriter();
        writer.Put(MessageIds.GameServerHeartbeat);
        writer.Put((byte)ReturnCode.Success);
        writer.Put(MessagePackSerializer.Serialize(new GameServerInfo
        {
            Port = _serverPort,
            PlayerCount = _roomManager.PlayerCount,
            RoomCount = _roomManager.RoomCount,
            CpuPercent = _perf.CpuPercent,
            MemoryMB = _perf.MemoryMB
        }));
        _lobbyPeer!.Send(writer, DeliveryMethod.ReliableOrdered);
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
