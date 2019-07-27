using System;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace TcpMux
{
    public class TcpMuxServer
    {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();

        private static readonly RemoteCertificateValidationCallback ServerCertificateValidationCallback =
            (object sender, X509Certificate cert, X509Chain chain, SslPolicyErrors sslPolicyErrors) =>
            {
                Log.Debug($"Validating certificate from {cert.Subject}");
                return true;

            };

        private readonly TcpMuxOptions options;
        private bool _stopped;

        public TcpMuxServer(TcpMuxOptions options)
        {
            this.options = options;
        }

        public void Start()
        {
            Log.Info($"Preparing message routing to {options.TargetHost}:{options.TargetPort}");
            Log.Info($"Opening local port {options.ListenPort}...");
            var listener = new TcpListener(IPAddress.Any, options.ListenPort);
            listener.Server.LingerState = new LingerOption(enable: false, seconds: 0);
            listener.Start();
            Log.Info("Port open");

            X509Certificate2 certificate = null;
            if (options.Ssl)
            {
                certificate = CertificateFactory.GetCertificateForSubject(options.SslCn ?? options.TargetHost);
            }

            Task.Run(async () =>
            {
                while (!Volatile.Read(ref _stopped))
                {
                    var client = await listener.AcceptTcpClientAsync();
                    HandleClientConnection(client, options, certificate);
                }
            });
        }

        public void Stop()
        {
            Volatile.Write(ref _stopped, true);
        }
        private static async void HandleClientConnection(TcpClient client, TcpMuxOptions options,
            X509Certificate2 certificate)
        {
            try
            {
                client.NoDelay = true;
                Log.Info($"New client connection: {client.Client.RemoteEndPoint}");
                Log.Info($"Opening connection to {options.TargetHost}:{options.TargetPort}...");

                var target = new TcpClient(options.TargetHost, options.TargetPort) { NoDelay = true };
                Log.Info($" opened target connection: {target.Client.RemoteEndPoint}");

                Stream sourceStream = client.GetStream();
                Stream targetStream = target.GetStream();

                if (options.Ssl)
                {
                    var sslSourceStream = new SslStream(client.GetStream());
                    Log.Info($"Performing SSL authentication with client {client.Client.RemoteEndPoint}");
                    await sslSourceStream.AuthenticateAsServerAsync(certificate, false,
                        SslProtocols.Tls | SslProtocols.Tls11 | SslProtocols.Tls12, false);
                    Log.Debug($"SSL authentication with client {client.Client.RemoteEndPoint} successful");
                    sourceStream = sslSourceStream;
                }

                if (options.Ssl || options.SslOffload)
                {
                    Log.Debug($"Performing SSL authentication with server {target.Client.RemoteEndPoint}");
                    var sslTargetStream = new SslStream(targetStream, false, ServerCertificateValidationCallback);
                    await sslTargetStream.AuthenticateAsClientAsync(options.TargetHost, null, SslProtocols.Tls12, false);
                    Log.Debug($"SSL authentication with server {target.Client.RemoteEndPoint} successful; " +
                                      $"server cert Subject: {sslTargetStream.RemoteCertificate?.Subject}");
                    targetStream = sslTargetStream;
                }

                RouteMessages(sourceStream, client.Client.RemoteEndPoint,
                    targetStream, target.Client.RemoteEndPoint, options);
                RouteMessages(targetStream, target.Client.RemoteEndPoint,
                    sourceStream, client.Client.RemoteEndPoint, options);
            }
            catch (Exception ex)
            {
                Log.Info("Error: " + ex);
            }
        }

        private static void RouteMessages(Stream source, EndPoint sourceRemoteEndPoint, Stream target,
            EndPoint targetRemoteEndPoint, TcpMuxOptions options)
        {
            var buffer = new byte[65536];
            Task.Run(async () =>
            {
                while (true)
                {
                    var read = await source.ReadAsync(buffer, 0, buffer.Length);
                    if (read == 0)
                    {
                        Log.Info($"Connection {sourceRemoteEndPoint} closed; closing connection {targetRemoteEndPoint}");
                        target.Close();
                        return;
                    }

                    Log.Info($"Sending data from {sourceRemoteEndPoint} to {targetRemoteEndPoint}...");
                    if (options.DumpHex)
                        Console.WriteLine(Utils.HexDump(buffer, 0, read));

                    if (options.DumpText)
                    {
                        var text = Encoding.UTF8.GetString(buffer, 0, read);
                        Console.WriteLine(text);
                    }
                    await target.WriteAsync(buffer, 0, read);
                    Log.Info($"{read} bytes sent");
                }
            });
        }
    }
}
