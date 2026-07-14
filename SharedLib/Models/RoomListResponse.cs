using MessagePack;

namespace SharedLib.Models;

[MessagePackObject]
/// <summary>
/// 房间列表响应
/// </summary>
public class RoomListResponse
{
    [Key(0)] public List<RoomListInfo> Rooms { get; set; } = [];
}
