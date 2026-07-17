using Portly.Infrastructure.Logging;
using System.Collections.Concurrent;

namespace Portly.Tests.Helpers
{
    internal class TestLogProvider(bool client) : LogProviderBase
    {
        private readonly ConcurrentBag<string> _logs = [];
        /// <summary>
        /// Contains all the logs.
        /// </summary>
        public IReadOnlyCollection<string> Logs => _logs;

        /// <summary>
        /// Raised when a log message is logged.
        /// </summary>
        public event EventHandler<string>? OnLog;

        private readonly bool _client = client;

        protected override void Write(string message, LogLevel logLevel)
        {
            _logs.Add(message);
            TestContext.Out.WriteLine($"[{(_client ? "Client" : "Server")}][{logLevel}]: " + message);
            OnLog?.Invoke(this, message);
        }

        /// <summary>
        /// Clears the entire history of this logger.
        /// </summary>
        public void ClearHistory() => _logs.Clear();
    }
}
