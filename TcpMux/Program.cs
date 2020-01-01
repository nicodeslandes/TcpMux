using System;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using TcpMux.Options;

namespace TcpMux
{
    public partial class Program
    {
        public static int Main(string[] args)
        {
            var argumentParser = new ArgumentParser();
            TcpMuxOptions options;

            switch (argumentParser.ParseArguments(args))
            {
                case ParsingSuccess(var o):
                    options = o;
                    break;
                case ArgumentParsingError(var error):
                    Console.WriteLine("Incorrect arguments: {0}", error);
                    return 1;
                case NotEnoughArguments _:
                    ShowUsage();
                    return 1;
                default:
                    throw new InvalidOperationException("Incorrect parsing result");
            }

            if (options.RunningMode == RunningMode.RegisterCACert)
            {
                RegisterCACert();
                return 0;
            }

            var runner = new TcpMuxRunner(options);
            runner.Run();
            return 0;
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
        }
    }
}
