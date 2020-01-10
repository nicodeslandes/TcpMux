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
using Serilog;
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
            if (_options.Target is null && !_options.SniRouting && _options.MultiplexingMode == MultiplexingMode.None)
            {
                throw new InvalidOperationException("Target host cannot be null");
            }

            if (!_options.SniRouting)
            {
                Log.Information("Preparing message routing to {target}", _options.Target?.ToShortString());
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

                var targetStream = await OpenTargetStream(target);
                RouteMessages(clientStream, targetStream);
                RouteMessages(targetStream, clientStream);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error: {message}", ex.Message);
            }
        }

        private async Task<EndPointStream> OpenTargetStream(DnsEndPoint target)
        {
            var targetStream = await ConnectToTarget(target);

            if (_options.Ssl || _options.SslOffload)
            {
                targetStream = await HandleSslTarget(targetStream, target);
            }

            return targetStream;
        }

        private async Task<(EndPointStream stream, DnsEndPoint? target)> NegotiateSslConnection(
            EndPointStream clientStream, X509Certificate2? certificate)
        {
            var target = _options.Target;
            var sslSourceStream = new SslStream(clientStream.Stream);

            Log.Information("Performing SSL authentication with client {client}", clientStream);

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
                Log.Verbose("SNI Host: {sniHost}", sniHost);
                target = new DnsEndPoint(sniHost, target?.Port ?? _options.ListenPort);
            }

            Log.Verbose("SSL authentication with client {client} successful", clientStream);
            return (new EndPointStream(sslSourceStream, clientStream.EndPoint), target);
        }

        private async Task<EndPointStream> HandleSslTarget(EndPointStream targetStream, DnsEndPoint target)
        {
            Log.Verbose("Performing SSL authentication with server {server}", targetStream);
            var sslTargetStream = new SslStream(targetStream.Stream, false, ValidateCertificate);
            await sslTargetStream.AuthenticateAsClientAsync(target.Host, null, SslProtocols.Tls12, false);
            Log.Verbose("SSL authentication with server {server} successful; server cert Subject: {subject}",
                targetStream, sslTargetStream.RemoteCertificate?.Subject);
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
            Log.Information("Opened target connection: {endPoint}", tcpClient.Client.RemoteEndPoint);
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
                        Log.Information("Connection {source} closed; closing connection {target}", source, target);
                        target.Stream.Close();
                        return;
                    }

                    Log.Information("Sending data from {source} to {target}...", source, target);
                    if (_options.DumpHex)
                        Console.WriteLine(Utils.HexDump(buffer, 0, read));

                    if (_options.DumpText)
                    {
                        var text = Encoding.UTF8.GetString(buffer, 0, read);
                        Console.WriteLine(text);
                    }
                    await target.Stream.WriteAsync(buffer, 0, read);
                    Log.Information("{read} bytes sent", read);
                }
            });
        }

        private bool ValidateCertificate(object sender, X509Certificate cert, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            Log.Verbose("Validating certificate from {subject}", cert.Subject);
            return true;
        }
    }
}
