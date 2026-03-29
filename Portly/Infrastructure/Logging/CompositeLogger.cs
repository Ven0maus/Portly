using Portly.Abstractions;

namespace Portly.Infrastructure.Logging
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
    public class CompositeLogger : ILogProvider
    {
        /// <summary>
        /// All log providers contained within this composite.
        /// </summary>
        public readonly List<ILogProvider> Providers = [];

        /// <inheritdoc />
        public IReadOnlySet<LogLevel> TrackedLogLevels => Providers
            .SelectMany(a => a.TrackedLogLevels)
            .ToHashSet();

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="providers"></param>
        public CompositeLogger(params LogProviderBase[] providers)
        {
            Providers.AddRange(providers);
        }

        /// <inheritdoc />
        public void Disable(params LogLevel[] logLevels)
        {
            foreach (var provider in Providers)
                provider.Disable(logLevels);
        }

        /// <inheritdoc />
        public void Enable(params LogLevel[] logLevels)
        {
            foreach (var provider in Providers)
                provider.Enable(logLevels);
        }

        /// <summary>
        /// Writes a log message to all configured log providers.
        /// </summary>
        /// <param name="message">The message to log.</param>
        /// <param name="logLevel">
        /// The severity level of the log message. Defaults to <see cref="LogLevel.Info"/>.
        /// </param>
        public void Log(string message, LogLevel logLevel = LogLevel.Info)
        {
            foreach (var p in Providers)
                p.Log(message, logLevel);
        }
    }
}
