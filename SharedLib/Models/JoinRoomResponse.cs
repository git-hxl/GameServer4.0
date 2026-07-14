using MessagePack;

namespace SharedLib.Models;

[MessagePackObject]
/// <summary>
/// 加入房间响应
/// </summary>
public class JoinRoomResponse
{
    [Key(0)] public string RoomId { get; set; } = string.Empty;
    [Key(1)] public string GameServerAddress { get; set; } = string.Empty;
    [Key(2)] public int GameServerPort { get; set; }
}
