using LiteNetLib;
using SharedLib.Models;

namespace LobbyServer.Room;

/// <summary>
/// 游戏服务器房间实体，包含房间 ID、类型、关联的游戏服务器和玩家列表
/// </summary>
public class LobbyRoom
{
    public RoomInfo Info { get; set; } = new();
    public NetPeer GameServerPeer { get; set; } = null!;
    public NetPeer? OwnerPeer { get; set; }
    public Dictionary<NetPeer, PlayerInfo> PlayerPeers { get; set; } = [];
    public HashSet<NetPeer> ReadyPeers { get; set; } = [];
}
