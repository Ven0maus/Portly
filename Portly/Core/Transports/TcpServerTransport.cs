using Portly.Core.Interfaces;
using System.Net;
using System.Net.Sockets;

namespace Portly.Core.Transports
{
    /// <summary>
    /// TCP transport implementation for the server.
    /// </summary>
    public class TcpServerTransport : IServerTransport
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();

        /// <inheritdoc/>
        public event Func<ITransportConnection, Task>? OnClientAccepted;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="ip"></param>
        /// <param name="port"></param>
        public TcpServerTransport(IPAddress ip, int port)
        {
            _listener = new TcpListener(ip, port);
        }

        /// <inheritdoc/>
        public async Task StartAsync(CancellationToken token)
        {
            _listener.Start();

            while (!token.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(token);

                var connection = new TcpTransportConnection(client);

                if (OnClientAccepted != null)
                    await OnClientAccepted.Invoke(connection);
            }
        }

        /// <inheritdoc/>
        public Task StopAsync()
        {
            _cts.Cancel();
            _listener.Stop();
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
