using System.Buffers;
using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text.Json;

namespace Portly.Core.PacketHandling
{
    /// <summary>
    /// Utility class that manages serialization, sending, and receiving of length-prefixed packets over TCP, with memory-efficient handling.
    /// </summary>
    public static class PacketHandler
    {
        private static readonly int _maxPacketSize = 64 * 1024;
        private static readonly byte[] _emptyPacketPayload = new byte[4];

        /// <summary>
        /// Serializes a Packet and prepends the length (int32, big-endian).
        /// </summary>
        public static byte[] SerializePacket(Packet packet)
        {
            if (packet == null)
                return _emptyPacketPayload;

            var payload = JsonSerializer.SerializeToUtf8Bytes(packet);
            if (payload.Length > _maxPacketSize) throw new InvalidOperationException($"Packet too large: {payload.Length} bytes");

            var buffer = ArrayPool<byte>.Shared.Rent(4 + payload.Length);

            // Length prefix
            BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(0, 4), payload.Length);
            payload.CopyTo(buffer.AsSpan(4, payload.Length));

            // Return a trimmed copy for sending
            var result = new byte[4 + payload.Length];
            Buffer.BlockCopy(buffer, 0, result, 0, result.Length);
            ArrayPool<byte>.Shared.Return(buffer);
            return result;
        }

        /// <summary>
        /// Sends a packet over a NetworkStream.
        /// </summary>
        public static async Task SendPacketAsync(NetworkStream stream, Packet packet)
        {
            var data = SerializePacket(packet);
            await stream.WriteAsync(data);
            await stream.FlushAsync();
        }

        /// <summary>
        /// Continuously reads packets from a NetworkStream, using ArrayPool buffers to reduce allocations.
        /// </summary>
        public static async Task ReadPacketsAsync(NetworkStream stream, Func<Packet, Task> onPacket, CancellationToken token = default)
        {
            var lengthBuffer = new byte[4];

            while (!token.IsCancellationRequested)
            {
                // Read 4-byte length prefix
                int bytesRead = 0;
                while (bytesRead < 4)
                {
                    int read = await stream.ReadAsync(lengthBuffer.AsMemory(bytesRead, 4 - bytesRead));
                    if (read == 0) throw new IOException("Connection closed");
                    bytesRead += read;
                }

                int packetLength = BinaryPrimitives.ReadInt32BigEndian(lengthBuffer.AsSpan(0, 4));

                // Packet length validation
                if (packetLength < 0) throw new IOException("Invalid packet length");
                if (packetLength > _maxPacketSize) throw new IOException($"Packet too large: {packetLength} bytes");

                Packet? packet = null;
                if (packetLength == 0)
                {
                    // Zero-length packet - heartbeat packet
                    packet = new Packet
                    {
                        Type = PacketType.Heartbeat,
                        Payload = []
                    };
                }
                else
                {
                    // Rent buffer for packet payload
                    byte[] dataBuffer = ArrayPool<byte>.Shared.Rent(packetLength);
                    bool clearDataBufferAfterUse = false;

                    try
                    {
                        int offset = 0;
                        while (offset < packetLength)
                        {
                            int read = await stream.ReadAsync(dataBuffer.AsMemory(offset, packetLength - offset));
                            if (read == 0) throw new IOException("Connection closed");
                            offset += read;
                        }

                        // Deserialize immediately
                        packet = JsonSerializer.Deserialize<Packet>(dataBuffer.AsSpan(0, packetLength));
                        if (packet != null && packet.Type == PacketType.Handshake)
                            clearDataBufferAfterUse = true;
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(dataBuffer, clearDataBufferAfterUse);
                    }
                }

                // Now process packet asynchronously without holding the buffer
                if (packet != null)
                {
                    await onPacket(packet);
                }
            }
        }

        public static async Task<Packet> ReceiveSinglePacketAsync(NetworkStream stream)
        {
            byte[] lengthBuffer = new byte[4];

            int read = 0;
            while (read < 4)
            {
                int r = await stream.ReadAsync(lengthBuffer.AsMemory(read, 4 - read));
                if (r == 0) throw new IOException("Connection closed");
                read += r;
            }

            int packetLength = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(lengthBuffer);
            if (packetLength < 0) throw new IOException("Invalid packet length");

            if (packetLength == 0)
            {
                return new Packet
                {
                    Type = PacketType.Heartbeat,
                    Payload = []
                };
            }

            byte[] buffer = ArrayPool<byte>.Shared.Rent(packetLength);
            try
            {
                int offset = 0;
                while (offset < packetLength)
                {
                    int r = await stream.ReadAsync(buffer.AsMemory(offset, packetLength - offset));
                    if (r == 0) throw new IOException("Connection closed");
                    offset += r;
                }

                var packet = System.Text.Json.JsonSerializer.Deserialize<Packet>(buffer.AsSpan(0, packetLength));
                return packet == null ? throw new IOException("Failed to deserialize packet") : packet;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
            }
        }
    }
}