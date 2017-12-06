using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace TcpMux
{
    class Program
    {
        static bool Verbose = false;
        static bool Ssl = false;

        static readonly RemoteCertificateValidationCallback ServerCertificateValidationCallback =
            (object sender, X509Certificate cert, X509Chain chain, SslPolicyErrors sslPolicyErrors) =>
        {
            Console.WriteLine($"Validating certificate from {cert.Subject}");
            return true;

        };
        static int Main(string[] args)
        {
            if (args.Length < 3)
            {
                Console.Error.WriteLine($"Usage: tcpmux [options] <listen_port> <target_host> <target_port>\n" +
                                        $"  options:\n" +
                                        $"     -v: Verbose mode; display traffic\n" +
                                        $"     -ssl: perform ssl decoding and reencoding\n\n"
                    );
                return 1;
            }

            int i = 0;
            while (true)
            {
                if (args[i][0] == '-')
                {
                    var option = args[i++];
                    switch (option)
                    {

                        case "-v":
                            Verbose = true;
                            break;
                        case "-ssl":
                            Ssl = true;
                            break;
                        default:
                            Console.WriteLine($"Invalid option: {option}");
                            return 1;
                    }
                }
                else
                {
                    break;
                }
            }

            int listenPort = int.Parse(args[0]);
            string targetHost = args[1];
            int targetPort = int.Parse(args[2]);

            Console.WriteLine($"Preparing message routing to {targetHost}:{targetPort}");
            Console.Write($"Opening local port {listenPort}...");
            var listener = new TcpListener(IPAddress.Any, listenPort);
            listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Linger, false);
            listener.Start();
            Console.WriteLine(" done");

            X509Certificate2 certificate = null;
            if (Ssl)
            {
                var store = new X509Store(StoreLocation.CurrentUser);
                store.Open(OpenFlags.OpenExistingOnly);
                certificate = store.Certificates.Cast<X509Certificate2>().First(c => c.Subject == "CN=surface4");
            }

            Task.Run(async () =>
            {
                while (true)
                {
                    var client = await listener.AcceptTcpClientAsync();
                    HandleClientConnection(client, targetHost, targetPort, certificate);
                }
            });

            Console.WriteLine("Press Ctrl+C to exit");
            while (true)
            {
                Console.Read();
            }
        }

        private static async void HandleClientConnection(TcpClient client, string targetHost, int targetPort,
            X509Certificate2 certificate)
        {
            try
            {
                client.NoDelay = true;
                Console.WriteLine($"New client connection: {client.Client.RemoteEndPoint}");
                Console.Write($"Opening connection to {targetHost}:{targetPort}...");

                var target = new TcpClient(targetHost, targetPort) {NoDelay = true};
                Console.WriteLine($" opened target connection: {target.Client.RemoteEndPoint}");

                Stream sourceStream = client.GetStream();
                Stream targetStream = target.GetStream();

                if (Ssl)
                {
                    var sslSourceStream = new SslStream(client.GetStream());
                    Console.WriteLine($"Performing SSL authentication with client {client.Client.RemoteEndPoint}");
                    await sslSourceStream.AuthenticateAsServerAsync(certificate, false,
                        SslProtocols.Tls | SslProtocols.Tls11 | SslProtocols.Tls12, false);
                    Console.WriteLine($"SSL authentication with client {client.Client.RemoteEndPoint} successful");
                    sourceStream = sslSourceStream;

                    Console.WriteLine($"Performing SSL authentication with server {target.Client.RemoteEndPoint}");
                    var sslTargetStream = new SslStream(targetStream, false, ServerCertificateValidationCallback);
                    await sslTargetStream.AuthenticateAsClientAsync(targetHost, null, SslProtocols.Tls12, false);
                    Console.WriteLine($"SSL authentication with server {target.Client.RemoteEndPoint} successful; " +
                                      $"server cert Subject: {sslTargetStream.RemoteCertificate?.Subject}");
                }

                RouteMessages(sourceStream, client.Client.RemoteEndPoint,
                    targetStream, target.Client.RemoteEndPoint);
                RouteMessages(targetStream, target.Client.RemoteEndPoint,
                    sourceStream, client.Client.RemoteEndPoint);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: " + ex);
            }
        }

        private static void RouteMessages(Stream source, EndPoint sourceRemoteEndPoint, Stream target,
            EndPoint targetRemoteEndPoint)
        {
            var buffer = new byte[65536];
            Task.Run(async () =>
            {
                while (true)
                {
                    var read = await source.ReadAsync(buffer, 0, buffer.Length);
                    if (read == 0)
                    {
                        Console.WriteLine($"Connection {sourceRemoteEndPoint} closed; " +
                                          $"closing connection {targetRemoteEndPoint}");
                        target.Close();
                        return;
                    }

                    Console.Write(
                        $"Sending data from {sourceRemoteEndPoint} to {targetRemoteEndPoint}...");
                    await target.WriteAsync(buffer, 0, read);
                    Console.WriteLine($" {read} bytes sent");
                    if (Verbose)
                        Console.WriteLine(Utils.HexDump(buffer, 0, read));
                }
            });
        }
    }
}
