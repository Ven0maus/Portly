using Portly.Core.Authentication.Encryption;
using Portly.Core.PacketHandling;
using System.Net.Sockets;

namespace Portly.Core.Server
{
    internal class ClientConnection(TcpClient client, EventHandler<Guid>? onDisconnect) : IServerClient
    {
        public TcpClient Client { get; } = client;
        public NetworkStream Stream { get; } = client.GetStream();
        public CancellationTokenSource Cancellation { get; } = new();

        public DateTime LastReceived { get; set; } = DateTime.UtcNow;
        public DateTime LastSent { get; set; } = DateTime.UtcNow;

        public Guid Id { get; } = Guid.NewGuid();
        internal IPacketCrypto? Crypto { get; set; }

        private int _disconnected = 0;
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
                LastSent = DateTime.UtcNow;
            }
            finally
            {
                _sendLock.Release();
            }
        }

        public void Disconnect()
        {
            if (Interlocked.Exchange(ref _disconnected, 1) == 1)
                return;

            try { Cancellation.Cancel(); } catch { }
            try { Stream.Close(); } catch { }
            try { Client.Close(); } catch { }

            _onDisconnect?.Invoke(this, Id);
        }
    }
}
