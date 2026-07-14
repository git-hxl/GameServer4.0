using MessagePack;

namespace SharedLib.Models;

[MessagePackObject]
/// <summary>
/// 游戏服务器心跳请求
/// </summary>
public class GameServerHeartbeatRequest
{
    [Key(0)] public int Port { get; set; }
    [Key(1)] public int PlayerCount { get; set; }
    [Key(2)] public int RoomCount { get; set; }
    [Key(3)] public float CpuPercent { get; set; }
    [Key(4)] public long MemoryMB { get; set; }
}
