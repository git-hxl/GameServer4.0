using LiteNetLib;
using LiteNetLib.Utils;
using MessagePack;
using Serilog;
using SharedLib.Config;
using SharedLib.Models;
using SharedLib.Protocol;
using LobbyServer.Lobby;
using LobbyServer.Room;
using System.Collections.Concurrent;

namespace LobbyServer;

/// <summary>
/// 大厅服务器主类，负责客户端连接管理、消息路由和 GameServer 注册
/// </summary>
public class LobbyServer
{
    private readonly NetManager _netManager;
    private readonly EventBasedNetListener _listener;
    private readonly string _connectionKey;
    private LobbyManager _lobbyManager = null!;
    private RoomManager _roomManager = null!;
    private readonly Dictionary<NetPeer, PlayerInfo> _players = new();

    private readonly ConcurrentDictionary<NetPeer, GameServerInfo> _gameServers = new();
    private readonly ConcurrentDictionary<NetPeer, DateTime> _gameServerHeartbeat = new();

    /// <summary>
    /// 根据配置初始化大厅服务器
    /// </summary>
    public LobbyServer(LobbyServerConfig config)
    {
        _connectionKey = config.ConnectionKey;
        _listener = new EventBasedNetListener();
        _netManager = new NetManager(_listener)
        {
            UpdateTime = config.UpdateTime,
            PingInterval = config.PingInterval,
            DisconnectTimeout = config.DisconnectTimeout,
            ChannelsCount = config.ChannelsCount
        };
    }

    /// <summary>
    /// 启动大厅服务器，初始化子模块并绑定网络事件回调
    /// </summary>
    public void Start(int port)
    {
        _lobbyManager = new LobbyManager(_netManager);
        _roomManager = new RoomManager(_netManager) { GameServers = _gameServers };

        _listener.ConnectionRequestEvent += OnConnectionRequest;
        _listener.PeerConnectedEvent += OnPeerConnected;
        _listener.PeerDisconnectedEvent += OnPeerDisconnected;
        _listener.NetworkReceiveEvent += OnNetworkReceive;

        _netManager.Start(port);
        Log.Information("[LobbyServer] 大厅服务器启动 port={Port}", port);
    }

    /// <summary>
    /// 停止大厅服务器
    /// </summary>
    public void Stop()
    {
        _netManager.Stop();
        Log.Information("[LobbyServer] 大厅服务器已停止");
    }

    /// <summary>
    /// 轮询处理网络事件
    /// </summary>
    public void PollEvents()
    {
        _netManager.PollEvents();
    }

    /// <summary>
    /// 处理连接请求并进行密钥验证
    /// </summary>
    private void OnConnectionRequest(ConnectionRequest request)
    {
        request.AcceptIfKey(_connectionKey);
    }

    /// <summary>
    /// 处理客户端连接成功事件
    /// </summary>
    private void OnPeerConnected(NetPeer peer)
    {
        Log.Information("[LobbyServer] 客户端连接 endpoint={EndPoint}", peer.Address);
    }

    /// <summary>
    /// 处理客户端断开连接，清理大厅、房间和玩家数据
    /// </summary>
    private void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
    {
        Log.Information("[LobbyServer] 客户端断开 endpoint={EndPoint} reason={Reason}",
            peer.Address, disconnectInfo.Reason);

        _lobbyManager.RemoveByPeer(peer);
        _roomManager.RemovePlayer(peer);
        _players.Remove(peer);
        _gameServers.TryRemove(peer, out _);
        _gameServerHeartbeat.TryRemove(peer, out _);
    }

    /// <summary>
    /// 处理收到的网络消息，根据消息 ID 分发到对应的处理器
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
                case MessageIds.JoinLobby:
                    var joinReq = MessagePackSerializer.Deserialize<JoinLobbyRequest>(payload);
                    if (joinReq != null)
                    {
                        var (joinLobbyRes, joinLobbyCode) = _lobbyManager.Join(peer, joinReq);
                        _players[peer] = joinReq.Player;
                        Send(peer, MessageIds.JoinLobby, joinLobbyCode, joinLobbyRes);
                    }
                    else
                    {
                        Log.Warning("[LobbyServer] JoinLobby 反序列化失败");
                        Send(peer, MessageIds.JoinLobby, ReturnCode.DeserializeFailed, new JoinLobbyResponse());
                    }
                    break;

                case MessageIds.LeaveLobby:
                    var leaveReq = MessagePackSerializer.Deserialize<LeaveLobbyRequest>(payload);
                    if (leaveReq != null)
                    {
                        var (leaveLobbyRes, leaveLobbyCode) = _lobbyManager.Leave(peer, leaveReq);
                        Send(peer, MessageIds.LeaveLobby, leaveLobbyCode, leaveLobbyRes);
                    }
                    else
                    {
                        Log.Warning("[LobbyServer] LeaveLobby 反序列化失败");
                        Send(peer, MessageIds.LeaveLobby, ReturnCode.DeserializeFailed, new LeaveLobbyResponse());
                    }
                    break;

                case MessageIds.Chat:
                    var chatReq = MessagePackSerializer.Deserialize<ChatRequest>(payload);
                    if (chatReq != null)
                        _lobbyManager.Chat(peer, chatReq);
                    else
                        Log.Warning("[LobbyServer] Chat 反序列化失败");
                    break;

                case MessageIds.GameServerRegister:
                    var regInfo = MessagePackSerializer.Deserialize<GameServerInfo>(payload);
                    if (regInfo != null)
                    {
                        var ep = peer.Address;
                        var epStr = ep.ToString();
                        var colon = epStr.LastIndexOf(':');
                        regInfo.Address = colon > 0 ? epStr[..colon] : epStr;
                        _gameServers[peer] = regInfo;
                        _gameServerHeartbeat[peer] = DateTime.UtcNow;
                        Log.Information("[LobbyServer] GameServer 注册成功 port={Port}", regInfo.Port);
                    }
                    else
                    {
                        Log.Warning("[LobbyServer] GameServerRegister 反序列化失败");
                    }
                    break;

                case MessageIds.GameServerHeartbeat:
                    var hbInfo = MessagePackSerializer.Deserialize<GameServerInfo>(payload);
                    if (hbInfo == null)
                    {
                        Log.Warning("[LobbyServer] GameServerHeartbeat 反序列化失败");
                    }
                    else if (!_gameServers.TryGetValue(peer, out var gsInfo))
                    {
                        Log.Warning("[LobbyServer] 收到未注册 GameServer 的心跳");
                    }
                    else
                    {
                        gsInfo.PlayerCount = hbInfo.PlayerCount;
                        gsInfo.RoomCount = hbInfo.RoomCount;
                        gsInfo.CpuPercent = hbInfo.CpuPercent;
                        gsInfo.MemoryMB = hbInfo.MemoryMB;
                        _gameServerHeartbeat[peer] = DateTime.UtcNow;
                        Log.Information("[LobbyServer] GameServer 心跳 port={Port} playerCount={PlayerCount} roomCount={RoomCount} cpu={Cpu:F1}% memory={Mem}MB",
                            hbInfo.Port, hbInfo.PlayerCount, hbInfo.RoomCount, hbInfo.CpuPercent, hbInfo.MemoryMB);
                    }
                    break;

                case MessageIds.CreateRoom:
                    var createReq = MessagePackSerializer.Deserialize<CreateRoomRequest>(payload);
                    if (createReq != null && _players.TryGetValue(peer, out var createPlayer))
                    {
                        var (createRes, createCode) = _roomManager.CreateRoom(peer, createPlayer, createReq);
                        Send(peer, MessageIds.CreateRoom, createCode, createRes);
                    }
                    else if (createReq == null)
                    {
                        Log.Warning("[LobbyServer] CreateRoom 反序列化失败");
                        Send(peer, MessageIds.CreateRoom, ReturnCode.DeserializeFailed, new CreateRoomResponse());
                    }
                    else
                    {
                        Log.Warning("[LobbyServer] 创建房间失败：未找到玩家信息");
                        Send(peer, MessageIds.CreateRoom, ReturnCode.DeserializeFailed, new CreateRoomResponse());
                    }
                    break;

                case MessageIds.JoinRoom:
                    var joinRoomReq = MessagePackSerializer.Deserialize<JoinRoomRequest>(payload);
                    if (joinRoomReq != null && _players.TryGetValue(peer, out var joinPlayer))
                    {
                        var (joinRoomRes, joinCode) = _roomManager.JoinRoom(peer, joinPlayer, joinRoomReq);
                        Send(peer, MessageIds.JoinRoom, joinCode, joinRoomRes);
                    }
                    else if (joinRoomReq == null)
                    {
                        Log.Warning("[LobbyServer] JoinRoom 反序列化失败");
                        Send(peer, MessageIds.JoinRoom, ReturnCode.DeserializeFailed, new JoinRoomResponse());
                    }
                    else
                    {
                        Log.Warning("[LobbyServer] 加入房间失败：未找到玩家信息");
                        Send(peer, MessageIds.JoinRoom, ReturnCode.NotInLobby, new JoinRoomResponse());
                    }
                    break;

                case MessageIds.LeaveRoom:
                    var (leaveRoomRes, leaveCode) = _roomManager.LeaveRoom(peer);
                    Send(peer, MessageIds.LeaveRoom, leaveCode, leaveRoomRes);
                    break;

                case MessageIds.RoomList:
                    var roomListRes = _roomManager.GetRoomList();
                    Send(peer, MessageIds.RoomList, ReturnCode.Success, roomListRes);
                    break;

                case MessageIds.GameReady:
                    var (readyRes, readyCode) = _roomManager.SetReady(peer);
                    Send(peer, MessageIds.GameReady, readyCode, readyRes);
                    break;

                case MessageIds.GameUnready:
                    var (unreadyRes, unreadyCode) = _roomManager.SetUnready(peer);
                    Send(peer, MessageIds.GameUnready, unreadyCode, unreadyRes);
                    break;

                case MessageIds.GameStart:
                    var (startCode, startNotify) = _roomManager.StartGame(peer);
                    Send(peer, MessageIds.GameStart, startCode, new GameStartResponse { Code = (int)startCode });
                    break;

                default:
                    Log.Warning("[LobbyServer] 未知消息ID messageId={MessageId}", messageId);
                    Send(peer, messageId, ReturnCode.Error, new { });
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[LobbyServer] 消息处理异常");
        }
        finally
        {
            reader.Recycle();
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
}
