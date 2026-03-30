using Portly.Abstractions;
using Portly.Infrastructure;
using Portly.Infrastructure.Configuration;
using Portly.Protocol;
using System.Net;

namespace Portly.Runtime
{
    /// <summary>
    /// Represent a data container for a client that is connected to a server.
    /// </summary>
    /// <param name="packetProtocol"></param>
    /// <param name="configuration"></param>
    /// <param name="connection"></param>
    /// <param name="keepAliveManager"></param>
    /// <param name="onDisconnect"></param>
    internal class ServerClient(IPacketProtocol packetProtocol, ServerConfiguration configuration, ITransportConnection connection,
        KeepAliveManager<ServerClient> keepAliveManager, EventHandler<IServerClient>? onDisconnect) : IServerClient
    {
        public ITransportConnection Connection { get; } = connection;
        public Stream Stream { get; } = connection.Stream;
        public IPAddress IpAddress { get; } = (connection.RemoteEndPoint as IPEndPoint
                 ?? throw new InvalidOperationException("Expected IPEndPoint.")).Address.MapToIPv6();
        public CancellationTokenSource Cancellation { get; } = new();
        public ClientRateLimiter ClientRateLimiter { get; } = new(configuration.RateLimits);
        public Task? ClientTask { get; set; }

        public Guid Id { get; } = Guid.NewGuid();
        internal IPacketProtocol PacketProtocol { get; } = packetProtocol;

        private int _disconnected = 0;
        private readonly IPacketProtocol _packetProtocol = packetProtocol;
        private readonly KeepAliveManager<ServerClient> _keepAliveManager = keepAliveManager;
        private readonly SemaphoreSlim _sendLock = new(1, 1);

        private readonly EventHandler<IServerClient>? _onDisconnect = onDisconnect;

        public async Task SendPacketAsync(Packet packet, bool encrypt, CancellationToken cancellationToken = default)
        {
            if (!Connection.IsConnected)
                throw new InvalidOperationException("Client not connected.");

            await _sendLock.WaitAsync();
            try
            {
                await _packetProtocol.SendPacketAsync(Stream, packet, encrypt, cancellationToken);
                _keepAliveManager.UpdateLastSent(this);
            }
            finally
            {
                _sendLock.Release();
            }
        }

        public async Task DisconnectAsync(string reason = "", bool informClient = true)
        {
            // Send disconnection packet before cancel
            if (informClient)
                await SendPacketAsync(Packet.Create(PacketType.Disconnect, reason), default);
            await DisconnectInternalAsync();
        }

        /// <summary>
        /// Disconnects the client without sending a disconnect packet.
        /// <br>Usually called when we are aware the client is already no longer receiving any packets.</br>
        /// </summary>
        /// <returns></returns>
        internal async Task DisconnectInternalAsync()
        {
            if (Interlocked.Exchange(ref _disconnected, 1) == 1)
                return;

            try { Cancellation.Cancel(); } catch { }
            try { Stream.Close(); } catch { }
            try { await Connection.CloseAsync(); } catch { }

            _keepAliveManager.Unregister(this);
            _onDisconnect?.Invoke(this, this);
        }
    }
}
