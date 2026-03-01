using Portly.Core.PacketHandling;

namespace Portly.Core.Server
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
        /// Datetime when last a packet was received.
        /// </summary>
        DateTime LastReceived { get; }

        /// <summary>
        /// Datetime when last a packet was send.
        /// </summary>
        DateTime LastSent { get; }

        /// <summary>
        /// Sends a packet asynchronously to the client.
        /// </summary>
        /// <param name="packet"></param>
        /// <returns></returns>
        Task SendPacketAsync(Packet packet);

        /// <summary>
        /// Disconnects the client from the server.
        /// </summary>
        void Disconnect();
    }
}
