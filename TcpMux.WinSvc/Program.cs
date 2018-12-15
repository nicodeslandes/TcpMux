using System;
using System.IO;
using System.Reactive.Disposables;
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

            TopshelfExitCode rc;
            using (logDisposables)
            {
                rc = HostFactory.Run(x =>
                {
                    Console.WriteLine(1);
                    x.Service((Action<Topshelf.ServiceConfigurators.ServiceConfigurator<TcpMux.Program>>)(s =>
                    {
                        Console.WriteLine(2);
                        s.ConstructUsing(name =>
                        {
                            logDisposables.Disposable = RedirectOutToLogfiles();
                            return new TcpMux.Program();
                        });

                        s.WhenStarted(tc => TcpMux.Program.Main(args));
                        s.WhenStopped(tc => { });
                    }));
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

        private static IDisposable RedirectOutToLogfiles()
        {
            var disposable = new CompositeDisposable();
            var logDirectoryPath = Path.Combine(Environment.GetEnvironmentVariable("TEMP"), "TcpMux");
            if (!Directory.Exists(logDirectoryPath)) Directory.CreateDirectory(logDirectoryPath);
            var logFile = Path.Combine(logDirectoryPath, "tcpmux.log");
            Console.WriteLine($"Redirecting logs to {logFile}");

            TextWriter NewTextWriter()
            {
                var writer = File.AppendText(logFile);
                disposable.Add(writer);
                return writer;
            }

            Console.SetOut(NewTextWriter());
            Console.SetError(NewTextWriter());

            return disposable;
        }
    }
}
