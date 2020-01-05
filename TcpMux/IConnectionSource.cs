using System.Collections.Generic;
using System.IO;
using System.Net;

namespace TcpMux
{
    public interface IConnectionSource
    {
        IAsyncEnumerable<EndPointStream> GetClientConnections();
    }
}