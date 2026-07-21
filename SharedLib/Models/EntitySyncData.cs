using MessagePack;

namespace SharedLib.Models
{

    [MessagePackObject]
    public class EntitySyncData
    {
        [Key(0)] public long EntityId { get; set; }
        [Key(1)] public byte EntityType { get; set; }
        [Key(2)] public float PosX { get; set; }
        [Key(3)] public float PosY { get; set; }
        [Key(4)] public float PosZ { get; set; }
        [Key(5)] public float RotX { get; set; }
        [Key(6)] public float RotY { get; set; }
        [Key(7)] public float RotZ { get; set; }
        [Key(8)] public float Speed { get; set; }
        [Key(9)] public string AnimName { get; set; } = string.Empty;
        [Key(10)] public float AnimNormalTime { get; set; }
    }
}
