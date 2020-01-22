using System;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using Serilog;
using TcpMux.Options;

namespace TcpMux
{
    public sealed class Program
    {
        public static int Main(string[] args)
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.Console()
                .CreateLogger();

            if (!TryParseArguments(args, out var options))
            {
                return 1;
            }

            if (options.Verbose)
            {
                Log.Logger = new LoggerConfiguration()
                    .MinimumLevel.Verbose()
                    .WriteTo.Console()
                    .CreateLogger();
            }

            switch (options.RunningMode)
            {
                case RunningMode.RegisterCACert:
                    RegisterCACert();
                    break;
                case RunningMode.Client:
                case null:
                    var runner = new TcpMuxRunner(options);
                    runner.Run();
                    break;
                case RunningMode.TunnelIn:
                case RunningMode.TunnelOut:
                    var tunnelRunner = new TunnelRunner(options);
                    tunnelRunner.Run();
                    break;
                default:
                    Console.Error.WriteLine($"Unsupported running mode: {options.RunningMode}");
                    return 1;
            }

            return 0;
        }

        private static bool TryParseArguments(string[] args,
            [NotNullWhen(returnValue: true)]out TcpMuxOptions? options)
        {
            switch (ArgumentParser.ParseArguments(args))
            {
                case ParsingSuccess(var o):
                    options = o;
                    return true;
                case ArgumentParsingError(var error):
                    Console.WriteLine("Incorrect arguments: {0}", error);
                    options = null;
                    return false;
                case NotEnoughArguments _:
                    ShowUsage();
                    options = null;
                    return false;
                default:
                    throw new InvalidOperationException("Incorrect parsing result");
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
                    CertificateFactory.GenerateCertificate(CertificateFactory.TcpMuxCASubject, generateCA: true);
                Console.WriteLine("Registering certificate in current user store");

                // Add CA certificate to Root store
                CertificateFactory.AddCertToStore(cert, StoreName.Root, StoreLocation.CurrentUser);
            }
#pragma warning disable CA1031 // Do not catch general exception types
            catch (Exception ex)
            {
                Console.WriteLine("Error: " + ex.Message);
            }
#pragma warning restore CA1031 // Do not catch general exception types
        }


        private static void ShowUsage()
        {
            var version = Assembly.GetEntryAssembly()
                                ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                                ?.InformationalVersion
                                .ToString();
            var versionLine = $"tcpmux - version {version}";
            Console.Error.WriteLine(
                new string('-', versionLine.Length) + "\n" +
                versionLine + "\n" +
                new string('-', versionLine.Length) + "\n\n" +
                "Usage: tcpmux [options]\n\n" +
                "options:\n" +
                "   -v: Verbose mode; display traffic\n" +
                "   -l <port>: Port to open\n" +
                "   -t <host>[:<port>]: Target; if port is omitted, defaults to the listening port\n" +
                "   -v: Verbose mode; display traffic\n" +
                "   -hex: Hex mode; display traffic as hex dump\n" +
                "   -text: Text mode; display traffic as text dump\n" +
                "   -ssl: perform ssl decoding and reencoding\n" +
                "   -sslOff: perform ssl off-loading (ie connect to the target via SSL, and expose a decrypted port)\n" +
                "   -sslCn: CN to use in the generated SSL certificate (defaults to <target_host>)\n" +
                "   -regCA: register self-signed certificate CA\n\n"
            );
        }
    }
}
