using Portly.Core.Authentication.Handshake;
using Portly.Core.Interfaces;

namespace Portly.Client
{
    /// <summary>
    /// Represents a client responsible for connecting to a server,
    /// performing a Trust-On-First-Use (TOFU) handshake, and sending/receiving packets.
    ///
    /// The client establishes a connection to a remote server, verifies its identity
    /// using <see cref="TrustClient"/> by validating the server's public key fingerprint,
    /// and performs a challenge-response to ensure authenticity.
    ///
    /// After a successful handshake, it continuously listens for incoming packets
    /// using <see cref="IPacketProtocol"/> and allows sending packets over the connection.
    ///
    /// This implementation ensures server identity verification as a foundation for secure communication.
    /// </summary>
    public class PortlyClient : PortlyClientBase
    {
        /// <inheritdoc/>
        public PortlyClient(Func<IClientTransport>? clientTransport = null,
            IPacketProtocol? packetProtocol = null,
            IPacketSerializationProvider? packetSerializationProvider = null,
            Func<byte[], IEncryptionProvider>? encryptionProvider = null,
            ILogProvider? logProvider = null) :
            base(clientTransport, packetProtocol, packetSerializationProvider, encryptionProvider, logProvider)
        { }
    }
}
