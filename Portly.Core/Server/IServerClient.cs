using Portly.Core.PacketHandling;

namespace Portly.Core.Server
{
    public interface IServerClient
    {
        Guid Id { get; }
        DateTime LastReceived { get; }
        DateTime LastSent { get; }

        Task SendPacketAsync(Packet packet);
        void Disconnect();
    }
}
