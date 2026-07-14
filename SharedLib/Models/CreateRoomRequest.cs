using MessagePack;

namespace SharedLib.Models;

[MessagePackObject]
public class CreateRoomRequest
{
    [Key(0)] public string? RoomId { get; set; }
    [Key(1)] public RoomType RoomType { get; set; } = RoomType.Default;
}
