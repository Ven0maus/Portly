using Portly.Infrastructure.Logging;

namespace Portly.Tests.Helpers
{
    internal class TestLogProvider(bool client) : LogProviderBase
    {
        private readonly bool _client = client;

        protected override void Write(string message, LogLevel logLevel)
        {
            TestContext.Out.WriteLine($"[{(_client ? "Client" : "Server")}][{logLevel}]: " + message);
        }
    }
}
