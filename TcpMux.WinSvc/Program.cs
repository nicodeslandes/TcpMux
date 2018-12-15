using System;
using System.IO;
using System.Reactive.Disposables;
using NLog;
using NLog.Config;
using NLog.Targets;
using Topshelf;

namespace TcpMux.WinSvc
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        static void Main(string[] args)
        {
            Console.WriteLine("Starting Windows service");
            var logDisposables = new SerialDisposable();
            //var logWriter = new TextWriter())
            //Console.SetOut()

            LoggingConfiguration loggingConfig = new LoggingConfiguration();


            TopshelfExitCode rc;
            using (logDisposables)
            {
                rc = HostFactory.Run(x =>
                {
                    Console.WriteLine(1);
                    x.Service<TcpMuxServer>(s =>
                    {
                        Console.WriteLine(2);
                        s.ConstructUsing(name =>
                        {
                            //logDisposables.Disposable = RedirectOutToLogfiles();
                            var logDirectoryPath = GetLogDirectory();
                            var logFile = Path.Combine(logDirectoryPath, $"tcpmux{name}.log");

                            var fileTarget = new FileTarget("fileTarget")
                            {
                                FileName = logFile,
                                Layout = "${longdate} ${level} ${message}  ${exception}"
                            };
                            loggingConfig.AddTarget(fileTarget);

                            LogManager.Configuration = loggingConfig;
                            var options = new TcpMuxOptions
                            {
                                TargetHost = "www.wikipedia.com",
                                ListenPort = 8080,
                                TargetPort = 443
                            };
                            return new TcpMuxServer(options);
                        });

                        s.WhenStarted(ts => ts.Start());
                        s.WhenStopped(ts => ts.Stop());
                    });

                    x.RunAsLocalSystem();

                    x.EnableServiceRecovery(r =>
                    {
                        r.RestartService(0);
                        r.RestartService(1);
                    });

                    x.SetDescription("Tcp Multiplexer service");
                    x.SetDisplayName("TcpMux");
                    x.SetServiceName("TcpMux");

                });
            }

            var exitCode = (int)Convert.ChangeType(rc, rc.GetTypeCode());
            Console.WriteLine($"Leaving; exit code: {rc}");

            Environment.ExitCode = exitCode;
            Console.WriteLine($"Leaving; exit code: {rc}");
        }

        private static string GetLogDirectory()
        {
            var logDirectoryPath = Path.Combine(Environment.GetEnvironmentVariable("TEMP"), "TcpMux");
            if (!Directory.Exists(logDirectoryPath)) Directory.CreateDirectory(logDirectoryPath);
            return logDirectoryPath;
        }
    }
}
