namespace Portly.Abstractions
{
    /// <summary>
    /// Server Transport Provider
    /// </summary>
    public interface IServerTransport : IAsyncDisposable
    {
        /// <summary>
        /// Starts the server asynchronously.
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
        Task StartAsync(CancellationToken token);

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
