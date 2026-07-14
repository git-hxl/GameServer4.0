using LiteNetLib;
using SharedLib.Models;

namespace LobbyServer.Room;

/// <summary>
/// 游戏服务器房间实体，包含房间 ID、类型、关联的游戏服务器和玩家列表
/// </summary>
public class GameServerRoom
{
    public string RoomId { get; set; } = string.Empty;
    public RoomType RoomType { get; set; } = RoomType.Default;
    public NetPeer GameServerPeer { get; set; } = null!;
    public NetPeer? OwnerPeer { get; set; }
    public Dictionary<NetPeer, PlayerInfo> Players { get; set; } = [];
}
