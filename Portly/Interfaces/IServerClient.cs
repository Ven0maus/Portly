using Portly.PacketHandling;

namespace Portly.Server
{
    /// <summary>
    /// Represents the connected client's data object for a server.
    /// </summary>
    public interface IServerClient
    {
        /// <summary>
        /// Unique client identifier
        /// </summary>
        Guid Id { get; }

        /// <summary>
        /// Sends a packet asynchronously to the client.
        /// </summary>
        /// <param name="packet"></param>
        /// <returns></returns>
        Task SendPacketAsync(Packet packet);

        /// <summary>
        /// Disconnects the client from the server, and informing them with a disconnect packet.
        /// </summary>
        Task DisconnectAsync();
    }
}
