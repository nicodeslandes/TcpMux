using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using DnsClient;
using Serilog;
using TcpMux.Extensions;
using TcpMux.Options;

namespace TcpMux
{
    public class TrafficRouter
    {
        private readonly TcpMuxOptions _options;
        private readonly Lazy<LookupClient> _dnsLookupClient;

        public TrafficRouter(TcpMuxOptions options)
        {
            _options = options;
            _dnsLookupClient = new Lazy<LookupClient>(() => new LookupClient());
        }

        public void RouteMessages(EndPointStream source, EndPointStream target)
        {
            var buffer = new byte[65536];
            Task.Run(async () =>
            {
                while (true)
                {
                    var read = await source.Stream.ReadAsync(buffer, 0, buffer.Length);
                    if (read == 0)
                    {
                        Log.Information("Connection {source} closed; closing connection {target}", source, target);
                        target.Stream.Close();
                        return;
                    }

                    Log.Information("Sending {count:N0} bytes from {source} to {target}...", read, source, target);
                    if (_options.DumpHex)
                        Console.WriteLine(Utils.HexDump(buffer, 0, read));

                    if (_options.DumpText)
                    {
                        var text = Encoding.UTF8.GetString(buffer, 0, read);
                        Console.WriteLine(text);
                    }
                    await target.Stream.WriteAsync(buffer, 0, read);
                    Log.Verbose("{read} bytes sent", read);
                }
            });
        }

        public EndPointStream ConnectToTarget(DnsEndPoint target)
        {
            var targetHost = target.Host;
            var targetPort = target.Port;

            Log.Information("Opening connection to {target}", target.ToShortString());
            if (_options.ForceDnsResolution)
            {
                targetHost = _dnsLookupClient.Value.GetHostEntry(targetHost).AddressList[0].ToString();
            }

            var tcpClient = new TcpClient(targetHost, targetPort) { NoDelay = true };
            Log.Information("Opened target connection: {endPoint}", tcpClient.Client.RemoteEndPoint);
            return new EndPointStream(tcpClient.GetStream(), tcpClient.Client.RemoteEndPoint);
        }
    }
}
