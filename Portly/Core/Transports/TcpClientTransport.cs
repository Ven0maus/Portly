using Portly.Core.Interfaces;
using System.Net;
using System.Net.Sockets;

namespace Portly.Core.Transports
{
    /// <summary>
    /// TCP transport implementation for the client.
    /// </summary>
    public sealed class TcpClientTransport : IClientTransport
    {
        private TcpClient? _client;

        /// <inheritdoc/>
        public EndPoint RemoteEndPoint => _client?.Client.RemoteEndPoint!;

        /// <inheritdoc/>
        public Stream Stream => _client?.GetStream()
            ?? throw new InvalidOperationException("Not connected.");

        /// <inheritdoc/>
        public async Task ConnectAsync(string host, int port, CancellationToken token)
        {
            _client = new TcpClient();
            await _client.ConnectAsync(host, port, token);
        }

        /// <inheritdoc/>
        public Task DisconnectAsync()
        {
            _client?.Close();
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            _client?.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
