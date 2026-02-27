namespace Portly.Core.PacketHandling
{
    public class Packet
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public PacketType Type { get; set; }
        public required byte[] Payload { get; set; }
    }
}
