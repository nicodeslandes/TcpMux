using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace TcpMux
{
    class Program
    {
        static int Main(string[] args)
        {
            if (args.Length != 3)
            {
                Console.Error.WriteLine($"Usage: tcpmux <listen_port> <target_host> <target_port>");
                return 1;
            }

            int listenPost = int.Parse(args[0]);
            string targetHost = args[1];
            int targetPort = int.Parse(args[2]);

            var listener = new TcpListener(IPAddress.Any, listenPost);
            listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Linger, false);
            listener.Start();
            Task.Run(async () =>
            {
                while (true)
                {
                    var client = await listener.AcceptTcpClientAsync();
                    client.NoDelay = true;
                    Console.WriteLine($"New client connection: {client.Client.RemoteEndPoint}");
                    Console.Write($"Opening connection to {targetHost}:{targetPort}...");

                    var target = new TcpClient(targetHost, targetPort) {NoDelay = true};
                    Console.WriteLine($" opened target connection: {target.Client.RemoteEndPoint}");

                    RouteMessages(client, target);
                    RouteMessages(target, client);
                }
            });

            Console.WriteLine("Press Ctrl+C to exit");
            while (true)
            {
                Console.Read();
            }
        }

        private static void RouteMessages(TcpClient source, TcpClient target)
        {
            var buffer = new byte[65536];
            var sourceStream = source.GetStream();
            var targetStream = target.GetStream();
            Task.Run(async () =>
            {
                while (true)
                {
                    var read = await sourceStream.ReadAsync(buffer, 0, buffer.Length);
                    if (read == 0)
                    {
                        Console.WriteLine($"Connection {source.Client.RemoteEndPoint} closed; " +
                                          $"closing connection {target.Client.RemoteEndPoint}");
                        target.Client.Shutdown(SocketShutdown.Send);
                        return;
                    }

                    Console.Write(
                        $"Sending data from {source.Client.RemoteEndPoint} to {target.Client.RemoteEndPoint}...");
                    await targetStream.WriteAsync(buffer, 0, read);
                    Console.WriteLine($" {read} bytes sent");
                }
            });
        }
    }
}
