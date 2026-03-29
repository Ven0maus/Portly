using MessagePack;

namespace Portly.Protocol
{
    /// <summary>
    /// Internal packet structure used for non-exposed transport metadata per packet.
    /// </summary>
    [MessagePackObject(AllowPrivate = true)]
    internal sealed class TransportPacket
    {
        [Key(0)]
        public required byte[] Payload { get; init; }

        [Key(1)]
        public string? Nonce { get; init; }

        [Key(2)]
        public DateTime? CreationTimestampUtc { get; init; }
    }
}
