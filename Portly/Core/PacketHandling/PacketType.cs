namespace Portly.Core.PacketHandling
{
    internal enum PacketType
    {
        KeepAlive,
        LiteHandshake,
        SecureHandshake,
        Disconnect
    }
}
