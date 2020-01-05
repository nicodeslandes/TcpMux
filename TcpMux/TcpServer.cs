using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
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
                yield return new EndPointStream(client);
            }
        }

        private TcpListener StartTcpListener()
        {
            Log($"Opening local port {_options.ListenPort}...", addNewLine: false);
            var listener = new TcpListener(IPAddress.Any, _options.ListenPort);
            listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Linger, false);
            listener.Start();
            Console.WriteLine(" done");
            return listener;
        }

        // TODO: Add Serilog/NLog
        private static void Log(string message, bool addNewLine = true)
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            if (addNewLine)
                Console.WriteLine($"{timestamp} {message}");
            else
                Console.Write($"{timestamp} {message}");
        }

        private void LogVerbose(string message, bool addNewLine = true)
        {
            if (_options.Verbose)
                Log(message, addNewLine);
        }
    }
}
