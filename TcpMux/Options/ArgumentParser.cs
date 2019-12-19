using System;
using System.Collections.Generic;
using System.Reflection;

namespace TcpMux.Options
{
    public class ArgumentParser
    {
        public ArgumentParser()
        {
        }

        public OptionParsingResult ParseArguments(string[] args)
        {
            var remainingArgs = new List<string>();
            var options = new TcpMuxOptions();

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
                        options.Verbose = true;
                        CertificateFactory.Verbose = true;
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
                            return OptionParsingResult.Error("Missing SSL CN");
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
                        return OptionParsingResult.Success(options);
                    default:
                        return OptionParsingResult.Error($"Invalid option: {arg}");
                }
            }

            if (remainingArgs.Count != 3)
            {
                return OptionParsingResult.NotEnoughArguments();
            }

            options.ListenPort = ushort.Parse(remainingArgs[0]);
            options.TargetHost = remainingArgs[1];
            options.TargetPort = ushort.Parse(remainingArgs[2]);

            return OptionParsingResult.Success(options);
        }
    }
}