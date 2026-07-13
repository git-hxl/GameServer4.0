namespace SharedLib.Config;

public class ServerSettings
{
    public ServerConfig Server { get; set; } = new();
    public NetworkConfig Network { get; set; } = new();
}
