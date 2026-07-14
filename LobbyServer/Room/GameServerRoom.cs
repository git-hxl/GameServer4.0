using LiteNetLib;
using SharedLib.Models;

namespace LobbyServer.Room;

public class GameServerRoom
{
    public string RoomId { get; set; } = string.Empty;
    public RoomType RoomType { get; set; } = RoomType.Default;
    public NetPeer GameServerPeer { get; set; } = null!;
    public Dictionary<NetPeer, PlayerInfo> Players { get; set; } = [];
}
