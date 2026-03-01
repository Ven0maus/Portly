using Portly.PacketHandling;

namespace Portly.Interfaces
{
    internal interface IPacketCrypto
    {
        Packet Encrypt(Packet packet);
        Packet Decrypt(Packet packet);
    }
}
