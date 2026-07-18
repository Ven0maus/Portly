using MessagePack;

namespace Portly.Protocol
{
    [MessagePackObject(AllowPrivate = true)]
    internal class ServerTickSyncPacket
    {
        [Key(0)]
        public long Tick { get; set; }

        [Key(1)]
        public long ServerTimestamp { get; set; }

        [Key(2)]
        public double ServerTickRate { get; set; }
    }
}
