using Portly.Core.Interfaces;

namespace Portly.Core.Utilities.Logging
{
    /// <summary>
    /// Provides a base implementation for the log provider.
    /// </summary>
    public abstract class LogProviderBase : ILogProvider
    {
        /// <summary>
        /// Contains all currently active logging levels.
        /// </summary>
        protected readonly HashSet<LogLevel> LogLevels = [];

        /// <inheritdoc/>
        public virtual void Log(string message, LogLevel logLevel = LogLevel.Info)
        {
            if (LogLevels.Contains(logLevel))
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
                _ = Enable(LogLevel.Debug);
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
                LogLevels.Add(level);
        }

        /// <summary>
        /// Enables a log level.
        /// </summary>
        /// <param name="logLevel"></param>
        public ILogProvider Enable(LogLevel logLevel)
        {
            LogLevels.Add(logLevel);
            return this;
        }

        /// <summary>
        /// Disables a log level.
        /// </summary>
        /// <param name="logLevel"></param>
        public ILogProvider Disable(LogLevel logLevel)
        {
            LogLevels.Remove(logLevel);
            return this;
        }
    }
}
