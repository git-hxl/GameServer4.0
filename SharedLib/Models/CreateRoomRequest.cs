using MessagePack;

namespace SharedLib.Models;

[MessagePackObject]
/// <summary>
/// 创建房间请求
/// </summary>
public class CreateRoomRequest
{
    [Key(0)] public string? RoomId { get; set; }
    [Key(1)] public RoomType RoomType { get; set; } = RoomType.Default;
}
