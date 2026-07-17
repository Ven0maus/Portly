namespace Portly.Protocol
{
    /// <summary>
    /// Determines how a routed packet should be executed.
    /// </summary>
    public enum PacketExecutionMode
    {
        /// <summary>
        /// Executed as soon as its received.
        /// </summary>
        Immediate,
        /// <summary>
        /// Packet is queued into the TickSystem once received, and executed on the next tick.
        /// </summary>
        Tick
    }
}
