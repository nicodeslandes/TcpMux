using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace TcpMux
{
    public static class EndPointParser
    {
        public static DnsEndPoint Parse(string str)
        {
            void ThrowParsingException()
            {
                throw new ParsingError($"Invalid value: '{str}': please use <host>[:<port>] format");
            }

            if (string.IsNullOrWhiteSpace(str))
            {
                ThrowParsingException();
            }

            var elements = str.Split(':');
            if (elements.Length > 2)
            {
                ThrowParsingException();
            }

            if (elements.Length == 2)
            {
                // Parse the port
                if (ushort.TryParse(elements[1], out var port))
                {
                    return new DnsEndPoint(elements[0], port);
                }
                else
                {
                    throw new ParsingError($"Invalid port: {elements[1]}");
                }
            }

            return new DnsEndPoint(elements[0], 0);
        }
    }
}
