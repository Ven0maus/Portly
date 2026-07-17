namespace Portly.Protocol
{
    internal sealed class PacketRoute<T>
    {
        public required Func<T, Packet, Task>? Handler { get; init; }
        public PacketExecutionMode ExecutionMode { get; init; }
    }
}
