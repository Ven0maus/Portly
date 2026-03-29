using Portly.Core.Authentication.Handshake;
using Portly.Core.Interfaces;

namespace Portly.Server
{
    /// <summary>
    /// Represents a server responsible for accepting client connections,
    /// performing a Trust-On-First-Use (TOFU) handshake, and processing incoming packets.
    ///
    /// The server listens for incoming <see cref="ITransportConnection"/> connections and handles each client
    /// asynchronously. Upon connection, a handshake is performed using <see cref="TrustServer"/> to
    /// establish the server's identity by sending its public key and signing a client-provided challenge.
    ///
    /// After a successful handshake, the server continuously reads and processes packets using
    /// <see cref="IPacketProtocol"/>, enabling efficient, length-prefixed communication over the network.
    ///
    /// This implementation establishes the foundation for secure
    /// communication by verifying server identity during the initial connection phase.
    /// </summary>
    public class PortlyServer : PortlyServerBase
    {
        /// <inheritdoc/>
        public PortlyServer(
            IServerTransport? serverTransport = null,
            Func<IPacketProtocol>? packetProtocol = null,
            IPacketSerializationProvider? packetSerializationProvider = null,
            Func<byte[], IEncryptionProvider>? encryptionProvider = null,
            ILogProvider? logProvider = null) :
            base(serverTransport, packetProtocol, packetSerializationProvider, encryptionProvider, logProvider: logProvider)
        { }
    }
}
