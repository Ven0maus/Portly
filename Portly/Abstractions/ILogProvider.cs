using Portly.Infrastructure.Logging;

namespace Portly.Abstractions
{
    /// <summary>
    /// Represents a logging implementation for the server / client.
    /// </summary>
    public interface ILogProvider
    {
        /// <summary>
        /// The log levels which are currently being logged to.
        /// </summary>
        IReadOnlySet<LogLevel> TrackedLogLevels { get; }

        /// <summary>
        /// Enable all the specified log levels.
        /// </summary>
        /// <param name="logLevels"></param>
        void Enable(params LogLevel[] logLevels);

        /// <summary>
        /// Disable all the specified log levels.
        /// </summary>
        /// <param name="logLevels"></param>
        void Disable(params LogLevel[] logLevels);

        /// <summary>
        /// Logs a message with the specified log level.
        /// </summary>
        /// <param name="message"></param>
        /// <param name="logLevel"></param>
        void Log(string message, LogLevel logLevel = LogLevel.Info);
    }
}
