using MessagePack;

namespace SharedLib.Models;

[MessagePackObject]
public class LeaveLobbyRequest
{
    [Key(0)] public long UserId { get; set; }
}
