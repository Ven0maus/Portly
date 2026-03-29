using Portly.Abstractions;
using System.Net;
using System.Net.Sockets;

namespace Portly.Transport
{
    /// <summary>
    /// TCP transport implementation for the server.
    /// </summary>
    /// <remarks>
    /// Constructor
    /// </remarks>
    /// <param name="logProvider"></param>
    public class TcpServerTransport(ILogProvider? logProvider = null) : IServerTransport
    {
        private int _state; // 0 = stopped, 1 = running
        private TcpListener? _listener;
        private readonly ILogProvider? _logProvider = logProvider;

        /// <inheritdoc/>
        public event Func<ITransportConnection, Task>? OnClientAccepted;
        /// <inheritdoc/>
        public event EventHandler? OnServerStarted;
        /// <inheritdoc/>
        public event EventHandler? OnServerStopped;

        /// <inheritdoc/>
        public async Task StartAsync(IPAddress ip, int port, CancellationToken token)
        {
            if (Interlocked.Exchange(ref _state, 1) == 1)
                throw new InvalidOperationException("Server already started.");

            _listener = new TcpListener(ip, port);
            _listener.Start();
            OnServerStarted?.Invoke(this, EventArgs.Empty);

            try
            {
                while (!token.IsCancellationRequested)
                {
                    TcpClient? client;

                    try
                    {
                        client = await _listener.AcceptTcpClientAsync(token);
                    }
                    catch (Exception ex) when (
                        ex is OperationCanceledException ||
                        ex is ObjectDisposedException ||
                        ex is SocketException)
                    {
                        break;
                    }

                    var connection = new TcpTransportConnection(client);

                    if (OnClientAccepted != null)
                    {
                        // Don't block accepting new clients
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                if (OnClientAccepted != null)
                                    await OnClientAccepted.Invoke(connection);
                            }
                            catch (Exception ex)
                            {
                                _logProvider?.Log(ex.Message, Infrastructure.Logging.LogLevel.Error);
                            }
                        }, token);
                    }
                }
            }
            finally
            {
                try
                {
                    _listener?.Stop();
                }
                catch { }

                _listener = null;
                Interlocked.Exchange(ref _state, 0);
                OnServerStopped?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <inheritdoc/>
        public Task StopAsync()
        {
            if (Interlocked.Exchange(ref _state, 0) == 0)
                return Task.CompletedTask;

            try
            {
                _listener?.Stop();
            }
            catch { }

            _listener = null;
            OnServerStopped?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
