using Portly.Infrastructure.Configuration.Settings;
using Portly.Infrastructure.Logging;
using Portly.Runtime;

namespace Portly.Client
{
    internal class Program
    {
        private static readonly PortlyClient _client = new(logProvider: LogProviderBase.Default);

        private static async Task Main()
        {
            _client.OnConnected += OnConnected;
            await _client.ConnectAsync("localhost", new ConnectionSettings().Port);

            var input = Console.ReadLine();
            while (string.IsNullOrWhiteSpace(input) || !input.Equals("shutdown", StringComparison.OrdinalIgnoreCase))
            {
                input = Console.ReadLine();
            }

            await _client.DisconnectAsync();

            Console.WriteLine("Client disconnected, press any key to exit.");
            Console.ReadKey();
        }

        private static void OnConnected(object? sender, EventArgs e)
        {
            Console.WriteLine("Write shutdown to stop the client.");
        }
    }
}
