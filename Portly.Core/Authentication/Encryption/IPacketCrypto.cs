using Portly.Core.PacketHandling;

namespace Portly.Core.Authentication.Encryption
{
    internal interface IPacketCrypto
    {
        Packet Encrypt(Packet packet);
        Packet Decrypt(Packet packet);
    }
}
