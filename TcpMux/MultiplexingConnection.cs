using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using TcpMux.Extensions;

namespace TcpMux
{
    class MultiplexingConnection
    {
        private readonly DnsEndPoint _multiplexerTarget;
        private TcpClient _client;
        private NetworkStream _stream;
        private readonly SemaphoreSlim _asyncLock = new SemaphoreSlim(1);
        private readonly object _lock = new object();
        private int _nextStreamId = 1;

        private Dictionary<int, MultiplexedStream> _multiplexedStreams = new Dictionary<int, MultiplexedStream>();

        public MultiplexingConnection(DnsEndPoint multiplexerTarget)
        {
            _multiplexerTarget = multiplexerTarget;
            _client = new TcpClient(_multiplexerTarget.Host, _multiplexerTarget.Port);
            _stream = _client.GetStream();
        }

        public void Connect()
        {

            Task.Run(async () =>
            {
                while (true)
                {
                    var packetLength = await ReadUShort();
                    if (packetLength < 4)
                    {
                        throw new InvalidOperationException($"Invalid packet size received: {packetLength}");
                    }

                    var streamId = await ReadInt();
                    var data = await ReadBytes((ushort)(packetLength - 4));
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

        private async Task<ArraySegment<byte>> ReadBytes(ushort size)
        {
            var buffer = new byte[65536];
            var read = 0;
            while (read < size)
            {
                var readBytes = await _stream.ReadAsync(buffer, read, size - read);
                if (readBytes <= 0)
                {
                    // TODO: Deal with this more elegantly
                    throw new Exception("Connection close");
                }

                read += readBytes;
            }

            return new ArraySegment<byte>(buffer, 0, size);
        }

        private async Task<ushort> ReadUShort()
        {
            var buffer = await ReadBytes(2);
            return (ushort)(buffer.Array![buffer.Offset] << 8 + buffer.Array[buffer.Offset + 1]);
        }

        private async Task<int> ReadInt()
        {
            var buffer = await ReadBytes(4);
            return buffer.Array![buffer.Offset] << 24
                + buffer.Array[buffer.Offset + 2] << 16
                + buffer.Array[buffer.Offset + 3] << 8
                + buffer.Array[buffer.Offset + 4];
        }

        internal Stream CreateMultiplexedStream()
        {
            return new MultiplexedStream(this, GetNextStreamId());
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
                await WriteUShort(packetLen);
                await WriteInt(id);
                await _stream.WriteAsync(arraySegment.Array!, bytesWritten, packetLen - 4);
                bytesWritten += packetLen - 4;
            }
        }

        private async Task WriteInt(int value)
        {
            var bytes = new byte[4];
            bytes[0] = (byte)(value >> 24);
            bytes[1] = (byte)(value >> 16);
            bytes[2] = (byte)(value >> 8);
            bytes[3] = (byte)value;

            await _stream.WriteAsync(bytes);
        }
        private async Task WriteUShort(ushort value)
        {
            var bytes = new byte[2];
            bytes[0] = (byte)(value >> 8);
            bytes[2] = (byte)value;

            await _stream.WriteAsync(bytes);
        }
    }

    class MultiplexedStream : Stream
    {
        private readonly MultiplexingConnection _connection;
        private readonly Channel<ArraySegment<byte>> _channel = Channel.CreateUnbounded<ArraySegment<byte>>();
        private readonly object _lock = new object();

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
