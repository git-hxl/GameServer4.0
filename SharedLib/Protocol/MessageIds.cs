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
}
