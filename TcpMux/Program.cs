using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace TcpMux
{
    public class Program
    {
        private static bool Verbose = false;
        private static bool Ssl = false;
        private static bool SslOffload = false;
        private static bool DumpHex = false;
        private static bool DumpText = false;
        private static string SslCn = null;
        private static readonly RemoteCertificateValidationCallback ServerCertificateValidationCallback =
            (object sender, X509Certificate cert, X509Chain chain, SslPolicyErrors sslPolicyErrors) =>
        {
            LogVerbose($"Validating certificate from {cert.Subject}");
            return true;

        };

        private static void LogVerbose(string message, bool addNewLine = true)
        {
            if (Verbose)
                Log(message, addNewLine);
        }

        private static void Log(string message, bool addNewLine = true)
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            if (addNewLine)
                Console.WriteLine($"{timestamp} {message}");
            else
                Console.Write($"{timestamp} {message}");
        }

        public static int Main(string[] args)
        {
            var remainingArgs = new List<string>();

            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                if (arg[0] != '-')
                {
                    remainingArgs.Add(arg);
                    continue;
                }

                switch (arg)
                {
                    case "-v":
                        Verbose = true;
                        CertificateFactory.Verbose = true;
                        break;
                    case "-ssl":
                        Ssl = true;
                        break;
                    case "-sslOff":
                        SslOffload = true;
                        break;
                    case "-sslCn":
                        if (i >= args.Length || (SslCn = args[++i])[0] == '-')
                        {
                            Console.WriteLine("Missing SSL CN");
                            return 1;
                        }
                        break;
                    case "-hex":
                        DumpHex = true;
                        break;
                    case "-text":
                        DumpText = true;
                        break;
                    case "-regCA":
                        RegisterCACert();
                        return 0;
                    default:
                        Console.WriteLine($"Invalid option: {arg}");
                        return 1;
                }
            }

            if (remainingArgs.Count != 3)
            {
                var version = Assembly.GetEntryAssembly()
                                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                                .InformationalVersion
                                .ToString();
                var versionLine = $"tcpmux - version {version}";
                Console.Error.WriteLine(
                    new string('-', versionLine.Length) + "\n" +
                    versionLine + "\n" +
                    new string('-', versionLine.Length) + "\n\n" +
                    "Usage: tcpmux [options] <listen_port> <target_host> <target_port>\n\n" +
                    "options:\n" +
                    "   -v: Verbose mode; display traffic\n" +
                    "   -hex: Hex mode; display traffic as hex dump\n" +
                    "   -text: Text mode; display traffic as text dump\n" +
                    "   -ssl: perform ssl decoding and reencoding\n" +
                    "   -sslOff: perform ssl off-loading (ie connect to the target via SSL, and expose a decrypted port)\n" +
                    "   -sslCn: CN to use in the generated SSL certificate (defaults to <target_host>)\n" +
                    "   -regCA: register self-signed certificate CA\n\n"
                );
                return 1;
            }

            var listenPort = int.Parse(remainingArgs[0]);
            var targetHost = remainingArgs[1];
            var targetPort = int.Parse(remainingArgs[2]);

            Log($"Preparing message routing to {targetHost}:{targetPort}");
            Log($"Opening local port {listenPort}...", addNewLine: false);
            var listener = new TcpListener(IPAddress.Any, listenPort);
            listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Linger, false);
            listener.Start();
            Console.WriteLine(" done");

            X509Certificate2 certificate = null;
            if (Ssl)
            {
                certificate = CertificateFactory.GetCertificateForSubject(SslCn ?? targetHost);
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

        private static void RegisterCACert()
        {
            Console.WriteLine("Press Enter to register the CA Certificate for TcpMux to your certificate store");
            Console.ReadLine();

            try
            {
                Console.WriteLine("Generating CA Certificate...");
                var cert =
                    CertificateFactory.GenerateCertificate(CertificateFactory.TcpMuxCASubjectDN, generateCA: true);
                Console.WriteLine("Registering certificate in current user store");

                // Add CA certificate to Root store
                CertificateFactory.AddCertToStore(cert, StoreName.Root, StoreLocation.CurrentUser);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: " + ex.Message);
            }
        }

        private static async void HandleClientConnection(TcpClient client, string targetHost, int targetPort,
            X509Certificate2 certificate)
        {
            try
            {
                client.NoDelay = true;
                Log($"New client connection: {client.Client.RemoteEndPoint}");
                Console.Write($"Opening connection to {targetHost}:{targetPort}...");

                var target = new TcpClient(targetHost, targetPort) { NoDelay = true };
                Log($" opened target connection: {target.Client.RemoteEndPoint}");

                Stream sourceStream = client.GetStream();
                Stream targetStream = target.GetStream();

                if (Ssl)
                {
                    var sslSourceStream = new SslStream(client.GetStream());
                    Log($"Performing SSL authentication with client {client.Client.RemoteEndPoint}");
                    await sslSourceStream.AuthenticateAsServerAsync(certificate, false,
                        SslProtocols.Tls | SslProtocols.Tls11 | SslProtocols.Tls12, false);
                    LogVerbose($"SSL authentication with client {client.Client.RemoteEndPoint} successful");
                    sourceStream = sslSourceStream;
                }

                if (Ssl || SslOffload)
                {
                    LogVerbose($"Performing SSL authentication with server {target.Client.RemoteEndPoint}");
                    var sslTargetStream = new SslStream(targetStream, false, ServerCertificateValidationCallback);
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
                        Log($"Connection {sourceRemoteEndPoint} closed; closing connection {targetRemoteEndPoint}");
                        target.Close();
                        return;
                    }

                    Log($"Sending data from {sourceRemoteEndPoint} to {targetRemoteEndPoint}...");
                    if (DumpHex)
                        Console.WriteLine(Utils.HexDump(buffer, 0, read));

                    if (DumpText)
                    {
                        var text = Encoding.UTF8.GetString(buffer, 0, read);
                        Console.WriteLine(text);
                    }
                    await target.WriteAsync(buffer, 0, read);
                    Log($"{read} bytes sent");
                }
            });
        }
    }
}
