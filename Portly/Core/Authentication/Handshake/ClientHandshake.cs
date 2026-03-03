using MessagePack;

namespace Portly.Core.Authentication.Handshake
{
    [MessagePackObject(AllowPrivate = true)]
    internal sealed class ClientHandshake
    {
        [Key(0)]
        public byte[] Challenge { get; set; } = [];

        [Key(1)]
        public byte[] ClientEphemeralKey { get; set; } = [];

        [Key(2)]
        public byte[] Protocol { get; set; } = [];

        [Key(3)]
        public byte[] ProtocolVersion { get; set; } = [];
    }
}
