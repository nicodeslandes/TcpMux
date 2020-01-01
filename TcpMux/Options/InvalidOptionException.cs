using System;
using System.Runtime.Serialization;

namespace TcpMux.Options
{
    [Serializable]
    internal class InvalidOptionException : Exception
    {
        public InvalidOptionException()
        {
        }

        public InvalidOptionException(string? message) : base(message)
        {
        }

        public InvalidOptionException(string? message, Exception? innerException) : base(message, innerException)
        {
        }

        protected InvalidOptionException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}