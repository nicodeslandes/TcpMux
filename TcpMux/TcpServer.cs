using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using Serilog;
using TcpMux.Options;

namespace TcpMux
{
    public class TcpServer : IConnectionSource
    {
        private readonly TcpListener _server;

        public TcpServer(int listeningPort)
        {
            _server = StartTcpListener(listeningPort);
        }

        public async IAsyncEnumerable<EndPointStream> GetClientConnections()
        {
            while (true)
            {
                Log.Debug("Waiting for connection on port {endpoint}", _server.LocalEndpoint);
                var client = await _server.AcceptTcpClientAsync();
                client.NoDelay = true;
                Log.Information("New client connection: {client}", client.Client.RemoteEndPoint);
                yield return new EndPointStream(client);
            }
        }

        private TcpListener StartTcpListener(int port)
        {
            Log.Information("Opening local port {port}", port);
            var listener = new TcpListener(IPAddress.Any, port);
            listener.Server.LingerState = new LingerOption(enable: false, seconds: 0);
            listener.Start();
            Log.Information("Port {port} successfully opened", port);
            return listener;
        }
    }
}
