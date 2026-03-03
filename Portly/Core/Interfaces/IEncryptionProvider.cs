using Portly.Core.PacketHandling;

namespace Portly.Core.Interfaces
{
    /// <summary>
    /// Packet encryption structure
    /// </summary>
    public interface IEncryptionProvider
    {
        /// <summary>
        /// Encrypts a packet.
        /// </summary>
        /// <param name="packet"></param>
        /// <returns></returns>
        Packet Encrypt(Packet packet);
        /// <summary>
        /// Decrypts a packet.
        /// </summary>
        /// <param name="packet"></param>
        /// <returns></returns>
        Packet Decrypt(Packet packet);
    }
}
