using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Overby.Extensions.AsyncBinaryReaderWriter;
using Serilog;

namespace TcpMux
{
    class DemultiplexingConnection
    {
        private readonly EndPointStream _stream;
        private readonly AsyncBinaryWriter _writer;
        private readonly AsyncBinaryReader _reader;
        private readonly Channel<EndPointStream> _newConnections = Channel.CreateUnbounded<EndPointStream>();
        private readonly Dictionary<int, DemultiplexedStream> _demultiplexedStreams
            = new Dictionary<int, DemultiplexedStream>();

        public DemultiplexingConnection(EndPointStream stream)
        {
            _stream = stream;
            _writer = new AsyncBinaryWriter(_stream.Stream);
            _reader = new AsyncBinaryReader(_stream.Stream);
            Start();
        }
        public IAsyncEnumerable<EndPointStream> GetMultiplexedConnections()
        {
            return _newConnections.Reader.ReadAllAsync();
        }

        public void Start()
        {
            Task.Run(async () =>
            {
                while (true)
                {
                    var packet = await ReadPacket();
                    if (!_demultiplexedStreams.TryGetValue(packet.StreamId, out var demultiplexedStream))
                    {
                        // New multiplexed connection
                        demultiplexedStream = new DemultiplexedStream(this, packet.StreamId);

                        _demultiplexedStreams[packet.StreamId] = demultiplexedStream;

                        Task.Run(async () =>
                        {

                            // Read the target from it
                            Log.Verbose("New multiplexed stream {id} received; reading stream target", packet.StreamId);
                            var target = await TryReadTarget(demultiplexedStream);
                            if (target == null)
                            {
                                Log.Warning("Invalid target name received. Closing connection to {endPoint}",
                                    _stream.EndPoint);
                                _stream.Stream.Close();
                            }

                            // Yield a new demultiplexed stream
                            Log.Information("New multiplexed stream {id} received for target {target}",
                                packet.StreamId, target);
                            await _newConnections.Writer.WriteAsync(new EndPointStream(demultiplexedStream, target));
                        });
                    }
                }
            });
        }

        private async Task<DnsEndPoint?> TryReadTarget(DemultiplexedStream stream)
        {
            var reader = new AsyncBinaryReader(stream);
            var targetString = await reader.ReadStringAsync();
            try
            {
                return EndPointParser.Parse(targetString);
            }
            catch (ParsingError ex)
            {
                Log.Warning(ex, "Failed to read target: {error}", ex.Message);
                return null;
            }
        }

        private async Task<MultiplexedPacket> ReadPacket()
        {
            Log.Verbose("Reading multiplexed packet from {endpoint}", _stream);
            var id = await _reader.ReadInt32Async();
            Log.Verbose("id: {id} (0x{id:x})", id, id);
            var length = await _reader.ReadUInt16Async();
            Log.Verbose("length: {length}", length);
            var data = await _reader.ReadBytesAsync(length - 4);
            var packet = new MultiplexedPacket(id, new ArraySegment<byte>(data));
            Log.Debug("Read multiplexed packet with id {id}, length {length}", id, length);
            return packet;
        }

        private async Task WritePacket(MultiplexedPacket packet)
        {
            // TODO: Remove copy-pasted code from MultiplexedStream
            await _writer.WriteAsync(packet.Data.Count + 4);
            await _writer.WriteAsync(packet.StreamId);
            await _writer.WriteAsync(packet.Data.Array!, packet.Data.Offset, packet.Data.Count);
        }

        internal async Task WriteMultiplexedData(int id, ArraySegment<byte> data)
        {
            int bytesWritten = 0;
            while (bytesWritten < data.Count)
            {
                var bytesToWrite = data.Count - bytesWritten;
                var packetLen = Math.Min(bytesToWrite + 4, ushort.MaxValue);
                await WritePacket(new MultiplexedPacket(id, data.Slice(bytesWritten, packetLen - 4)));
                bytesWritten += packetLen - 4;
            }
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

    internal class DemultiplexedStream : Stream
    {
        private readonly DemultiplexingConnection _connection;
        private readonly Channel<ArraySegment<byte>> _channel = Channel.CreateUnbounded<ArraySegment<byte>>();
        private ArraySegment<byte>? _pendingData;

        public DemultiplexedStream(DemultiplexingConnection connection, int id)
        {
            _connection = connection;
            Id = id;
        }
        public int Id { get; }

        public async override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            Log.Verbose("Reading {count} bytes from demultiplexed stream {id}", count, Id);

            // TODO: Make thread-safe
            var data = _pendingData ?? await _channel.Reader.ReadAsync();

            Log.Verbose("Processing {count} bytes from demultiplexed stream {id}", data.Count, Id);

            var copiedByteCount = Math.Min(data.Count, count);
            data.CopyTo(new ArraySegment<byte>(buffer, offset, count));

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
            Log.Verbose("Writing {count} bytes to demultiplexed stream {id}", count, Id);
            return _connection.WriteMultiplexedData(Id, new ArraySegment<byte>(buffer, offset, count));
        }

        internal ValueTask ForwardData(ArraySegment<byte> data)
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
