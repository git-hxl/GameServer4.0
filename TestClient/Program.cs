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
var nickname = $"Test_{userId % 10000}";

// ── 初始化日志 ───────────────────────────────────────────────────────
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File("test_client_log.txt",
        rollingInterval: RollingInterval.Day,
        rollOnFileSizeLimit: true)
    .CreateLogger();

Log.Information("TestClient 启动, userId={UserId} nickname={Nickname}", userId, nickname);

// ── 创建 LiteNetLib 客户端 ───────────────────────────────────────────
var listener = new EventBasedNetListener();
var client   = new NetManager(listener);

// 连接成功
listener.PeerConnectedEvent += peer =>
{
    Log.Information("已连接到服务器");
};

// 断开连接
listener.PeerDisconnectedEvent += (peer, info) =>
{
    Log.Information("连接断开: {Reason}", info.Reason);
};

// 收到消息
listener.NetworkReceiveEvent += (peer, reader, channel, method) =>
{
    try
    {
        var messageId = reader.GetUShort();
        var payload   = reader.GetRemainingBytes();

        switch (messageId)
        {
            case MessageIds.JoinLobby:
                var joinRes = MessagePackSerializer.Deserialize<JoinLobbyResponse>(payload);
                Log.Information("[JoinLobby] 加入成功 userId={UserId} nickname={Nickname}",
                    joinRes?.UserId, joinRes?.Nickname);
                break;

            case MessageIds.LeaveLobby:
                var leaveRes = MessagePackSerializer.Deserialize<LeaveLobbyResponse>(payload);
                Log.Information("[LeaveLobby] 已离开 userId={UserId}", leaveRes?.UserId);
                break;

            case MessageIds.ChatNotify:
                var chatNotify = MessagePackSerializer.Deserialize<ChatNotify>(payload);
                Log.Information("[Chat] {Nickname}: {Content}",
                    chatNotify?.Nickname, chatNotify?.Content);
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
listener.NetworkErrorEvent += (endPoint, error) =>
{
    Log.Error("网络错误: {Error}, 来源: {EndPoint}", error, endPoint);
};

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
    writer.Put(MessagePackSerializer.Serialize(data));
    peer.Send(writer, DeliveryMethod.ReliableOrdered);
}

// ── 主循环 + 命令行交互 ────────────────────────────────────────────
using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(15));
var cts       = new CancellationTokenSource();
var quitFlag  = false;

Log.Information("命令: join | leave | chat <内容> | status | quit");

_ = Task.Run(async () =>
{
    while (!quitFlag)
    {
        var line = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(line))
            continue;

        var parts = line.Split(' ', 2);
        var cmd   = parts[0].ToLower();

        switch (cmd)
        {
            case "join":
                SendMessage(MessageIds.JoinLobby, new JoinLobbyRequest
                {
                    UserId = userId,
                    Nickname = nickname
                });
                break;

            case "leave":
                SendMessage(MessageIds.LeaveLobby, new LeaveLobbyRequest
                {
                    UserId = userId
                });
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
                    Nickname = nickname,
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
