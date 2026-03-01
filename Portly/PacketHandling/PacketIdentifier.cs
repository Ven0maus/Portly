using MessagePack;

namespace Portly.PacketHandling
{
    /// <summary>
    /// Identifier to identify the packet by Id. (Range 0-100 is reserved)
    /// </summary>
    [MessagePackObject(AllowPrivate = true)]
    public readonly partial struct PacketIdentifier
    {
        /// <summary>
        /// The packet identifiers unique ID. (Range 0-100 is reserved)
        /// </summary>
        [Key(0)]
        public int Id { get; }

        [SerializationConstructor]
        private PacketIdentifier(int id)
        {
            Id = id; // no validation
        }

        /// <summary>
        /// Creates a new packet identifier from an integer value.
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public static PacketIdentifier Create(int id)
        {
            return new PacketIdentifier(ValidateId(id));
        }

        /// <summary>
        /// Creates a new packet identifier from an enum value.
        /// </summary>
        /// <param name="enumValue"></param>
        /// <returns></returns>
        public static PacketIdentifier Create(Enum enumValue)
        {
            return new PacketIdentifier(enumValue is PacketType pt ? (int)pt : ValidateId(Convert.ToInt32(enumValue)));
        }

        /// <summary>
        /// Explicit converter to convert an enum to a <see cref="PacketIdentifier"/>
        /// </summary>
        /// <param name="e"></param>
        public static explicit operator PacketIdentifier(Enum e)
            => Create(e);

        private static int ValidateId(int id)
        {
            if (id < 0)
                throw new ArgumentOutOfRangeException(nameof(id), "Negative ids are not accepted as valid packet identifiers.");
            if (id >= 0 && id <= 100)
                throw new ArgumentOutOfRangeException(nameof(id), "IDs 0–100 are reserved for system use.");
            return id;
        }

        /// <inheritdoc/>
        public override string ToString() => $"PacketType({Id})";

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is PacketIdentifier other && other.Id == Id;

        /// <inheritdoc/>
        public override int GetHashCode() => Id.GetHashCode();

        /// <inheritdoc/>
        public static bool operator ==(PacketIdentifier a, PacketIdentifier b) => a.Id == b.Id;

        /// <inheritdoc/>
        public static bool operator !=(PacketIdentifier a, PacketIdentifier b) => a.Id != b.Id;
    }
}
