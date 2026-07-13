using Serilog;

Log.Logger = new LoggerConfiguration().WriteTo.Console().WriteTo.File("log.txt",
    rollingInterval: RollingInterval.Day,
    rollOnFileSizeLimit: true).CreateLogger();

Log.Information("Starting up");

var server = new LobbyServer.LobbyServer();
server.Start(9050);

using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(30));

while (await timer.WaitForNextTickAsync())
{
    server.PollEvents();
}