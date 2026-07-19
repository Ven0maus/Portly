namespace Portly.Chatbox.Packets
{
    internal enum ChatPacket
    {
        RequestUsername = 101,
        RequestLeave,
        RequestKick,
        RequestChannelChange,
        ChatMessage
    }
}
