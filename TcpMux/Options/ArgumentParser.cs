using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Net;

namespace TcpMux.Options
{
    public sealed class ArgumentParser
    {
        public static OptionParsingResult ParseArguments(string[] args)
        {
            var remainingArgs = new List<string>();
            var options = new TcpMuxOptions();

            try
            {
                ReadArguments();

                // Default the target port to the listening port
                if (options.Target?.Port == 0)
                {
                    options.Target = new DnsEndPoint(options.Target.Host, options.ListenPort);
                }

                if (options.SniRouting && !options.Ssl)
                {
                    throw new InvalidOptionException("Sni options requires SSL handling with the -ssl option");
                }

                if (options.Target is null && !options.SniRouting && options.RunningMode != RunningMode.RegisterCACert && options.MultiplexingMode == MultiplexingMode.None)
                {
                    throw new InvalidOptionException("Please specify a target with the -t option");
                }

                if (options.MultiplexingMode == MultiplexingMode.Demultiplexer && options.ListenPort == 0)
                {
                    throw new InvalidOptionException("In demultiplexing mode, please specify a listening port with " +
                        "the -l option");
                }
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
                            options.Target = ReadNextArgument<DnsEndPoint>("Target");
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
                        case "-mux":
                            options.MultiplexingMode = MultiplexingMode.Multiplexer;
                            options.MultiplexingTarget = ReadNextArgument<DnsEndPoint>("MultiplexingTarget");
                            break;
                        case "-demux":
                            options.MultiplexingMode = MultiplexingMode.Demultiplexer;
                            break;
                        default:
                            throw new InvalidOptionException($"Invalid option: {arg}");
                    }

                    T ReadNextArgument<T>(string description)
                    {
                        if (typeof(T) == typeof(DnsEndPoint))
                        {
                            var str = ReadNextArgument<string>(description);
                            try
                            {
                                return (T)(object)EndPointParser.Parse(str);
                            }
                            catch (ParsingError ex)
                            {
                                throw new InvalidOptionException($"Incorrect value for {description}: {str}", ex);
                            }
                        }

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