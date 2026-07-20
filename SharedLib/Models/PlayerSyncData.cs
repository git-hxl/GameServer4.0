using MessagePack;

namespace SharedLib.Models;

[MessagePackObject]
public class PlayerSyncData
{
    [Key(0)] public long UserId { get; set; }
    [Key(1)] public float PosX { get; set; }
    [Key(2)] public float PosY { get; set; }
    [Key(3)] public float PosZ { get; set; }
    [Key(4)] public float RotY { get; set; }
    [Key(5)] public float Speed { get; set; }
    [Key(6)] public string AnimName { get; set; } = string.Empty;
    [Key(7)] public float AnimNormalTime { get; set; }
}
