using MessagePack;

namespace Portly.Core.Authentication.Handshake
{
    [MessagePackObject]
    public sealed class ServerHandshake
    {
        [Key(0)]
        public byte[] ServerEphemeralKey { get; set; } = [];

        [Key(1)]
        public byte[] Signature { get; set; } = [];
    }
}
