using MessagePack;

namespace SharedLib.Models;

[MessagePackObject]
/// <summary>
/// 创建房间响应
/// </summary>
public class CreateRoomResponse
{
    [Key(0)] public string RoomId { get; set; } = string.Empty;
    [Key(1)] public string GameServerAddress { get; set; } = string.Empty;
    [Key(2)] public int GameServerPort { get; set; }
    [Key(3)] public RoomType RoomType { get; set; }
}
