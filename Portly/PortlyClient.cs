using Portly.Authentication.Handshake;
using Portly.Interfaces;
using Portly.PacketHandling;

namespace Portly
{
    /// <summary>
    /// Represents a TCP-based client responsible for connecting to a server,
    /// performing a Trust-On-First-Use (TOFU) handshake, and sending/receiving packets.
    ///
    /// The client establishes a connection to a remote server, verifies its identity
    /// using <see cref="TrustClient"/> by validating the server's public key fingerprint,
    /// and performs a challenge-response to ensure authenticity.
    ///
    /// After a successful handshake, it continuously listens for incoming packets
    /// using <see cref="PacketProtocol"/> and allows sending packets over the connection.
    ///
    /// This implementation does not include encryption but ensures server identity
    /// verification as a foundation for secure communication.
    /// </summary>
    public class PortlyClient : PortlyClientBase
    {
        /// <inheritdoc/>
        public PortlyClient()
        {
            Router.Register(PacketType.KeepAlive, null);
            Router.Register(PacketType.Disconnect, HandleDisconnectPacket);
        }

        private async Task HandleDisconnectPacket(IClient client, Packet packet)
        {
            string reason = string.Empty;
            if (packet.Payload.Length != 0)
                reason = packet.As<string>().Payload;

            await ((PortlyClient)client).DisconnectInternalAsync(false, reason);
        }
    }
}
