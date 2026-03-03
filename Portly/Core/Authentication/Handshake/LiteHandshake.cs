using MessagePack;

namespace Portly.Core.Authentication.Handshake
{
    [MessagePackObject(AllowPrivate = true)]
    internal class LiteHandshake
    {
        [Key(0)]
        public required byte[] Protocol { get; init; }

        [Key(1)]
        public required byte[] ProtocolVersion { get; init; }
    }
}
