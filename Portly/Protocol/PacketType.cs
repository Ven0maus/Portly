namespace Portly.Protocol
{
    internal enum PacketType
    {
        KeepAlive,
        LiteHandshake,
        SecureHandshake,
        Disconnect
    }
}
