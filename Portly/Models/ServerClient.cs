using Portly.Interfaces;
using Portly.Managers;
using Portly.PacketHandling;
using Portly.Server;
using System.Net.Sockets;

namespace Portly.Models
{
    /// <summary>
    /// Represent a data container for a client that is connected to a server.
    /// </summary>
    /// <param name="client"></param>
    /// <param name="keepAliveManager"></param>
    /// <param name="onDisconnect"></param>
    internal class ServerClient(TcpClient client, KeepAliveManager<ServerClient> keepAliveManager, EventHandler<Guid>? onDisconnect) : IServerClient
    {
        public TcpClient Client { get; } = client;
        public NetworkStream Stream { get; } = client.GetStream();
        public CancellationTokenSource Cancellation { get; } = new();

        public Guid Id { get; } = Guid.NewGuid();
        internal IPacketCrypto? Crypto { get; set; }

        private int _disconnected = 0;
        private readonly KeepAliveManager<ServerClient> _keepAliveManager = keepAliveManager;
        private readonly SemaphoreSlim _sendLock = new(1, 1);

        private readonly EventHandler<Guid>? _onDisconnect = onDisconnect;

        public async Task SendPacketAsync(Packet packet)
        {
            if (!Client.Connected)
                throw new InvalidOperationException("Client not connected.");

            await _sendLock.WaitAsync();
            try
            {
                await PacketHandler.SendPacketAsync(Stream, packet, Crypto);
                _keepAliveManager.UpdateLastSent(this);
            }
            finally
            {
                _sendLock.Release();
            }
        }

        public async Task DisconnectAsync()
        {
            if (Interlocked.Exchange(ref _disconnected, 1) == 1)
                return;

            // Send disconnection packet before cancel
            await SendPacketAsync(Packet.Create(PacketType.Disconnect, Array.Empty<byte>(), false));
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
            try { Client.Close(); } catch { }

            _keepAliveManager.Unregister(this);
            _onDisconnect?.Invoke(this, Id);
        }
    }
}
