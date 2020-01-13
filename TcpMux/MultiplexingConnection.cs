using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Overby.Extensions.AsyncBinaryReaderWriter;
using Serilog;
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

            Start();
        }

        public void Start()
        {
            // Start reading task, which reads data from the network, and pushes data to the multiplexed streams
            Task.Run(async () =>
            {
                while (true)
                {
                    Log.Verbose("Reading multiplexed packet from {endpoint}", _client.Client.RemoteEndPoint);
                    var streamId = await _reader.ReadInt32Async();
                    Log.Verbose("id: {id} (0x{id:x})", streamId, streamId);
                    var packetLength = await _reader.ReadUInt16Async();
                    Log.Verbose("length: {length}", packetLength);
                    var data = await _reader.ReadBytesAsync(packetLength);
                    Log.Debug("Read multiplexed packet with id {id}, length {length}", streamId, packetLength);
                    await WriteToMultiplexedStream(streamId, data);
                }
            });

            // Start writing task, which receives data written to the multiplexed streams, and writes it to the network


        }

        private async ValueTask WriteToMultiplexedStream(int streamId, ArraySegment<byte> data)
        {
            if (!_multiplexedStreams.TryGetValue(streamId, out var multiplexedStream))
            {
                Log.Error("Unknown stream id: {streamId}", streamId);
                return;
            }

            await multiplexedStream.AddPendingData(data);
        }

        internal async Task<Stream> CreateMultiplexedStream(DnsEndPoint target)
        {
            // Create the new stream
            var stream = new MultiplexedStream(this, GetNextStreamId());
            _multiplexedStreams[stream.Id] = stream;

            // Send through the target descriptions
            Log.Verbose("Writing to multiplexed stream {id}: {host},{port}", stream.Id, target.Host, target.Port);
            using var writer = new AsyncBinaryWriter(stream);
            await writer.WriteAsync(target.Host);
            await writer.WriteAsync((ushort)target.Port);
            await writer.FlushAsync();
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
            Log.Verbose("Writing to multiplexing connection; stream id: {id}; data length: {count} bytes",
                id, arraySegment.Count);

            int bytesWritten = 0;
            while (bytesWritten < arraySegment.Count)
            {
                using var l = await _asyncLock.TakeLock();

                var bytesToWrite = arraySegment.Count - bytesWritten;
                ushort packetSize = (ushort)Math.Min(bytesToWrite, ushort.MaxValue);
                await _writer.WriteAsync(id);
                await _writer.WriteAsync(packetSize);
                await _writer.WriteAsync(arraySegment.Array!, bytesWritten, packetSize);
                bytesWritten += packetSize;
            }

            await _writer.FlushAsync();
            Log.Verbose("{count} bytes successfully written to stream {id}", arraySegment.Count, id);
        }
    }

    class MultiplexedStream : Stream
    {
        private readonly MultiplexingConnection _connection;
        private readonly Channel<ArraySegment<byte>> _channel = Channel.CreateUnbounded<ArraySegment<byte>>();
        private ArraySegment<byte>? _pendingData;

        public MultiplexedStream(MultiplexingConnection connection, int id)
        {
            _connection = connection;
            Id = id;
        }
        public int Id { get; }

        public async override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            Log.Verbose("Reading {count} bytes from stream {id}", count, Id);
            // TODO: Remove duplication with DemultiplexingConnection
            var data = _pendingData ?? await _channel.Reader.ReadAsync(cancellationToken);
            var copiedByteCount = Math.Min(data.Count, count);
            data.Slice(0, copiedByteCount).CopyTo(buffer, offset);

            if (copiedByteCount < data.Count)
            {
                _pendingData = data.Slice(copiedByteCount);
            }
            else
            {
                // TODO: See if there isn't more pending data to write to the buffer
                _pendingData = null;
            }

            return copiedByteCount;
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
