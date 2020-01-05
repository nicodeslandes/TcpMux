using System.IO;
using System.Net;
using System.Net.Sockets;

namespace TcpMux
{
    public struct EndPointStream
    {
        public EndPointStream(Stream stream, EndPoint endPoint)
        {
            Stream = stream;
            EndPoint = endPoint;
        }

        public EndPointStream(TcpClient client) : this(client.GetStream(), client.Client.RemoteEndPoint)
        {
        }

        public Stream Stream { get; }
        public EndPoint EndPoint { get; }

        public override string ToString() => EndPoint.ToString()!;
    }
}
