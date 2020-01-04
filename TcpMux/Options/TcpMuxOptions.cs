using System.Net;

namespace TcpMux.Options
{
    public class TcpMuxOptions
    {
        public RunningMode? RunningMode { get; set; }
        public bool Verbose { get; set; }
        public bool Ssl { get; set; }
        public bool SslOffload { get; set; }
        public string? SslCn { get; set; }
        public bool DumpHex { get; set; }
        public bool DumpText { get; set; }
        public ushort ListenPort { get; set; }
        public DnsEndPoint? Target { get; set; }
        public bool SniRouting { get; set; }
        public bool ForceDnsResolution { get; set; }
        public MultiplexingMode MultiplexingMode { get; set; }
        public DnsEndPoint? MultiplexingTarget { get; set; }
        public ushort MultiplexingListeningPort { get; internal set; }
    }

    public enum RunningMode
    {
        Client,
        Server,
        RegisterCACert
    }

    public enum MultiplexingMode
    {
        None,
        Multiplexer,
        Demultiplexer
    }
}