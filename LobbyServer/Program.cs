using Serilog;
using SharedLib.Config;

Log.Logger = new LoggerConfiguration().WriteTo.Console().WriteTo.File("log.txt",
    rollingInterval: RollingInterval.Day,
    rollOnFileSizeLimit: true).CreateLogger();

Log.Information("Starting up");

var settings = ConfigLoader.Load<ServerSettings>();
var server = new LobbyServer.LobbyServer(settings.Network);
server.Start(settings.Server.Port);

using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(settings.Network.UpdateTime));

while (await timer.WaitForNextTickAsync())
{
    server.PollEvents();
}
