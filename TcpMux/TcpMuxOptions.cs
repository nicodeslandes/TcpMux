namespace TcpMux
{
    public class TcpMuxOptions
    {
        public bool Ssl = false;
        public bool SslOffload = false;
        public bool DumpHex = false;
        public bool DumpText = false;
        public string SslCn = null;

        public bool RegisterCACert = false;

        public string TargetHost { get; set; }
        public ushort TargetPort { get;  set; }
        public ushort ListenPort { get; set; }
    }
}
