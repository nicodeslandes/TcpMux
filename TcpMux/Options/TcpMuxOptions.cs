namespace TcpMux.Options
{
    public class TcpMuxOptions
    {
        public bool RegisterCACert { get; set; }
        public bool Verbose { get; set; }
        public bool Ssl { get; set; }
        public bool SslOffload { get; set; }
        public string? SslCn { get; set; }
        public bool DumpHex { get; set; }
        public bool DumpText { get; set; }
        public ushort ListenPort { get; set; }
        public string? TargetHost { get; set; }
        public ushort TargetPort { get; set; }
    }
}