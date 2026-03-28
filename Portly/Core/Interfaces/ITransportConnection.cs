using System.Net;

namespace Portly.Core.Interfaces
{
    /// <summary>
    /// Provider for the accepted connection of the transport.
    /// </summary>
    public interface ITransportConnection : IAsyncDisposable
    {
        /// <summary>
        /// The remote endpoint of the transport connection.
        /// </summary>
        EndPoint RemoteEndPoint { get; }

        /// <summary>
        /// The stream of the transport connection.
        /// </summary>
        Stream Stream { get; }

        /// <summary>
        /// Defines if the connection is still connected.
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// Asynchronously closes the transport connection.
        /// </summary>
        /// <returns></returns>
        Task CloseAsync();
    }
}
