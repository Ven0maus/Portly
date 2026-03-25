using System.Net;

namespace Portly.Core.Interfaces
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
        /// Sends a packet asynchronously to the client.
        /// </summary>
        /// <param name="packet"></param>
        /// <param name="encrypt"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        Task SendPacketAsync(IPacket packet, bool encrypt, CancellationToken cancellationToken);

        /// <summary>
        /// Disconnects the client from the server, and informing them with a disconnect packet.
        /// </summary>
        Task DisconnectAsync(string reason = "");
    }
}
