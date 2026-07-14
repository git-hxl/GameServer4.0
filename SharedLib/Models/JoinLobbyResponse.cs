using MessagePack;

namespace SharedLib.Models;

[MessagePackObject]
public class JoinLobbyResponse
{
    [Key(0)] public PlayerInfo Player { get; set; } = new();
}
