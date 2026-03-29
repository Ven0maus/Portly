using Portly.Abstractions;

namespace Portly.Infrastructure.Logging
{
    /// <summary>
    /// Provides a base implementation for the log provider.
    /// </summary>
    public abstract class LogProviderBase : ILogProvider
    {
        private readonly HashSet<LogLevel> _logLevels = [];

        /// <inheritdoc />
        public IReadOnlySet<LogLevel> TrackedLogLevels => _logLevels;

        /// <inheritdoc/>
        public virtual void Log(string message, LogLevel logLevel = LogLevel.Info)
        {
            if (_logLevels.Contains(logLevel))
            {
                Write(message, logLevel);
            }
        }

        /// <summary>
        /// The main method that writes the information away.
        /// </summary>
        /// <param name="message"></param>
        /// <param name="logLevel"></param>
        protected abstract void Write(string message, LogLevel logLevel);

        /// <summary>
        /// Constructor with the default log levels enabled. (info, error, warning)
        /// </summary>
        public LogProviderBase(bool enableDebug = false) : this([])
        {
            if (enableDebug)
                Enable(LogLevel.Debug);
        }

        /// <summary>
        /// Constructor with definition of which log levels are enabled.
        /// </summary>
        /// <param name="defaultLogLevels">If none are provided, all except debug will be enabled.</param>
        public LogProviderBase(params LogLevel[] defaultLogLevels)
        {
            defaultLogLevels = defaultLogLevels == null || defaultLogLevels.Length == 0 ?
                [LogLevel.Info, LogLevel.Error, LogLevel.Warning] : defaultLogLevels;

            foreach (var level in defaultLogLevels)
                _logLevels.Add(level);
        }

        /// <inheritdoc/>
        public void Enable(params LogLevel[] logLevels)
        {
            foreach (var level in logLevels)
                _logLevels.Add(level);
        }

        /// <inheritdoc/>
        public void Disable(params LogLevel[] logLevels)
        {
            foreach (var level in logLevels)
                _logLevels.Remove(level);
        }

        /// <summary>
        /// Provides a default composite logger that logs to the console and to the disk.
        /// </summary>
        public static ILogProvider? Default => new CompositeLogger(
            new ConsoleLogger(), new FileSystemLogger());
    }
}
