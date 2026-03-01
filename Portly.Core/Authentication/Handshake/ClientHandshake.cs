using MessagePack;

namespace Portly.Core.Authentication.Handshake
{
    [MessagePackObject]
    public sealed class ClientHandshake
    {
        [Key(0)]
        public byte[] Challenge { get; set; } = [];

        [Key(1)]
        public byte[] ClientEphemeralKey { get; set; } = [];
    }
}
