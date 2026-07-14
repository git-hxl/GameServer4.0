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

public class LobbyServer
{
    public record GameServerInfo
    {
        public string Address { get; init; } = string.Empty;
        public int Port { get; init; }
        public int PlayerCount { get; set; }
        public int RoomCount { get; set; }
        public float CpuPercent { get; set; }
        public long MemoryMB { get; set; }
        public DateTime LastHeartbeat { get; set; }
    }
    private readonly NetManager _netManager;
    private readonly EventBasedNetListener _listener;
    private readonly string _connectionKey;
    private LobbyManager _lobbyManager = null!;
    private RoomManager _roomManager = null!;
    private readonly Dictionary<NetPeer, PlayerInfo> _players = new();

    private readonly ConcurrentDictionary<NetPeer, GameServerInfo> _gameServers = new();

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

    public void Start(int port)
    {
        _lobbyManager = new LobbyManager(_netManager);
        _roomManager = new RoomManager(_netManager) { GameServers = _gameServers };

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
        Log.Information("客户端断开: {EndPoint}, 原因: {Reason}",
            peer.Address, disconnectInfo.Reason);

        _lobbyManager.RemoveByPeer(peer);
        _roomManager.RemovePlayer(peer);
        _players.Remove(peer);
        _gameServers.TryRemove(peer, out _);
    }

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
                        _lobbyManager.Join(peer, joinReq);
                        _players[peer] = joinReq.Player;
                    }
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

                case MessageIds.GameServerRegister:
                    var regReq = MessagePackSerializer.Deserialize<GameServerRegisterRequest>(payload);
                    if (regReq != null)
                    {
#pragma warning disable CS0618
                        _gameServers[peer] = new GameServerInfo
                        {
                            Address = peer.Address.Address.ToString(),
#pragma warning restore CS0618
                            Port = regReq.Port,
                            PlayerCount = regReq.PlayerCount,
                            RoomCount = regReq.RoomCount,
                            LastHeartbeat = DateTime.UtcNow
                        };
                        Log.Information("GameServer 注册成功 端口={Port}", regReq.Port);
                    }
                    break;

                case MessageIds.GameServerHeartbeat:
                    var hbReq = MessagePackSerializer.Deserialize<GameServerHeartbeatRequest>(payload);
                    if (hbReq != null && _gameServers.TryGetValue(peer, out var info))
                    {
                        info.PlayerCount = hbReq.PlayerCount;
                        info.RoomCount = hbReq.RoomCount;
                        info.CpuPercent = hbReq.CpuPercent;
                        info.MemoryMB = hbReq.MemoryMB;
                        info.LastHeartbeat = DateTime.UtcNow;
                    }
                    break;

                case MessageIds.CreateRoom:
                    var createReq = MessagePackSerializer.Deserialize<CreateRoomRequest>(payload);
                    if (createReq != null && _players.TryGetValue(peer, out var createPlayer))
                    {
                        var (createRes, createCode) = _roomManager.CreateRoom(peer, createPlayer, createReq);
                        Send(peer, MessageIds.CreateRoom, createCode, createRes);
                    }
                    break;

                case MessageIds.JoinRoom:
                    var joinRoomReq = MessagePackSerializer.Deserialize<JoinRoomRequest>(payload);
                    if (joinRoomReq != null && _players.TryGetValue(peer, out var joinPlayer))
                    {
                        var (joinRoomRes, joinCode) = _roomManager.JoinRoom(peer, joinPlayer, joinRoomReq);
                        Send(peer, MessageIds.JoinRoom, joinCode, joinRoomRes);
                    }
                    break;

                case MessageIds.LeaveRoom:
                    var (leaveRoomRes, leaveCode) = _roomManager.LeaveRoom(peer);
                    Send(peer, MessageIds.LeaveRoom, leaveCode, leaveRoomRes);
                    break;

                case MessageIds.RoomList:
                    var roomListRes = _roomManager.GetRoomList();
                    Send(peer, MessageIds.RoomList, 0, roomListRes);
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

    private void Send(NetPeer peer, ushort messageId, ReturnCode code, object data)
    {
        var writer = new NetDataWriter();
        writer.Put(messageId);
        writer.Put((byte)code);
        writer.Put(MessagePackSerializer.Serialize(data));
        peer.Send(writer, DeliveryMethod.ReliableOrdered);
    }
}
