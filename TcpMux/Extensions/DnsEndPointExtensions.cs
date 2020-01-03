using System.Net;

namespace TcpMux.Extensions
{
    public static class DnsEndPointExtensions
    {
        public static string ToShortString(this DnsEndPoint endPoint)
            => (endPoint.Port == 0) ? endPoint.Host : $"{endPoint.Host}:{endPoint.Port}";
    }
}
