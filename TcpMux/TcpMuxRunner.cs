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
        private readonly TcpMuxOptions _options;
        private readonly Lazy<LookupClient> _dnsLookupClient;
        private readonly MultiplexingConnection? _multiplexingConnection;

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

            var server = new TcpServer(_options);
            var certificate = GetOrCreateCertificate();

            Task.Run(async () =>
            {
                await foreach(var clientStream in server.GetClientConnections())
                {
                    HandleClientConnection(clientStream, certificate);
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

        private async void HandleClientConnection(EndPointStream clientStream, X509Certificate2? certificate)
        {
            try
            {
                Log($"New client connection: {clientStream}");
                var target = _options.Target;

                if (_options.Ssl)
                {
                    // Perform the SSL negotiation with the client, and read the target through SNI if
                    // SNI routing is enabled
                    (clientStream, target) = await NegotiateSslConnection(clientStream, certificate);
                }

                if (target == null)
                {
                    Console.WriteLine($"Failed to identify a target; closing connection");
                    clientStream.Stream.Close();
                    return;
                }

                var targetStream = await ConnectToTarget(target);

                if (_options.Ssl || _options.SslOffload)
                {
                    targetStream = await HandleSslTarget(targetStream, target);
                }

                RouteMessages(clientStream, targetStream);
                RouteMessages(targetStream, clientStream);
            }
            catch (Exception ex)
            {
                Log("Error: " + ex);
            }
        }

        private async Task<(EndPointStream stream, DnsEndPoint? target)> NegotiateSslConnection(EndPointStream clientStream,
            X509Certificate2? certificate)
        {
            var target = _options.Target;
            var sslSourceStream = new SslStream(clientStream.Stream);

            Log($"Performing SSL authentication with client {clientStream}");

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

            LogVerbose($"SSL authentication with client {clientStream} successful");
            return (new EndPointStream(sslSourceStream, clientStream.EndPoint), target);
        }

        private async Task<EndPointStream> HandleSslTarget(EndPointStream targetStream, DnsEndPoint target)
        {
            LogVerbose($"Performing SSL authentication with server {targetStream}");
            var sslTargetStream = new SslStream(targetStream.Stream, false, ValidateCertificate);
            await sslTargetStream.AuthenticateAsClientAsync(target.Host, null, SslProtocols.Tls12, false);
            LogVerbose($"SSL authentication with server {targetStream} successful; " +
                              $"server cert Subject: {sslTargetStream.RemoteCertificate?.Subject}");
            return new EndPointStream(sslTargetStream, targetStream.EndPoint);
        }

        private async Task<EndPointStream> ConnectToTarget(DnsEndPoint target)
        {
            var targetHost = target.Host;
            var targetPort = target.Port;

            if (_options.MultiplexingMode == MultiplexingMode.Multiplexer)
            {
                // We're in multiplexer mode; instead of connecting to the target, we simply
                // create a new multipexed stream for the target
                var stream = await CreateMultiplexedStream(target);
                return new EndPointStream(stream, target);
            }

            Console.Write($"Opening connection to {target.ToShortString()}...");
            if (_options.ForceDnsResolution)
            {
                targetHost = _dnsLookupClient.Value.GetHostEntry(targetHost).AddressList[0].ToString();
            }

            var tcpClient = new TcpClient(targetHost, targetPort) { NoDelay = true };
            Log($" opened target connection: {tcpClient.Client.RemoteEndPoint}");
            return new EndPointStream(tcpClient.GetStream(), tcpClient.Client.RemoteEndPoint);
        }

        private Task<Stream> CreateMultiplexedStream(DnsEndPoint target)
        {
            return _multiplexingConnection!.CreateMultiplexedStream(target);
        }

        private void RouteMessages(EndPointStream source, EndPointStream target)
        {
            var buffer = new byte[65536];
            Task.Run(async () =>
            {
                while (true)
                {
                    var read = await source.Stream.ReadAsync(buffer, 0, buffer.Length);
                    if (read == 0)
                    {
                        Log($"Connection {source} closed; closing connection {target}");
                        target.Stream.Close();
                        return;
                    }

                    Log($"Sending data from {source} to {target}...");
                    if (_options.DumpHex)
                        Console.WriteLine(Utils.HexDump(buffer, 0, read));

                    if (_options.DumpText)
                    {
                        var text = Encoding.UTF8.GetString(buffer, 0, read);
                        Console.WriteLine(text);
                    }
                    await target.Stream.WriteAsync(buffer, 0, read);
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

        private bool ValidateCertificate(object sender, X509Certificate cert, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            LogVerbose($"Validating certificate from {cert.Subject}");
            return true;
        }
    }
}
