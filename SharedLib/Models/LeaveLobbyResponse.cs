using MessagePack;

namespace SharedLib.Models;

[MessagePackObject]
/// <summary>
/// 离开大厅响应
/// </summary>
public class LeaveLobbyResponse
{
    [Key(0)] public long UserId { get; set; }
}
