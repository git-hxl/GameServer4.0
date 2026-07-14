using MessagePack;

namespace SharedLib.Models;

[MessagePackObject]
/// <summary>
/// 加入大厅请求
/// </summary>
public class JoinLobbyRequest
{
    [Key(0)] public PlayerInfo Player { get; set; } = new();
}
