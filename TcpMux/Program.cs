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
using NLog;

namespace TcpMux
{
    public class Program
    {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();

        private static readonly RemoteCertificateValidationCallback ServerCertificateValidationCallback =
            (object sender, X509Certificate cert, X509Chain chain, SslPolicyErrors sslPolicyErrors) =>
        {
            Log.Debug($"Validating certificate from {cert.Subject}");
            return true;

        };
        
        public static int Main(string[] args)
        {
            try
            {
                var options = ParseCommandLineParameters(args);

                if (options.RegisterCACert)
                {
                    RegisterCACert();
                    return 0;
                }


                Log.Info($"Preparing message routing to {options.TargetHost}:{options.TargetPort}");
                Log.Info($"Opening local port {options.ListenPort}...");
                var listener = new TcpListener(IPAddress.Any, options.ListenPort);
                listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Linger, false);
                listener.Start();
                Log.Info("Port open");

                X509Certificate2 certificate = null;
                if (options.Ssl)
                {
                    certificate = CertificateFactory.GetCertificateForSubject(options.SslCn ?? options.TargetHost);
                }

                Task.Run(async () =>
                {
                    while (true)
                    {
                        var client = await listener.AcceptTcpClientAsync();
                        HandleClientConnection(client, options, certificate);
                    }
                });
            }
            catch (MissingParametersException) { return 1; }
            catch(InvalidOptionException ex)
            {
                Console.WriteLine("Invalid option: " + ex.Message);
                return 1;
            }

            Console.WriteLine("Press Ctrl+C to exit");
            while (true)
            {
                Console.Read();
            }
        }

        private static TcpMuxOptions ParseCommandLineParameters(string[] args)
        {
            var options = new TcpMuxOptions();
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
                        LogManager.GlobalThreshold = LogLevel.Debug;
                        break;
                    case "-ssl":
                        options.Ssl = true;
                        break;
                    case "-sslOff":
                        options.SslOffload = true;
                        break;
                    case "-sslCn":
                        if (i >= args.Length || (options.SslCn = args[++i])[0] == '-')
                        {
                            throw new InvalidOptionException("Missing SSL CN");
                        }
                        break;
                    case "-hex":
                        options.DumpHex = true;
                        break;
                    case "-text":
                        options.DumpText = true;
                        break;
                    case "-regCA":
                        options.RegisterCACert = true;
                        break;
                    default:
                        throw new InvalidOptionException($"Invalid option: {arg}");
                }
            }

            if (remainingArgs.Count != 3)
            {
                var version = Assembly.GetExecutingAssembly()
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

                if (remainingArgs.Count < 3) throw new MissingParametersException();
                else new InvalidOptionException("Invalid parameters: " + string.Join(" ", remainingArgs));
            }

            ushort ParsePort(string argument)
            {
                if (ushort.TryParse(remainingArgs[0], out var port)) return port;
                throw new InvalidOptionException($"Invalid port: {argument}");
            }

            options.ListenPort = ParsePort(remainingArgs[0]);
            options.TargetHost = remainingArgs[1];
            options.TargetPort = ParsePort(remainingArgs[2]);
            return options;
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

        private static async void HandleClientConnection(TcpClient client, TcpMuxOptions options,
            X509Certificate2 certificate)
        {
            try
            {
                client.NoDelay = true;
                Log.Info($"New client connection: {client.Client.RemoteEndPoint}");
                Console.Write($"Opening connection to {options.TargetHost}:{options.TargetPort}...");

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
