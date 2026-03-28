using Portly.Core.Interfaces;
using System.Net;
using System.Net.Sockets;

namespace Portly.Core.Transports
{
    /// <summary>
    /// TCP Transport connection implementation
    /// </summary>
    public class TcpTransportConnection : ITransportConnection
    {
        private readonly TcpClient _client;
        private int _closed; // 0 = open, 1 = closed

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="client"></param>
        public TcpTransportConnection(TcpClient client)
        {
            _client = client;
        }

        /// <inheritdoc/>
        public EndPoint RemoteEndPoint => _client.Client.RemoteEndPoint!;

        /// <inheritdoc/>
        public Stream Stream => _client.GetStream();

        /// <inheritdoc/>
        public bool IsConnected
        {
            get
            {
                if (_closed == 1)
                    return false;

                try
                {
                    var socket = _client.Client;

                    // Detect graceful disconnect
                    if (socket.Poll(0, SelectMode.SelectRead) && socket.Available == 0)
                        return false;

                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <inheritdoc/>
        public Task CloseAsync()
        {
            if (Interlocked.Exchange(ref _closed, 1) == 1)
                return Task.CompletedTask;

            try { _client.Client.Shutdown(SocketShutdown.Both); } catch { }
            _client.Close();

            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            _client.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
