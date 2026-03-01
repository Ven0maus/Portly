using MessagePack;
using Portly.Core.Interfaces;
using System.Buffers;
using System.Buffers.Binary;
using System.Net.Sockets;

namespace Portly.Core.PacketHandling
{
    /// <summary>
    /// Utility class that manages serialization, sending, and receiving of length-prefixed packets over TCP, with memory-efficient handling.
    /// </summary>
    public static class PacketProtocol
    {
        private static int _maxPacketSize = 64 * 1024;
        private static readonly byte[] _emptyPacketPayload = new byte[4]; // 0-length prefix
        private static readonly Packet _KeepAlivePacket = Packet.Create(PacketType.KeepAlive, Array.Empty<byte>(), false);

        private static readonly MessagePackSerializerOptions _messagePackSerializerOptions = MessagePackSerializerOptions.Standard
            .WithSecurity(MessagePackSecurity.UntrustedData);

        private static bool _debugModeEnabled = false;

        /// <summary>
        /// Allows overriding the max packet size. <br>Default: 64 KB</br>
        /// </summary>
        /// <param name="maxPacketSize"></param>
        public static void SetMaxPacketSize(int maxPacketSize = 64 * 1024)
        {
            _maxPacketSize = maxPacketSize;
        }

        /// <summary>
        /// Enabling debug mode will log extra information about packets to the console.
        /// </summary>
        /// <param name="enabled"></param>
        public static void SetDebugMode(bool enabled)
        {
            _debugModeEnabled = enabled;
        }

        /// <summary>
        /// Sends a packet over a NetworkStream.
        /// </summary>
        internal static async Task SendPacketAsync(NetworkStream stream, Packet packet, IPacketCrypto? crypto = null)
        {
            if (packet == null || packet.Identifier.Id == (int)PacketType.KeepAlive)
            {
                if (_debugModeEnabled) Console.WriteLine("Send KeepAlive packet.");
                await stream.WriteAsync(_emptyPacketPayload);
                return;
            }

            try
            {
                // Only encrypt once
                if (packet.SerializedPacket == null)
                    packet = crypto?.Encrypt(packet) ?? packet;
            }
            catch (Exception ex)
            {
                throw new IOException("Failed to encrypt packet: " + ex.Message, ex);
            }

            byte[] payload = packet.SerializedPacket ??= MessagePackSerializer.Serialize(packet, options: _messagePackSerializerOptions);

            if (payload.Length > _maxPacketSize)
                throw new InvalidOperationException($"Packet too large: {payload.Length}");

            if (_debugModeEnabled) Console.WriteLine($"Sending packet of size {payload.Length}.");

            byte[] buffer = ArrayPool<byte>.Shared.Rent(4 + payload.Length);

            try
            {
                var span = buffer.AsSpan();

                BinaryPrimitives.WriteInt32BigEndian(span.Slice(0, 4), payload.Length);
                payload.CopyTo(span.Slice(4));

                await stream.WriteAsync(buffer.AsMemory(0, 4 + payload.Length));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer, packet.Encrypted || packet.Identifier.Id == (int)PacketType.Handshake);
            }
        }

        /// <summary>
        /// Continuously reads packets from a NetworkStream, using ArrayPool buffers to reduce allocations.
        /// </summary>
        internal static async Task ReadPacketsAsync(NetworkStream stream, Func<Packet, Task> onPacket, IPacketCrypto? crypto = null, CancellationToken token = default)
        {
            var lengthBuffer = new byte[4];

            while (!token.IsCancellationRequested)
            {
                // Read 4-byte length prefix
                int bytesRead = 0;
                while (bytesRead < 4)
                {
                    int read = await stream.ReadAsync(lengthBuffer.AsMemory(bytesRead, 4 - bytesRead), token);
                    if (read == 0) throw new IOException("Connection closed");
                    bytesRead += read;
                }

                int packetLength = BinaryPrimitives.ReadInt32BigEndian(lengthBuffer.AsSpan(0, 4));

                // Packet length validation
                if (packetLength < 0) throw new IOException("Invalid packet length");
                if (packetLength > _maxPacketSize) throw new IOException($"Packet too large: {packetLength} bytes");

                Packet packet;
                if (packetLength == 0)
                {
                    // Zero-length packet - KeepAlive packet
                    packet = _KeepAlivePacket;
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
                            int read = await stream.ReadAsync(dataBuffer.AsMemory(offset, packetLength - offset), token);
                            if (read == 0) throw new IOException("Connection closed");
                            offset += read;
                        }

                        try
                        {
                            packet = MessagePackSerializer.Deserialize<Packet>(dataBuffer.AsMemory(0, packetLength), _messagePackSerializerOptions, cancellationToken: token);
                        }
                        catch (Exception ex)
                        {
                            throw new IOException("Failed to deserialize packet: " + ex.Message);
                        }

                        if (packet.Encrypted || packet.Identifier.Id == (int)PacketType.Handshake)
                            clearDataBufferAfterUse = true;
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(dataBuffer, clearDataBufferAfterUse);
                    }
                }

                try
                {
                    packet = crypto?.Decrypt(packet) ?? packet;
                }
                catch (Exception ex)
                {
                    throw new IOException("Failed to decrypt packet: " + ex.Message, ex);
                }

                if (_debugModeEnabled)
                    Console.WriteLine(packetLength == 0 ? "Received KeepAlive packet." : $"Received packet of length {packetLength}.");

                await onPacket(packet);
            }
        }

        internal static async Task<Packet> ReceiveSinglePacketAsync(NetworkStream stream, IPacketCrypto? crypto = null, CancellationToken token = default)
        {
            byte[] lengthBuffer = new byte[4];

            int read = 0;
            while (read < 4)
            {
                int r = await stream.ReadAsync(lengthBuffer.AsMemory(read, 4 - read), token);
                if (r == 0) throw new IOException("Connection closed");
                read += r;
            }

            int packetLength = BinaryPrimitives.ReadInt32BigEndian(lengthBuffer);
            if (packetLength < 0) throw new IOException("Invalid packet length");

            if (packetLength == 0)
            {
                if (_debugModeEnabled) Console.WriteLine("Received KeepAlive packet.");
                return _KeepAlivePacket;
            }

            byte[] buffer = ArrayPool<byte>.Shared.Rent(packetLength);
            bool clearDataBufferAfterUse = false;

            try
            {
                int offset = 0;
                while (offset < packetLength)
                {
                    int r = await stream.ReadAsync(buffer.AsMemory(offset, packetLength - offset), token);
                    if (r == 0) throw new IOException("Connection closed");
                    offset += r;
                }

                Packet packet;
                try
                {
                    packet = MessagePackSerializer.Deserialize<Packet>(buffer.AsMemory(0, packetLength), _messagePackSerializerOptions, cancellationToken: token);
                    clearDataBufferAfterUse = packet.Encrypted || packet.Identifier.Id == (int)PacketType.Handshake;
                }
                catch (Exception ex)
                {
                    throw new IOException("Failed to deserialize packet: " + ex.Message, ex);
                }

                try
                {
                    packet = crypto?.Decrypt(packet) ?? packet;
                }
                catch (Exception ex)
                {
                    throw new IOException("Failed to decrypt packet: " + ex.Message, ex);
                }

                if (_debugModeEnabled) Console.WriteLine($"Received packet of length {packetLength}.");

                return packet;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer, clearDataBufferAfterUse);
            }
        }
    }
}