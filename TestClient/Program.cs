using LiteNetLib;
using LiteNetLib.Utils;
using MessagePack;
using Serilog;
using SharedLib.Models;
using SharedLib.Protocol;

// ── 测试用户配置 ─────────────────────────────────────────────────────
const int ServerPort = 9050;
const string ConnectionKey = "Game@wasd9527";
var userId = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
var player = new PlayerInfo
{
    UserId = userId,
    Nickname = $"Test_{userId % 10000}",
    Gender = 1,
    Avatar = "default.png",
    Age = 20
};

// ── 初始化日志 ───────────────────────────────────────────────────────
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File("test_client_log.txt",
        rollingInterval: RollingInterval.Day,
        rollOnFileSizeLimit: true)
    .CreateLogger();

Log.Information("TestClient 启动, userId={UserId} nickname={Nickname}", userId, player.Nickname);

// ── 创建 LiteNetLib 客户端 ───────────────────────────────────────────
var listener = new EventBasedNetListener();
var client = new NetManager(listener);

// 连接成功
listener.PeerConnectedEvent += peer => { Log.Information("已连接到服务器"); };

// 断开连接
listener.PeerDisconnectedEvent += (peer, info) => { Log.Information("连接断开: {Reason}", info.Reason); };

// 收到消息
listener.NetworkReceiveEvent += (peer, reader, channel, method) =>
{
    try
    {
        var messageId = reader.GetUShort();
        var code = reader.GetByte();
        var payload = reader.GetRemainingBytes();

        switch (messageId)
        {
            case MessageIds.JoinLobby:
                var joinRes = MessagePackSerializer.Deserialize<JoinLobbyResponse>(payload);
                if (code == 0)
                    Log.Information("[JoinLobby] 成功 userId={UserId} nickname={Nickname}",
                        joinRes?.Player.UserId, joinRes?.Player.Nickname);
                else
                    Log.Warning("[JoinLobby] 失败 code={Code}", code);
                break;

            case MessageIds.LeaveLobby:
                var leaveRes = MessagePackSerializer.Deserialize<LeaveLobbyResponse>(payload);
                if (code == 0)
                    Log.Information("[LeaveLobby] 成功 userId={UserId}", leaveRes?.UserId);
                else
                    Log.Warning("[LeaveLobby] 失败 code={Code}", code);
                break;

            case MessageIds.ChatNotify:
                var chatNotify = MessagePackSerializer.Deserialize<ChatNotify>(payload);
                Log.Information("[Chat] {Nickname}: {Content}",
                    chatNotify?.Nickname, chatNotify?.Content);
                break;

            case MessageIds.CreateRoom:
                var createRes = MessagePackSerializer.Deserialize<CreateRoomResponse>(payload);
                if (code == 0)
                    Log.Information("[房间] 创建成功 roomId={RoomId} GameServer={Addr}:{Port}",
                        createRes?.Room.RoomId, createRes?.Room.GameServerAddress, createRes?.Room.GameServerPort);
                else
                    Log.Warning("[房间] 创建失败");
                break;

            case MessageIds.JoinRoom:
                var joinRoomRes = MessagePackSerializer.Deserialize<JoinRoomResponse>(payload);
                if (code == 0)
                {
                    var r = joinRoomRes?.Room;
                    Log.Information("[房间] 加入成功 roomId={RoomId} GameServer={Addr}:{Port} 房主={Owner}",
                        r?.RoomId, r?.GameServerAddress, r?.GameServerPort, r?.OwnerUserId);
                    if (r?.Players is { Count: > 0 })
                    {
                        Log.Information("[房间] 现有成员:");
                        foreach (var p in r.Players)
                            Log.Information("  {Nickname}({UserId})", p.Nickname, p.UserId);
                    }
                }
                else
                    Log.Warning("[房间] 加入失败");

                break;

            case MessageIds.LeaveRoom:
                var leaveRoomRes = MessagePackSerializer.Deserialize<LeaveRoomResponse>(payload);
                if (code == 0)
                    Log.Information("[房间] 已离开 roomId={RoomId}", leaveRoomRes?.RoomId);
                else
                    Log.Warning("[房间] 离开失败");
                break;

            case MessageIds.JoinRoomNotify:
                var joinNotify = MessagePackSerializer.Deserialize<JoinRoomNotify>(payload);
                Log.Information("[房间] {Nickname}({UserId}) 加入了房间 {RoomId}",
                    joinNotify?.Player.Nickname, joinNotify?.Player.UserId, joinNotify?.RoomId);
                break;

            case MessageIds.RoomList:
                var roomList = MessagePackSerializer.Deserialize<RoomListResponse>(payload);
                if (roomList?.Rooms.Count > 0)
                {
                    Log.Information("[房间列表]");
                    foreach (var r in roomList.Rooms)
                        Log.Information("  {RoomId} 类型={Type} 人数={Count}", r.RoomId, r.RoomType, r.PlayerCount);
                }
                else
                {
                    Log.Information("[房间列表] 空");
                }

                break;

            default:
                Log.Information("收到未知消息 ID: {MessageId}", messageId);
                break;
        }
    }
    catch (Exception ex)
    {
        Log.Error(ex, "消息解析失败");
    }
    finally
    {
        reader.Recycle();
    }
};

// 网络错误
listener.NetworkErrorEvent += (endPoint, error) => { Log.Error("网络错误: {Error}, 来源: {EndPoint}", error, endPoint); };

// ── 启动并连接 ──────────────────────────────────────────────────────
client.Start();
client.Connect("localhost", ServerPort, ConnectionKey);

// ── 发送消息帮助方法 ────────────────────────────────────────────────
void SendMessage(ushort messageId, object data)
{
    var peer = client.FirstPeer;
    if (peer?.ConnectionState != ConnectionState.Connected)
    {
        Log.Warning("未连接到服务器");
        return;
    }

    var writer = new NetDataWriter();
    writer.Put(messageId);
    writer.Put((byte)ReturnCode.Success);
    writer.Put(MessagePackSerializer.Serialize(data));
    peer.Send(writer, DeliveryMethod.ReliableOrdered);
}

// ── 主循环 + 命令行交互 ────────────────────────────────────────────
using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(15));
var cts = new CancellationTokenSource();
var quitFlag = false;

Log.Information(
    "命令: joinlobby | leavelobby | chat <内容> | createroom | joinroom <roomId> | leaveroom | rooms | status | quit");

_ = Task.Run(async () =>
{
    while (!quitFlag)
    {
        var line = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(line))
            continue;

        var parts = line.Split(' ', 2);
        var cmd = parts[0].ToLower();

        switch (cmd)
        {
            case "joinlobby":
                SendMessage(MessageIds.JoinLobby, new JoinLobbyRequest { Player = player });
                break;

            case "leavelobby":
                SendMessage(MessageIds.LeaveLobby, new LeaveLobbyRequest
                {
                    UserId = userId
                });
                break;

            case "createroom":
                SendMessage(MessageIds.CreateRoom, new CreateRoomRequest());
                break;

            case "joinroom":
                if (parts.Length < 2)
                {
                    Log.Information("用法: joinroom <RoomId>");
                    break;
                }

                SendMessage(MessageIds.JoinRoom, new JoinRoomRequest
                {
                    RoomId = parts[1]
                });
                break;

            case "leaveroom":
                SendMessage(MessageIds.LeaveRoom, new LeaveRoomRequest());
                break;

            case "rooms":
                SendMessage(MessageIds.RoomList, new RoomListRequest());
                break;

            case "chat":
                if (parts.Length < 2)
                {
                    Log.Information("用法: chat <内容>");
                    break;
                }

                SendMessage(MessageIds.Chat, new ChatRequest
                {
                    UserId = userId,
                    Nickname = player.Nickname,
                    Content = parts[1]
                });
                break;

            case "status":
                var p = client.FirstPeer;
                Log.Information("状态: {State}, 延迟: {Latency}ms",
                    p?.ConnectionState, p?.Ping);
                break;

            case "quit":
                var peer = client.FirstPeer;
                if (peer != null)
                {
                    client.DisconnectPeer(peer);
                    Log.Information("已断开服务器连接");
                }

                quitFlag = true;
                cts.Cancel();
                break;
        }
    }
});

// 网络轮询循环
try
{
    while (await timer.WaitForNextTickAsync(cts.Token))
    {
        client.PollEvents();
    }
}
catch (OperationCanceledException)
{
    // 正常退出
}

// ── 关闭 ────────────────────────────────────────────────────────────
client.Stop();
Log.Information("TestClient 已关闭");