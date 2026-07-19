using MessagePack;

namespace Portly.Chatbox.Objects
{
    [MessagePackObject(AllowPrivate = true)]
    internal class ChatMessage
    {
        [Key(0)]
        public string Message { get; set; }

        [Key(1)]
        public string Username { get; set; }

        public override string ToString()
        {
            return $"[{Username}]: {Message}";
        }
    }
}
