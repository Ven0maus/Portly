namespace Portly.Protocol
{
    internal sealed record QueuedPacket<T>(
        T Client,
        Packet Packet,
        PacketRoute<T> Route);
}
