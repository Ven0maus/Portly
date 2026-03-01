using MessagePack;

namespace Portly.Core.PacketHandling
{
    /// <summary>
    /// Identifier to identify the packet by Id. (Range 0-100 is reserved)
    /// </summary>
    [MessagePackObject]
    public readonly struct PacketIdentifier
    {
        [Key(0)]
        public int Id { get; }

        [SerializationConstructor]
        public PacketIdentifier(int id)
        {
            Id = ValidateId(id);
        }

        public PacketIdentifier(Enum enumValue)
        {
            Id = enumValue is PacketType pt ? (int)pt : ValidateId(Convert.ToInt32(enumValue));
        }

        public static explicit operator PacketIdentifier(Enum e)
            => new(e);

        private static int ValidateId(int id)
        {
            if (id < 0)
                throw new ArgumentOutOfRangeException(nameof(id), "Negative ids are not accepted as valid packet identifiers.");
            if (id >= 0 && id <= 100)
                throw new ArgumentOutOfRangeException(nameof(id), "IDs 0–100 are reserved for system use.");
            return id;
        }

        public override string ToString() => $"PacketType({Id})";

        public override bool Equals(object? obj) => obj is PacketIdentifier other && other.Id == Id;
        public override int GetHashCode() => Id.GetHashCode();

        public static bool operator ==(PacketIdentifier a, PacketIdentifier b) => a.Id == b.Id;
        public static bool operator !=(PacketIdentifier a, PacketIdentifier b) => a.Id != b.Id;
    }
}
