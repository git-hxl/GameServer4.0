namespace SharedLib.Config;

public class NetworkConfig
{
    public string ConnectionKey { get; set; } = "Game@wasd9527";
    public int UpdateTime { get; set; } = 15;
    public int PingInterval { get; set; } = 1000;
    public int DisconnectTimeout { get; set; } = 5000;
    public byte ChannelsCount { get; set; } = 4;
}

