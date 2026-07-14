using MessagePack;

namespace SharedLib.Models;

[MessagePackObject]
public class RoomListResponse
{
    [Key(0)] public List<RoomListInfo> Rooms { get; set; } = [];
}
