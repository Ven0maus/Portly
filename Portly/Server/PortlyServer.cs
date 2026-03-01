using Portly.Core.Authentication.Handshake;
using Portly.Core.Interfaces;
using Portly.Core.PacketHandling;
using System.Net.Sockets;

namespace Portly.Server
{
    /// <summary>
    /// Represents a TCP-based server responsible for accepting client connections,
    /// performing a Trust-On-First-Use (TOFU) handshake, and processing incoming packets.
    ///
    /// The server listens for incoming <see cref="TcpClient"/> connections and handles each client
    /// asynchronously. Upon connection, a handshake is performed using <see cref="TrustServer"/> to
    /// establish the server's identity by sending its public key and signing a client-provided challenge.
    ///
    /// After a successful handshake, the server continuously reads and processes packets using
    /// <see cref="PacketProtocol"/>, enabling efficient, length-prefixed communication over the network.
    ///
    /// This implementation does not include encryption, but establishes the foundation for secure
    /// communication by verifying server identity during the initial connection phase.
    /// </summary>
    public class PortlyServer : PortlyServerBase
    {
        /// <inheritdoc/>
        public PortlyServer(int port, ILogProvider? logProvider = null) : base(port, logProvider)
        {
            Router.Register(PacketType.KeepAlive, null);
            Router.Register(PacketType.Disconnect, HandleDisconnectPacket);
        }

        private async Task HandleDisconnectPacket(IServerClient client, Packet packet)
        {
            await ((ServerClient)client).DisconnectInternalAsync();
        }
    }
}
