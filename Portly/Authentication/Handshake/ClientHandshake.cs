using MessagePack;

namespace Portly.Authentication.Handshake
{
    [MessagePackObject(AllowPrivate = true)]
    internal sealed class ClientHandshake
    {
        [Key(0)]
        public byte[] Challenge { get; set; } = [];

        [Key(1)]
        public byte[] ClientEphemeralKey { get; set; } = [];
    }
}
