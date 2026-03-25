using Portly.Core.Interfaces;
using Portly.PacketHandling;
using System.Collections.Concurrent;

namespace Portly.Core.PacketHandling
{
    /// <summary>
    /// A router to route packets based on their identifier to their correct packet handlers.
    /// </summary>
    public sealed class PacketRouter<T>
    {
        private readonly ConcurrentDictionary<int, Func<T, IPacket, Task>?> _handlers =
            new();

        /// <summary>
        /// The base handler for the packet.
        /// </summary>
        /// <param name="client"></param>
        /// <param name="packet"></param>
        /// <returns></returns>
        public delegate Task PacketHandlerBase(T client, IPacket packet);

        /// <summary>
        /// Register a handler for a packet type, identifiers with a null handler are ignored.
        /// </summary>
        /// <param name="identifier"></param>
        /// <param name="handler"></param>
        public void Register(PacketIdentifier identifier, PacketHandlerBase? handler)
        {
            if (handler == null)
            {
                _handlers[identifier.Id] = null;
                return;
            }

            _handlers[identifier.Id] = async (client, packet) =>
            {
                await handler(client, packet);
            };
        }

        /// <summary>
        /// Register a handler for a packet type, identifiers with a null handler are ignored.
        /// </summary>
        /// <param name="identifier"></param>
        /// <param name="handler"></param>
        public void Register(Enum identifier, PacketHandlerBase? handler)
            => Register((PacketIdentifier)identifier, handler);

        internal Task? RouteAsync(T client, IPacket packet)
        {
            if (_handlers.TryGetValue(packet.Identifier.Id, out var handler))
                return handler == null ? null : handler(client, packet);
            else
                Console.WriteLine($"No handler registered for packet {packet.Identifier}");

            return null;
        }
    }
}
