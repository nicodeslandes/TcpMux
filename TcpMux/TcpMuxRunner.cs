using System;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using DnsClient;
using TcpMux.Options;

namespace TcpMux
{
    public class TcpMuxRunner
    {

        private bool ValidateCertificate(object sender, X509Certificate cert, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            LogVerbose($"Validating certificate from {cert.Subject}");
            return true;
        }

        private readonly TcpMuxOptions _options;
        private Lazy<LookupClient> _dnsLookupClient;

        public TcpMuxRunner(TcpMuxOptions options)
        {
            _options = options;
            _dnsLookupClient = new Lazy<LookupClient>(() => new LookupClient());
        }

        public void Run()
        {
            if (_options.Target is null && !_options.SniRouting)
            {
                throw new InvalidOperationException("Target host cannot be null");
            }

            // TODO: Make this more robust
            var targetHost = _options.SniRouting ? "<sni>" : _options.Target!.Split(':')[0];
            var targetPort = _options.SniRouting ? 443 : _options.Target!.Contains(":")
                ? ushort.Parse(_options.Target.Split(':')[1])
                : _options.ListenPort;

            if (!_options.SniRouting)
            {
                Log($"Preparing message routing to {_options.Target}");
            }

            Log($"Opening local port {_options.ListenPort}...", addNewLine: false);
            var listener = new TcpListener(IPAddress.Any, _options.ListenPort);
            listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Linger, false);
            listener.Start();
            Console.WriteLine(" done");

            X509Certificate2? certificate = null;
            if (_options.Ssl && !_options.SniRouting)
            {
                certificate = CertificateFactory.GetCertificateForSubject(_options.SslCn ?? targetHost);
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


        private async void HandleClientConnection(TcpClient client, string targetHost, int targetPort,
            X509Certificate2? certificate)
        {
            try
            {
                client.NoDelay = true;
                Log($"New client connection: {client.Client.RemoteEndPoint}");
                Console.Write($"Opening connection to {targetHost}:{targetPort}...");

                Stream sourceStream = client.GetStream();

                if (_options.Ssl)
                {
                    var sslSourceStream = new SslStream(sourceStream);

                    Log($"Performing SSL authentication with client {client.Client.RemoteEndPoint}");
#if !NET452
                    string? sniHost = null;
                    var sslOptions = new SslServerAuthenticationOptions
                    {
                        ServerCertificateSelectionCallback = (_, hostName) =>
                        {
                            sniHost = hostName;
                            return CertificateFactory.GetCertificateForSubject(sniHost);
                        }
                    };

                    await sslSourceStream.AuthenticateAsServerAsync(sslOptions);
                    Console.WriteLine("SNI Host: {0}", sniHost);
                    if (sniHost != null)
                    {
                        targetHost = sniHost;
                    }
#else
                    await sslSourceStream.AuthenticateAsServerAsync(certificate, false,
                        SslProtocols.Tls | SslProtocols.Tls11 | SslProtocols.Tls12, false);
#endif

                    LogVerbose($"SSL authentication with client {client.Client.RemoteEndPoint} successful");
                    sourceStream = sslSourceStream;
                }

                var target = ConnectToTarget(targetHost, targetPort);
                Log($" opened target connection: {target.Client.RemoteEndPoint}");
                Stream targetStream = target.GetStream();


                if (_options.Ssl || _options.SslOffload)
                {
                    LogVerbose($"Performing SSL authentication with server {target.Client.RemoteEndPoint}");
                    var sslTargetStream = new SslStream(targetStream, false, ValidateCertificate);
                    await sslTargetStream.AuthenticateAsClientAsync(targetHost, null, SslProtocols.Tls12, false);
                    LogVerbose($"SSL authentication with server {target.Client.RemoteEndPoint} successful; " +
                                      $"server cert Subject: {sslTargetStream.RemoteCertificate?.Subject}");
                    targetStream = sslTargetStream;
                }

                RouteMessages(sourceStream, client.Client.RemoteEndPoint,
                    targetStream, target.Client.RemoteEndPoint);
                RouteMessages(targetStream, target.Client.RemoteEndPoint,
                    sourceStream, client.Client.RemoteEndPoint);
            }
            catch (Exception ex)
            {
                Log("Error: " + ex);
            }
        }

        private TcpClient ConnectToTarget(string targetHost, int targetPort)
        {
            if (_options.ForceDnsResolution)
            {
                targetHost = _dnsLookupClient.Value.GetHostEntry(targetHost).AddressList[0].ToString();
            }
            return new TcpClient(targetHost, targetPort) { NoDelay = true };
        }

        private void RouteMessages(Stream source, EndPoint sourceRemoteEndPoint, Stream target,
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
                        Log($"Connection {sourceRemoteEndPoint} closed; closing connection {targetRemoteEndPoint}");
                        target.Close();
                        return;
                    }

                    Log($"Sending data from {sourceRemoteEndPoint} to {targetRemoteEndPoint}...");
                    if (_options.DumpHex)
                        Console.WriteLine(Utils.HexDump(buffer, 0, read));

                    if (_options.DumpText)
                    {
                        var text = Encoding.UTF8.GetString(buffer, 0, read);
                        Console.WriteLine(text);
                    }
                    await target.WriteAsync(buffer, 0, read);
                    Log($"{read} bytes sent");
                }
            });
        }

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
