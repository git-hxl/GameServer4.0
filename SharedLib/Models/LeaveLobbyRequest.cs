using MessagePack;

namespace SharedLib.Models;

[MessagePackObject]
/// <summary>
/// 离开大厅请求
/// </summary>
public class LeaveLobbyRequest
{
    [Key(0)] public long UserId { get; set; }
}
