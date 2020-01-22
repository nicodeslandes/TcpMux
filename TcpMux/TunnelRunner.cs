using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Serilog;
using TcpMux.Extensions;
using TcpMux.Options;

namespace TcpMux
{
    public class TunnelRunner
    {
        private readonly TcpMuxOptions _options;
        private readonly Channel<EndPointStream> _tunnelConnections = Channel.CreateUnbounded<EndPointStream>();
        private readonly TrafficRouter _router;
        private static readonly ReadOnlyMemory<byte> NewClientConnectionMessage = new byte[] { 1 }; 

        public TunnelRunner(TcpMuxOptions options)
        {
            _options = options;
            _router = new TrafficRouter(_options);
        }

        public void Run()
        {
            if (_options.RunningMode == RunningMode.TunnelIn)
            {
                Task.Run(StartTunnelConnectionListener);
                Task.Run(RunAsReceiver);
            }
            else if(_options.RunningMode == RunningMode.TunnelOut)
            {
                Task.Run(StartTunnelConnector);
            }
            else
            {
                throw new InvalidOperationException($"Unhandled Running mode: {_options.RunningMode}");
            }

            Console.WriteLine("Press Ctrl-C to exit");
            Thread.CurrentThread.Join();
        }

        private async Task StartTunnelConnector()
        {
            if (_options.Target == null)
            {
                throw new InvalidOperationException("Null Target not allowed in Tunnel-Out mode");
            }

            while (true)
            {
                // Establish connection to tunnel receiver
                var tunnelEndPoint = _options.TunnelTarget!;
                Log.Debug("Opening connection to tunnel receiver {endpoint}", tunnelEndPoint.ToShortString());
                var tunnelClient = new TcpClient(tunnelEndPoint.Host, tunnelEndPoint.Port) { NoDelay = true };
                Log.Information("Opened tunnel connection: {endPoint}", tunnelClient.Client.RemoteEndPoint);
                var tunnelEndPointStream = new EndPointStream(
                    tunnelClient.GetStream(), tunnelClient.Client.RemoteEndPoint);

                // Wait for the 1st byte; which means a remote connection has been established on the other side of
                // the tunnel
                var buffer = new byte[1];
                var read = await tunnelEndPointStream.Stream.ReadAsync(buffer, 0, 1);

                // Check for connection closure
                if (read == 0)
                {
                    Log.Information("Tunnel connection closed");
                    tunnelEndPointStream.Stream.Close();
                    break;
                }

                // Otherwise, ignore the 1st byte we just read and start routing message to the target
                var targetStream = _router.ConnectToTarget(_options.Target);
                _router.RouteMessages(tunnelEndPointStream, targetStream);
                _router.RouteMessages(targetStream, tunnelEndPointStream);
            }
        }

        private async Task StartTunnelConnectionListener()
        {
            var server = new TcpServer(_options.TunnelListenPort);
            await foreach (var connection in server.GetClientConnections())
            {
                Log.Information("New tunnel connection from {client}", connection);
                await _tunnelConnections.Writer.WriteAsync(connection);
            }
        }

        private async Task RunAsReceiver()
        {
            // Receiving mode
            var server = new TcpServer(_options.ListenPort);
            await foreach(var connection in server.GetClientConnections())
            {
                Log.Information("New connection from {client}", connection);

                // Get the next tunnel connection and bridge traffic between the 2
                var tunnelConnection = await _tunnelConnections.Reader.ReadAsync();

                // Send a single byte to notify the remote tunnel that a new client connection has been received
                await tunnelConnection.Stream.WriteAsync(NewClientConnectionMessage);

                _router.RouteMessages(connection, tunnelConnection);
                _router.RouteMessages(tunnelConnection, connection);
            }
        }
    }
}