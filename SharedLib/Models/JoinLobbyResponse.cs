using MessagePack;

namespace SharedLib.Models;

[MessagePackObject]
public class JoinLobbyResponse
{
    [Key(0)] public long UserId { get; set; }
    [Key(1)] public string Nickname { get; set; } = string.Empty;
}
