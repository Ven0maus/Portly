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
        /// Method to provide and store an encryption provider.
        /// </summary>
        /// <param name="encryptionProvider"></param>
        void SetEncryptionProvider(IEncryptionProvider encryptionProvider);

        /// <summary>
        /// Packet sending implementation
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="packet"></param>
        /// <param name="encrypt"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        Task SendPacketAsync(NetworkStream stream, IPacket packet, bool encrypt, CancellationToken cancellationToken = default);

        /// <summary>
        /// Single packet receive implementation
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        Task<IPacket> ReceiveSinglePacketAsync(NetworkStream stream, CancellationToken cancellationToken = default);

        /// <summary>
        /// Continuous packet receive implementation
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="onPacket"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        Task ReadPacketsAsync(NetworkStream stream, Func<IPacket, Task> onPacket, CancellationToken cancellationToken = default);
    }
}
