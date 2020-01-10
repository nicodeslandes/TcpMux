using System;
using System.Runtime.Serialization;

namespace TcpMux
{
    [Serializable]
    internal class ParsingError : Exception
    {
        public ParsingError()
        {
        }

        public ParsingError(string? message) : base(message)
        {
        }

        public ParsingError(string? message, Exception? innerException) : base(message, innerException)
        {
        }

        protected ParsingError(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}