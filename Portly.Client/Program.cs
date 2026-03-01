using Portly.Core.Utilities.Logging;

namespace Portly.Client
{
    internal class Program
    {
        private static readonly PortlyClient _client = new(new CompositeLogger(
            new ConsoleLogger(true),
            new FileSystemLogger(enableDebug: true)));

        private static async Task Main()
        {
            _client.OnConnected += OnConnected;
            await _client.ConnectAsync("localhost", 25565);

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
