using Portly.Infrastructure.Logging;

namespace Portly.Tests.Helpers
{
    internal class TestLogProvider : LogProviderBase
    {
        internal static TestLogProvider Instance { get; } = new TestLogProvider();

        private TestLogProvider() { }

        protected override void Write(string message, LogLevel logLevel)
        {
            TestContext.Out.WriteLine($"[{logLevel}]: " + message);
        }
    }
}
