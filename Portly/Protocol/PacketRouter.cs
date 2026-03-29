using Portly.Abstractions;
using Portly.PacketHandling;
using System.Collections.Concurrent;

namespace Portly.Protocol
{
    /// <summary>
    /// A router to route packets based on their identifier to their correct packet handlers.
    /// </summary>
    /// <remarks>
    /// Constructor
    /// </remarks>
    /// <param name="logProvider"></param>
    public sealed class PacketRouter<T>(ILogProvider? logProvider = null)
    {
        private readonly ILogProvider? _logProvider = logProvider;
        private readonly ConcurrentDictionary<int, Func<T, Packet, Task>?> _handlers =
            new();

        /// <summary>
        /// The base handler for the packet.
        /// </summary>
        /// <param name="client"></param>
        /// <param name="packet"></param>
        /// <returns></returns>
        public delegate Task PacketHandlerBase(T client, Packet packet);

        /// <summary>
        /// Register a handler for a packet type, identifiers with a null handler are ignored.
        /// </summary>
        /// <param name="identifier"></param>
        /// <param name="handler"></param>
        public void Register(PacketIdentifier identifier, PacketHandlerBase? handler)
        {
            if (handler == null)
            {
                _handlers.TryRemove(identifier.Id, out _);
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
        /// <typeparam name="TPayload"></typeparam>
        /// <param name="identifier"></param>
        /// <param name="handler"></param>
        /// <exception cref="InvalidOperationException"></exception>
        public void Register<TPayload>(PacketIdentifier identifier, Func<T, TPayload, Task>? handler)
        {
            if (handler == null)
            {
                _handlers[identifier.Id] = null;
                return;
            }

            _handlers[identifier.Id] = async (client, packet) =>
            {
                TPayload payload;

                try
                {
                    payload = packet.As<TPayload>().Payload;
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"Failed to deserialize packet \"{packet.Identifier}\" to type \"{typeof(TPayload).Name}\" during routing.", ex);
                }

                await handler(client, payload);
            };
        }

        /// <summary>
        /// Register a handler for a packet type, identifiers with a null handler are ignored.
        /// </summary>
        /// <param name="identifier"></param>
        /// <param name="handler"></param>
        public void Register(Enum identifier, PacketHandlerBase? handler)
            => Register((PacketIdentifier)identifier, handler);

        /// <summary>
        /// Register a handler for a packet type, identifiers with a null handler are ignored.
        /// </summary>
        /// <param name="identifier"></param>
        /// <param name="handler"></param>
        public void Register<TPayload>(Enum identifier, Func<T, TPayload, Task>? handler)
            => Register((PacketIdentifier)identifier, handler);

        internal Task? RouteAsync(T client, Packet packet)
        {
            try
            {
                if (_handlers.TryGetValue(packet.Identifier.Id, out var handler))
                    return handler == null ? null : handler(client, packet);
                else
                    _logProvider?.Log($"No handler registered for packet {packet.Identifier}", Infrastructure.Logging.LogLevel.Warning);
            }
            catch (Exception e)
            {
                _logProvider?.Log(e.Message, Infrastructure.Logging.LogLevel.Error);
            }

            return null;
        }
    }
}
