namespace SharedLib.Protocol;

/// <summary>
/// 消息 ID 常量
/// </summary>
public static class MessageIds
{
    public const ushort JoinLobby = 1;
    public const ushort LeaveLobby = 2;
    public const ushort Chat = 3;
    public const ushort ChatNotify = 4;

    // GameServer 内部通信
    public const ushort GameServerRegister = 100;
    public const ushort GameServerHeartbeat = 101;

    // 房间
    public const ushort CreateRoom = 10;
    public const ushort JoinRoom = 11;
    public const ushort LeaveRoom = 12;
    public const ushort JoinRoomNotify = 13;
    public const ushort RoomList = 14;

    // 准备与开始
    public const ushort GameReady = 20;
    public const ushort GameUnready = 21;
    public const ushort GameStart = 22;
    public const ushort GameStartNotify = 23;
    public const ushort CreateGameRoom = 24;
    // 游戏房间（GameServer 侧）
    public const ushort JoinGame = 30;
    public const ushort LeaveGame = 31;
    public const ushort JoinGameNotify = 32;
    // 同步
    public const ushort EntitySync = 40;
    public const ushort ObjectSpawn = 41;
    public const ushort ObjectDespawn = 42;
}
