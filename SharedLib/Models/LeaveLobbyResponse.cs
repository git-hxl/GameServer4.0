using MessagePack;

namespace SharedLib.Models;

[MessagePackObject]
public class LeaveLobbyResponse
{
    [Key(0)] public long UserId { get; set; }
}
