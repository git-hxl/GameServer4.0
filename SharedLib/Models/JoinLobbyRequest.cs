using MessagePack;

namespace SharedLib.Models;

[MessagePackObject]
public class JoinLobbyRequest
{
    [Key(0)] public PlayerInfo Player { get; set; } = new();
}
