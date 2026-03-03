using Portly.Core.Interfaces;
using System.Net.Sockets;

namespace Portly.Core.PacketHandling
{
    /// <summary>
    /// Basic packet protocol structure
    /// </summary>
    public interface IPacketProtocol
    {
        /// <summary>
        /// Protocol version
        /// </summary>
        Version Version { get; }

        /// <summary>
        /// Packet sending implementation
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="packet"></param>
        /// <param name="cancellationToken"></param>
        /// <param name="encryptionProvider"></param>
        /// <returns></returns>
        Task SendPacketAsync(NetworkStream stream, Packet packet, CancellationToken cancellationToken, IEncryptionProvider? encryptionProvider = null);

        /// <summary>
        /// Single packet receive implementation
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="cancellationToken"></param>
        /// <param name="encryptionProvider"></param>
        /// <returns></returns>
        Task<Packet> ReceiveSinglePacketAsync(NetworkStream stream, CancellationToken cancellationToken, IEncryptionProvider? encryptionProvider = null);

        /// <summary>
        /// Continuous packet receive implementation
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="onPacket"></param>
        /// <param name="cancellationToken"></param>
        /// <param name="encryptionProvider"></param>
        /// <returns></returns>
        Task ReadPacketsAsync(NetworkStream stream, Func<Packet, Task> onPacket, CancellationToken cancellationToken, IEncryptionProvider? encryptionProvider = null);
    }
}
