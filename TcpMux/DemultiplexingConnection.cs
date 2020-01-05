using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace TcpMux
{
    class DemultiplexingConnection
    {
        private readonly EndPointStream _stream;
        private readonly BinaryWriter _writer;
        private readonly BinaryReader _reader;
        private readonly Channel<EndPointStream> _connections = Channel.CreateUnbounded<EndPointStream>();

        public DemultiplexingConnection(EndPointStream stream)
        {
            _stream = stream;
            _writer = new BinaryWriter(_stream.Stream);
            _reader = new BinaryReader(_stream.Stream);
        }
        public IAsyncEnumerable<EndPointStream> GetMultiplexedConnections()
        {
            return _connections.Reader.ReadAllAsync();
        }

        public void Start()
        {
            Task.Run(async () =>
            {
                while (true)
                {
                    var packet = await ReadPacket();
                }
            });
        }

        private Task<MultiplexedPacket> ReadPacket()
        {
            // TODO: Replace with Async Reader, and merge with MultiplexingConnection
            var id = _reader.ReadInt32();
            var length = _reader.ReadUInt16();
            var data = _reader.ReadBytes(length - 4);
            var packet = new MultiplexedPacket(id, new ArraySegment<byte>(data));
            return Task.FromResult(packet);
        }
    }

    class MultiplexedPacket
    {
        public MultiplexedPacket(int streamId, ArraySegment<byte> data)
        {
            StreamId = streamId;
            Data = data;
        }
        public int StreamId { get; }
        public ArraySegment<byte> Data { get; }
    }
}
