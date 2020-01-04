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
using TcpMux.Extensions;
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
        private readonly Lazy<LookupClient> _dnsLookupClient;
        readonly MultiplexingConnection? _multiplexingConnection;

        public TcpMuxRunner(TcpMuxOptions options)
        {
            _options = options;
            _dnsLookupClient = new Lazy<LookupClient>(() => new LookupClient());

            if (options.MultiplexingMode == MultiplexingMode.Multiplexer && options.MultiplexingTarget != null)
            {
                _multiplexingConnection = new MultiplexingConnection(options.MultiplexingTarget);
            }
        }

        public void Run()
        {
            if (_options.Target is null && !_options.SniRouting)
            {
                throw new InvalidOperationException("Target host cannot be null");
            }

            if (!_options.SniRouting)
            {
                Log($"Preparing message routing to {_options.Target?.ToShortString()}");
            }

            var listener = StartTcpListener();
            var certificate = GetOrCreateCertificate();

            Task.Run(async () =>
            {
                while (true)
                {
                    var client = await listener.AcceptTcpClientAsync();
                    HandleClientConnection(client, certificate);
                }
            });

            Console.WriteLine("Press Ctrl+C to exit");
            while (true)
            {
                Console.Read();
            }
        }

        private X509Certificate2? GetOrCreateCertificate()
        {
            X509Certificate2? certificate = null;
            if (_options.Ssl && !_options.SniRouting)
            {
                certificate = CertificateFactory.GetCertificateForSubject(_options.SslCn ?? _options.Target!.Host);
            }

            return certificate;
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

        private async void HandleClientConnection(TcpClient client, X509Certificate2? certificate)
        {
            try
            {
                client.NoDelay = true;
                Log($"New client connection: {client.Client.RemoteEndPoint}");

                Stream sourceStream = client.GetStream();
                var clientRemoteEndPoint = client.Client.RemoteEndPoint;

                var target = _options.Target;

                if (_options.Ssl)
                {
                    // Perform the SSL negotiation with the client, and read the target through SNI if
                    // SNI routing is enabled
                    (sourceStream, target) = await NegotiateSslConnection(sourceStream, client, certificate);
                }

                if (target == null)
                {
                    Console.WriteLine($"Failed to identify a target; closing connection");
                    client.Close();
                    return;
                }

                var (targetStream, targetRemoteEndPoint) = await ConnectToTargetAsync(target);

                if (_options.Ssl || _options.SslOffload)
                {
                    targetStream = await HandleSslTarget(targetStream, target, targetRemoteEndPoint);
                }

                RouteMessages(sourceStream, clientRemoteEndPoint, targetStream, targetRemoteEndPoint);
                RouteMessages(targetStream, targetRemoteEndPoint, sourceStream, clientRemoteEndPoint);
            }
            catch (Exception ex)
            {
                Log("Error: " + ex);
            }
        }

        private async Task<(SslStream stream, DnsEndPoint? target)> NegotiateSslConnection(Stream sourceStream,
            TcpClient client, X509Certificate2? certificate)
        {
            var target = _options.Target;
            var sslSourceStream = new SslStream(sourceStream);

            Log($"Performing SSL authentication with client {client.Client.RemoteEndPoint}");

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

            if (sniHost != null)
            {
                LogVerbose($"SNI Host: {sniHost}");
                target = new DnsEndPoint(sniHost, target?.Port ?? _options.ListenPort);
            }

            LogVerbose($"SSL authentication with client {client.Client.RemoteEndPoint} successful");
            return (sslSourceStream, target);
        }

        private async Task<SslStream> HandleSslTarget(Stream targetStream, DnsEndPoint target,
            EndPoint targetRemoteEndPoint)
        {
            LogVerbose($"Performing SSL authentication with server {targetRemoteEndPoint}");
            var sslTargetStream = new SslStream(targetStream, false, ValidateCertificate);
            await sslTargetStream.AuthenticateAsClientAsync(target.Host, null, SslProtocols.Tls12, false);
            LogVerbose($"SSL authentication with server {targetRemoteEndPoint} successful; " +
                              $"server cert Subject: {sslTargetStream.RemoteCertificate?.Subject}");
            return sslTargetStream;
        }

        private async Task<(Stream stream, EndPoint remoteEndPoint)> ConnectToTargetAsync(DnsEndPoint target)
        {
            var targetHost = target.Host;
            var targetPort = target.Port;

            if (_options.MultiplexingMode == MultiplexingMode.Multiplexer)
            {
                // We're in multiplexer mode; instead of connecting to the target, we simply
                // create a new multipexed stream for the target
                var stream = await CreateMultiplexedStream(target);
                return (stream, target);
            }

            Console.Write($"Opening connection to {target.ToShortString()}...");
            if (_options.ForceDnsResolution)
            {
                targetHost = _dnsLookupClient.Value.GetHostEntry(targetHost).AddressList[0].ToString();
            }

            var tcpClient = new TcpClient(targetHost, targetPort) { NoDelay = true };
            Log($" opened target connection: {tcpClient.Client.RemoteEndPoint}");
            return (tcpClient.GetStream(), tcpClient.Client.RemoteEndPoint);
        }

        private Task<Stream> CreateMultiplexedStream(DnsEndPoint target)
        {
            return _multiplexingConnection!.CreateMultiplexedStream(target);
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
