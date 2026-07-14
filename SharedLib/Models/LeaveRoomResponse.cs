using MessagePack;

namespace SharedLib.Models;

[MessagePackObject]
/// <summary>
/// 离开房间响应
/// </summary>
public class LeaveRoomResponse
{
    [Key(0)] public string RoomId { get; set; } = string.Empty;
}
