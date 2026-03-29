using System.Net;

namespace Portly.Abstractions
{
    /// <summary>
    /// Client Transport Provider
    /// </summary>
    public interface IClientTransport : IAsyncDisposable
    {
        /// <summary>
        /// The remote endpoint of the client.
        /// </summary>
        EndPoint RemoteEndPoint { get; }

        /// <summary>
        /// The stream of the client.
        /// </summary>
        Stream Stream { get; }

        /// <summary>
        /// Connects asynchronously to the server.
        /// </summary>
        /// <param name="host"></param>
        /// <param name="port"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        Task ConnectAsync(string host, int port, CancellationToken token);

        /// <summary>
        /// Disconnects asynchronously from the server.
        /// </summary>
        /// <returns></returns>
        Task DisconnectAsync();
    }
}
