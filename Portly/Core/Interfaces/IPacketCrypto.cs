using Portly.Core.PacketHandling;

namespace Portly.Core.Interfaces
{
    internal interface IPacketCrypto
    {
        Packet Encrypt(Packet packet);
        Packet Decrypt(Packet packet);
    }
}
