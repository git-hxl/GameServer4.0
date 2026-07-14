using MessagePack;

namespace SharedLib.Models;

/// <summary>
/// 加入房间响应
/// </summary>
[MessagePackObject]
public class JoinRoomResponse
{
    [Key(0)] public string RoomId { get; set; } = string.Empty;
    [Key(1)] public string GameServerAddress { get; set; } = string.Empty;
    [Key(2)] public int GameServerPort { get; set; }
    [Key(3)] public List<PlayerInfo> Players { get; set; } = [];
    [Key(4)] public long OwnerUserId { get; set; }
}
