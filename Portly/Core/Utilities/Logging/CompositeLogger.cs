using Portly.Core.Interfaces;

namespace Portly.Core.Utilities.Logging
{
    /// <summary>
    /// A logger that forwards log messages to multiple underlying log providers.
    /// </summary>
    /// <remarks>
    /// This implementation allows combining multiple logging outputs, such as writing
    /// to both the console and the file system simultaneously.
    /// </remarks>
    /// <remarks>
    /// Initializes a new instance of the <see cref="CompositeLogger"/> class
    /// with the specified log providers.
    /// </remarks>
    /// <param name="providers">
    /// The log providers to which all log messages will be forwarded.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="providers"/> is <c>null</c>.
    /// </exception>
    public class CompositeLogger(params LogProviderBase[] providers) : ILogProvider
    {
        private readonly ILogProvider[] _providers = providers ?? throw new ArgumentNullException(nameof(providers));

        /// <summary>
        /// Writes a log message to all configured log providers.
        /// </summary>
        /// <param name="message">The message to log.</param>
        /// <param name="logLevel">
        /// The severity level of the log message. Defaults to <see cref="LogLevel.Info"/>.
        /// </param>
        public void Log(string message, LogLevel logLevel = LogLevel.Info)
        {
            foreach (var p in _providers)
                p.Log(message, logLevel);
        }
    }
}
