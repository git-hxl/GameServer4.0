using MessagePack;

namespace SharedLib.Models;

[MessagePackObject]
/// <summary>
/// 加入大厅响应
/// </summary>
public class JoinLobbyResponse
{
    [Key(0)] public PlayerInfo Player { get; set; } = new();
}
