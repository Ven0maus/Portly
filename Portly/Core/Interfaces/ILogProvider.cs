using Portly.Core.Utilities.Logging;

namespace Portly.Core.Interfaces
{
    /// <summary>
    /// Represents a logging implementation for the server / client.
    /// </summary>
    public interface ILogProvider
    {
        /// <summary>
        /// Logs a message with the specified log level.
        /// </summary>
        /// <param name="message"></param>
        /// <param name="logLevel"></param>
        void Log(string message, LogLevel logLevel = LogLevel.Info);
    }
}
