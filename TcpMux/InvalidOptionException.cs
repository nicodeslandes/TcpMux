using System;
using System.Collections.Generic;
using System.Text;

namespace TcpMux
{
    class InvalidOptionException : Exception
    {
        public InvalidOptionException(string message) : base(message)
        {

        }
    }

    class MissingParametersException : Exception
    {
    }
}
