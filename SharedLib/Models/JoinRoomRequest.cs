using MessagePack;

namespace SharedLib.Models;

[MessagePackObject]
/// <summary>
/// 加入房间请求
/// </summary>
public class JoinRoomRequest
{
    [Key(0)] public string RoomId { get; set; } = string.Empty;
}
