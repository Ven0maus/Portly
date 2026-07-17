namespace Portly.Infrastructure
{
    /// <summary>
    /// The context of the current tick.
    /// </summary>
    public readonly struct TickContext
    {
        /// <summary>
        /// The current tick number.
        /// </summary>
        public long Tick { get; init; }

        /// <summary>
        /// Fixed simulation timestep.
        /// </summary>
        public double FixedDeltaTime { get; init; }

        /// <summary>
        /// Actual time since previous tick.
        /// Useful for diagnostics.
        /// </summary>
        public double ElapsedTime { get; init; }
    }
}
