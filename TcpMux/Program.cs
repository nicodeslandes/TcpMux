using System;
using System.Collections.Generic;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using NLog;

namespace TcpMux
{
    public class Program
    {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();
        
        public static int Main(string[] args)
        {
            TcpMuxOptions options;
            try
            {
                options = ParseCommandLineParameters(args);

                if (options.RegisterCACert)
                {
                    RegisterCACert();
                    return 0;
                }
            }
            catch (MissingParametersException) { return 1; }
            catch (InvalidOptionException ex)
            {
                Console.WriteLine("Invalid option: " + ex.Message);
                return 1;
            }

            var server = new TcpMuxServer(options);
            server.Start();

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
                Log.Info("Generating CA Certificate...");
                var cert =
                    CertificateFactory.GenerateCertificate(CertificateFactory.TcpMuxCASubjectDN, generateCA: true);
                Log.Info("Registering certificate in current user store");

                // Add CA certificate to Root store
                CertificateFactory.AddCertToStore(cert, StoreName.Root, StoreLocation.CurrentUser);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error: " + ex.Message);
            }
        }
    }
}
