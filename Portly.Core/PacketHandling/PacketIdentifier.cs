using MessagePack;

namespace Portly.Core.PacketHandling
{
    [MessagePackObject(AllowPrivate = true, SuppressSourceGeneration = true)]
    public readonly struct PacketIdentifier
    {
        [Key(0)]
        public int Id { get; }

        [SerializationConstructor]
        private PacketIdentifier(int id) => Id = id;

        internal PacketIdentifier(PacketType packetType) => Id = (int)packetType;

        /// <summary>
        /// Creates a new packet identifier. (Range 0-100 is reserved)
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public static PacketIdentifier Create(int id)
        {
            if (id >= 0 && id <= 100)
                throw new ArgumentOutOfRangeException(nameof(id), "IDs 0–100 are reserved for system use.");
            return new PacketIdentifier(id);
        }

        public override string ToString() => $"PacketType({Id})";

        public override bool Equals(object? obj) => obj is PacketIdentifier other && other.Id == Id;
        public override int GetHashCode() => Id.GetHashCode();

        public static bool operator ==(PacketIdentifier a, PacketIdentifier b) => a.Id == b.Id;
        public static bool operator !=(PacketIdentifier a, PacketIdentifier b) => a.Id != b.Id;
    }
}
