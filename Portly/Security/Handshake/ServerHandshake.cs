using MessagePack;

namespace Portly.Security.Handshake
{
    [MessagePackObject(AllowPrivate = true)]
    internal sealed class ServerHandshake
    {
        [Key(0)]
        public byte[] ServerEphemeralKey { get; set; } = [];

        [Key(1)]
        public byte[] Signature { get; set; } = [];

        [Key(2)]
        public Guid ClientId { get; set; }

        [Key(3)]
        public int TickRate { get; set; }

        [Key(4)]
        public long CurrentTick { get; set; }

        [Key(5)]
        public long ServerTimestamp { get; set; }
    }
}
