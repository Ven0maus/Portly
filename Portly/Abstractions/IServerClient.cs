using System.Net;

namespace Portly.Abstractions
{
    /// <summary>
    /// Represents the server-side client data object.
    /// </summary>
    public interface IServerClient
    {
        /// <summary>
        /// Unique client identifier
        /// </summary>
        Guid Id { get; }

        /// <summary>
        /// The IPAddress of the client
        /// </summary>
        IPAddress IpAddress { get; }

        /// <summary>
        /// Disconnects the client from the server, and informing them with a disconnect packet.
        /// </summary>
        Task DisconnectAsync(string reason = "", bool informClient = true);
    }
}
