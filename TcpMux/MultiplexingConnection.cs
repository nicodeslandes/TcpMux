using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Overby.Extensions.AsyncBinaryReaderWriter;
using TcpMux.Extensions;

namespace TcpMux
{
    class MultiplexingConnection
    {
        private readonly DnsEndPoint _multiplexerTarget;
        private readonly TcpClient _client;
        private readonly AsyncBinaryWriter _writer;
        private readonly AsyncBinaryReader _reader;
        private readonly SemaphoreSlim _asyncLock = new SemaphoreSlim(1);
        private readonly object _lock = new object();
        private int _nextStreamId = 1;

        private readonly Dictionary<int, MultiplexedStream> _multiplexedStreams = new Dictionary<int, MultiplexedStream>();

        public MultiplexingConnection(DnsEndPoint multiplexerTarget)
        {
            _multiplexerTarget = multiplexerTarget;
            _client = new TcpClient(_multiplexerTarget.Host, _multiplexerTarget.Port);
            
            var stream = _client.GetStream();
            _writer = new AsyncBinaryWriter(stream);
            _reader = new AsyncBinaryReader(stream);
        }

        public void Start()
        {
            Task.Run(async () =>
            {
                while (true)
                {
                    var packetLength = await _reader.ReadUInt16Async();
                    if (packetLength < 4)
                    {
                        throw new InvalidOperationException($"Invalid packet size received: {packetLength}");
                    }

                    var streamId = await _reader.ReadInt32Async();
                    var data = await _reader.ReadBytesAsync(packetLength - 4);
                    await WriteToMultiplexedStream(streamId, data);
                }
            });
        }

        private async ValueTask WriteToMultiplexedStream(int streamId, ArraySegment<byte> data)
        {
            if (!_multiplexedStreams.TryGetValue(streamId, out var multiplexedStream))
            {
                Console.WriteLine($"Error!! Unknown stream id: {streamId}");
                return;
            }

            await multiplexedStream.AddPendingData(data);
        }

        internal async Task<Stream> CreateMultiplexedStream(DnsEndPoint target)
        {
            // Create the new stream
            var stream = new MultiplexedStream(this, GetNextStreamId());

            // Send through the target descriptions
            using var writer = new AsyncBinaryWriter(stream);
            await writer.WriteAsync(target.Host);
            await writer.WriteAsync(target.Port);

            return stream;
        }

        private int GetNextStreamId()
        {
            lock (_lock)
            {
                return _nextStreamId++;
            }
        }

        internal async Task WriteMultiplexedData(int id, ArraySegment<byte> arraySegment)
        {
            int bytesWritten = 0;
            while (bytesWritten < arraySegment.Count)
            {
                using var l = await _asyncLock.TakeLock();

                var bytesToWrite = arraySegment.Count - bytesWritten;
                ushort packetLen = (ushort)Math.Min(bytesToWrite + 4, ushort.MaxValue);
                await _writer.WriteAsync(packetLen);
                await _writer.WriteAsync(id);
                await _writer.WriteAsync(arraySegment.Array!, bytesWritten, packetLen - 4);
                bytesWritten += packetLen - 4;
            }
        }
    }

    class MultiplexedStream : Stream
    {
        private readonly MultiplexingConnection _connection;
        private readonly Channel<ArraySegment<byte>> _channel = Channel.CreateUnbounded<ArraySegment<byte>>();

        public MultiplexedStream(MultiplexingConnection connection, int id)
        {
            _connection = connection;
            Id = id;
        }
        public int Id { get; }

        public async override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            var segment = await _channel.Reader.ReadAsync(cancellationToken);
            segment.CopyTo(buffer, offset);
            return segment.Count;
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return _connection.WriteMultiplexedData(Id, new ArraySegment<byte>(buffer, offset, count));
        }

        internal ValueTask AddPendingData(ArraySegment<byte> data)
        {
            return _channel.Writer.WriteAsync(data);
        }


        #region Stream methods

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => true;  
        
        public override long Length => throw new NotImplementedException();

        public override long Position { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return ReadAsync(buffer, offset, count).Result;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotImplementedException();
        }

        public override void SetLength(long value)
        {
            throw new NotImplementedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            WriteAsync(buffer, offset, count).Wait();
        }
        #endregion
    }
}
