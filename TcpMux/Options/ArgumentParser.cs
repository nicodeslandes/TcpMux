using System;
using System.Collections.Generic;
using System.ComponentModel;
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

            try
            {
                ReadArguments();
            }
            catch (InvalidOptionException ex)
            {
                return OptionParsingResult.Error(ex.Message);
            }

            if (options.RunningMode == RunningMode.RegisterCACert)
            {
                return OptionParsingResult.Success(options);
            }

            return OptionParsingResult.Success(options);

            void ReadArguments()
            {
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
                        case "-l":
                            options.ListenPort = ReadNextArgument<ushort>("Listen Port");
                            break;
                        case "-t":
                            options.Target = ReadNextArgument<string>("Target");
                            break;
                        case "-ssl":
                            options.Ssl = true;
                            break;
                        case "-sslOff":
                            options.SslOffload = true;
                            break;
                        case "-sslCn":
                            options.SslCn = ReadNextArgument<string>("SSL CN");
                            break;
                        case "-sni":
                            options.SniRouting = true;
                            break;
                        case "-forceDns":
                            options.ForceDnsResolution = true;
                            break;
                        case "-hex":
                            options.DumpHex = true;
                            break;
                        case "-text":
                            options.DumpText = true;
                            break;
                        case "-s":
                            SetRunningMode(RunningMode.Server);
                            break;
                        case "-regCA":
                            SetRunningMode(RunningMode.RegisterCACert);
                            // Skip remaining arguments
                            return;
                        default:
                            throw new InvalidOptionException($"Invalid option: {arg}");
                    }

                    T ReadNextArgument<T>(string description)
                    {
                        if (i >= args.Length || args[++i][0] == '-')
                        {
                            throw new InvalidOptionException($"Missing {description}");
                        }

                        var converter = TypeDescriptor.GetConverter(typeof(T));
                        return (T)converter.ConvertFromString(args[i]);
                    }
                }
            }

            void SetRunningMode(RunningMode mode)
            {
                if (options.RunningMode != null)
                {
                    throw new InvalidOptionException("Cannot use -regCA and -s together");
                }

                options.RunningMode = mode;
            }
        }
    }
}