using LiteNetLib;
using Serilog;

namespace LobbyServer;

public class LobbyServer
{
    private readonly NetManager _netManager;
    private readonly EventBasedNetListener _listener;

    public LobbyServer()
    {
        _listener = new EventBasedNetListener();
        _netManager = new NetManager(_listener);
    }

    public void Start(int port)
    {
        _listener.ConnectionRequestEvent += request =>
        {
            request.AcceptIfKey("GameServer4.0");
        };

        _listener.PeerConnectedEvent += peer =>
        {
            Log.Information("Client connected: {EndPoint}", peer.Address);
        };

        _listener.PeerDisconnectedEvent += (peer, disconnectInfo) =>
        {
            Log.Information("Client disconnected: {EndPoint}, reason: {Reason}",
                peer.Address, disconnectInfo.Reason);
        };

        _listener.NetworkReceiveEvent += (peer, reader, channel, deliveryMethod) =>
        {
            Log.Information("Received {ByteCount} bytes from {EndPoint}", reader.AvailableBytes, peer.Address);
            reader.Recycle();
        };

        _netManager.Start(port);
        Log.Information("Lobby server started on port {Port}", port);
    }

    public void Stop()
    {
        _netManager.Stop();
        Log.Information("Lobby server stopped");
    }

    public void PollEvents()
    {
        _netManager.PollEvents();
    }
}
