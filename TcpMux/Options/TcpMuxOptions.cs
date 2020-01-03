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
        public string? Target { get; set; }
        public bool SniRouting { get; set; }
        public bool ForceDnsResolution { get; set; }
    }

    public enum RunningMode
    {
        Client,
        Server,
        RegisterCACert
    }
}