using Portly.PacketHandling;
using System.Collections.Concurrent;

namespace Portly.Core.PacketHandling
{
    /// <summary>
    /// A router to route packets based on their identifier to their correct packet handlers.
    /// </summary>
    public sealed class PacketRouter<T>
    {
        private readonly ConcurrentDictionary<int, Func<T, Packet, Task>?> _handlers =
            new();

        /// <summary>
        /// The handler for the packet.
        /// </summary>
        /// <typeparam name="TPayload"></typeparam>
        /// <param name="client"></param>
        /// <param name="packet"></param>
        /// <returns></returns>
        public delegate Task PacketHandler<TPayload>(T client, Packet<TPayload> packet);

        /// <summary>
        /// The base handler for the packet.
        /// </summary>
        /// <param name="client"></param>
        /// <param name="packet"></param>
        /// <returns></returns>
        public delegate Task PacketHandlerBase(T client, Packet packet);

        /// <summary>
        /// Register a typed handler for a packet type, identifiers with a null handler are ignored.
        /// </summary>
        public void Register<TPayload>(PacketIdentifier identifier, PacketHandler<TPayload>? handler)
        {
            if (handler == null)
            {
                _handlers[identifier.Id] = null;
                return;
            }

            _handlers[identifier.Id] = async (client, packet) =>
            {
                var typedPacket = packet.As<TPayload>();
                await handler(client, typedPacket);
            };
        }

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
        /// Register a typed handler for a packet type, identifiers with a null handler are ignored.
        /// </summary>
        /// <typeparam name="TPayload"></typeparam>
        /// <param name="identifier"></param>
        /// <param name="handler"></param>
        public void Register<TPayload>(Enum identifier, PacketHandler<TPayload>? handler)
            => Register((PacketIdentifier)identifier, handler);

        /// <summary>
        /// Register a handler for a packet type, identifiers with a null handler are ignored.
        /// </summary>
        /// <param name="identifier"></param>
        /// <param name="handler"></param>
        public void Register(Enum identifier, PacketHandlerBase? handler)
            => Register((PacketIdentifier)identifier, handler);

        internal Task? RouteAsync(T client, Packet packet)
        {
            if (_handlers.TryGetValue(packet.Identifier.Id, out var handler))
                return handler == null ? null : handler(client, packet);
            else
                Console.WriteLine($"No handler registered for packet {packet.Identifier}");

            return null;
        }
    }
}
