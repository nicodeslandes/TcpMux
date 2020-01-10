using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using Serilog;
using TcpMux.Options;

namespace TcpMux
{
    public class TcpServer : IConnectionSource
    {
        private readonly TcpMuxOptions _options;
        private readonly TcpListener _server;

        public TcpServer(TcpMuxOptions options)
        {
            _options = options;
            _server = StartTcpListener();
        }

        public async IAsyncEnumerable<EndPointStream> GetClientConnections()
        {
            while (true)
            {
                var client = await _server.AcceptTcpClientAsync();
                client.NoDelay = true;
                Log.Information("New client connection: {client}", client.Client.RemoteEndPoint);
                yield return new EndPointStream(client);
            }
        }

        private TcpListener StartTcpListener()
        {
            var listenPort = _options.ListenPort;
            Log.Information("Opening local port {port}", listenPort);
            var listener = new TcpListener(IPAddress.Any, listenPort);
            listener.Server.LingerState = new LingerOption(enable: false, seconds: 0);
            listener.Start();
            Log.Information("Port {port} succesfully opened", listenPort);
            return listener;
        }
    }
}
