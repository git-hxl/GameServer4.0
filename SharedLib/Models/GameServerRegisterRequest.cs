using MessagePack;

namespace SharedLib.Models;

/// <summary>
/// 游戏服务器注册请求
/// </summary>
[MessagePackObject]
public class GameServerRegisterRequest
{
    [Key(0)] public int Port { get; set; }
    [Key(1)] public int PlayerCount { get; set; }
    [Key(2)] public int RoomCount { get; set; }
}
