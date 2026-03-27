using Portly.Core.Configuration.Settings;
using Portly.Core.Interfaces;
using Portly.Core.Utilities.Logging;
using System.Buffers;
using System.Buffers.Binary;
using System.Net.Sockets;

namespace Portly.Core.PacketHandling.Protocols
{
    /// <summary>
    /// Provides a default implementation that uses MessagePack and AES encryption support.
    /// </summary>
    public sealed class DefaultPacketProtocol : IPacketProtocol
    {
        private Version? _version;
        /// <inheritdoc/>
        public Version Version => _version ??= new Version(1, 0, 0);

        private static readonly byte[] _emptyPacketPayload = new byte[4]; // 0-length prefix
        private static readonly IPacket _KeepAlivePacket = Packet.Create(PacketType.KeepAlive, Array.Empty<byte>());

        private readonly int _idleTimeout, _writeTimeout, _maxPacketSize;
        private readonly ILogProvider? _logProvider;
        private readonly IPacketSerializationProvider _packetSerializer;

        private IEncryptionProvider? _encryptionProvider;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="connectionSettings"></param>
        /// <param name="packetSerializationProvider"></param>
        /// <param name="logProvider"></param>
        public DefaultPacketProtocol(ConnectionSettings connectionSettings, IPacketSerializationProvider packetSerializationProvider, ILogProvider? logProvider = null)
        {
            _idleTimeout = connectionSettings.IdleTimeoutSeconds;
            _writeTimeout = connectionSettings.WriteTimeoutSeconds;
            _maxPacketSize = connectionSettings.MaxRequestSizeBytes;
            _logProvider = logProvider;
            _packetSerializer = packetSerializationProvider;
        }

        /// <inheritdoc/>
        public void SetEncryptionProvider(IEncryptionProvider encryptionProvider)
        {
            _encryptionProvider = encryptionProvider;
        }

        /// <inheritdoc/>
        public async Task ReadPacketsAsync(NetworkStream stream, Func<IPacket, Task> onPacket, CancellationToken cancellationToken = default)
        {
            var lengthBuffer = new byte[4];

            while (!cancellationToken.IsCancellationRequested)
            {
                using var cts = _idleTimeout <= 0 ? null : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts?.CancelAfter(TimeSpan.FromSeconds(_idleTimeout));
                var readToken = cts?.Token ?? cancellationToken;

                // Read 4-byte length prefix
                int bytesRead = 0;
                while (bytesRead < 4)
                {
                    int read = await stream.ReadAsync(lengthBuffer.AsMemory(bytesRead, 4 - bytesRead), readToken);
                    if (read == 0) throw new IOException("Connection closed");
                    bytesRead += read;
                }

                int packetLength = BinaryPrimitives.ReadInt32BigEndian(lengthBuffer.AsSpan(0, 4));

                // Packet length validation
                if (packetLength < 0) throw new IOException("Invalid packet length");
                if (packetLength > _maxPacketSize) throw new IOException($"Packet too large: {packetLength} bytes");

                IPacket packet;
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
                            int read = await stream.ReadAsync(dataBuffer.AsMemory(offset, packetLength - offset), readToken);
                            if (read == 0) throw new IOException("Connection closed");
                            offset += read;
                        }

                        try
                        {
                            packet = _packetSerializer.Deserialize<Packet>(dataBuffer.AsMemory(0, packetLength), cancellationToken);
                        }
                        catch (Exception ex)
                        {
                            throw new IOException("Failed to deserialize packet: " + ex.Message);
                        }

                        if (packet.Encrypted || packet.Identifier.Id == (int)PacketType.SecureHandshake)
                            clearDataBufferAfterUse = true;
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(dataBuffer, clearDataBufferAfterUse);
                    }
                }

                try
                {
                    if (packet.Encrypted)
                        packet.Decrypt(_encryptionProvider);
                }
                catch (Exception ex)
                {
                    throw new IOException("Failed to decrypt packet: " + ex.Message, ex);
                }

                _logProvider?.Log(packetLength == 0 ? "Received KeepAlive packet." : $"Received packet of length {packetLength}.", LogLevel.Debug);

                await onPacket(packet);
            }
        }

        /// <inheritdoc/>
        public async Task<IPacket> ReceiveSinglePacketAsync(NetworkStream stream, CancellationToken cancellationToken = default)
        {
            byte[] lengthBuffer = new byte[4];

            int read = 0;
            while (read < 4)
            {
                int r = await stream.ReadAsync(lengthBuffer.AsMemory(read, 4 - read), cancellationToken);
                if (r == 0) throw new IOException("Connection closed");
                read += r;
            }

            int packetLength = BinaryPrimitives.ReadInt32BigEndian(lengthBuffer);
            if (packetLength < 0) throw new IOException("Invalid packet length");

            if (packetLength == 0)
            {
                _logProvider?.Log("Received KeepAlive packet.", LogLevel.Debug);
                return _KeepAlivePacket;
            }

            if (packetLength > _maxPacketSize) throw new IOException($"Packet too large: {packetLength} bytes");

            byte[] buffer = ArrayPool<byte>.Shared.Rent(packetLength);
            bool clearDataBufferAfterUse = false;

            try
            {
                int offset = 0;
                while (offset < packetLength)
                {
                    int r = await stream.ReadAsync(buffer.AsMemory(offset, packetLength - offset), cancellationToken);
                    if (r == 0) throw new IOException("Connection closed");
                    offset += r;
                }

                IPacket packet;
                try
                {
                    packet = _packetSerializer.Deserialize<Packet>(buffer.AsMemory(0, packetLength), cancellationToken);
                    clearDataBufferAfterUse = packet.Encrypted || packet.Identifier.Id == (int)PacketType.SecureHandshake;
                }
                catch (Exception ex)
                {
                    throw new IOException("Failed to deserialize packet: " + ex.Message, ex);
                }

                try
                {
                    if (packet.Encrypted)
                        packet.Decrypt(_encryptionProvider);
                }
                catch (Exception ex)
                {
                    throw new IOException("Failed to decrypt packet: " + ex.Message, ex);
                }

                _logProvider?.Log($"Received packet of length {packetLength}.", LogLevel.Debug);

                return packet;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer, clearDataBufferAfterUse);
            }
        }

        /// <inheritdoc/>
        public async Task SendPacketAsync(NetworkStream stream, IPacket packet, bool encrypted, CancellationToken cancellationToken = default)
        {
            if (packet == null || packet.Identifier.Id == (int)PacketType.KeepAlive)
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_writeTimeout));

                _logProvider?.Log("KeepAlive check send.", LogLevel.Debug);
                await stream.WriteAsync(_emptyPacketPayload, cts.Token);
                return;
            }

            try
            {
                if (encrypted)
                    packet.Encrypt(_encryptionProvider);
            }
            catch (Exception ex)
            {
                throw new IOException("Failed to encrypt packet: " + ex.Message, ex);
            }

            byte[] payload = _packetSerializer.Serialize(packet, cancellationToken);

            if (payload.Length > _maxPacketSize)
                throw new InvalidOperationException($"Packet too large: {payload.Length}");

            _logProvider?.Log($"Sending packet of size {payload.Length}.", LogLevel.Debug);

            byte[] buffer = ArrayPool<byte>.Shared.Rent(4 + payload.Length);

            try
            {
                var span = buffer.AsSpan();

                BinaryPrimitives.WriteInt32BigEndian(span.Slice(0, 4), payload.Length);
                payload.CopyTo(span.Slice(4));

                // Use a cancellation token if a write timeout is specified
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_writeTimeout));

                await stream.WriteAsync(buffer.AsMemory(0, 4 + payload.Length), cts.Token);
            }
            catch (OperationCanceledException)
            {
                throw new IOException("Write operation timed out.");
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer, packet.Encrypted || packet.Identifier.Id == (int)PacketType.SecureHandshake);
            }
        }
    }
}
