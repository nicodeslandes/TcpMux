using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Threading.Tasks;
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
            var rc = HostFactory.Run(x =>
            {
                x.Service<TcpMux.Program>(s =>
                {
                    s.ConstructUsing(name => new TcpMux.Program());
                    s.WhenStarted(tc => TcpMux.Program.Main(args));
                    s.WhenStopped(tc => { });
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

            var exitCode = (int)Convert.ChangeType(rc, rc.GetTypeCode());
            Environment.ExitCode = exitCode;
        }
    }
}
