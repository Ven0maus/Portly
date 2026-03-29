using System.Net;

namespace Portly.Abstractions
{
    /// <summary>
    /// Server Transport Provider
    /// </summary>
    public interface IServerTransport : IAsyncDisposable
    {
        /// <summary>
        /// Raised when the server enters a started state.
        /// </summary>
        event EventHandler? OnServerStarted;
        /// <summary>
        /// Raised when the server enters a stopped state.
        /// </summary>
        event EventHandler? OnServerStopped;

        /// <summary>
        /// Starts the server asynchronously.
        /// </summary>
        /// <param name="ip"></param>
        /// <param name="port"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        Task StartAsync(IPAddress ip, int port, CancellationToken token);

        /// <summary>
        /// Stops the server asynchronously.
        /// </summary>
        /// <returns></returns>
        Task StopAsync();

        /// <summary>
        /// Raised when a new client is accepted.
        /// </summary>
        event Func<ITransportConnection, Task>? OnClientAccepted;
    }
}
