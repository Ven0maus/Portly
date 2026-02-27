using Portly.Core.Server;

namespace Portly.Server
{
    internal class Program
    {
        private static async Task Main()
        {
            var server = new PortlyServer(25565);

            // run the server in background so the main thread continues
            _ = Task.Run(() => server.StartAsync());

            Console.WriteLine("Write shutdown to stop the server.");
            var input = Console.ReadLine();
            while (string.IsNullOrWhiteSpace(input) || !input.Equals("shutdown", StringComparison.OrdinalIgnoreCase))
            {
                input = Console.ReadLine();
            }

            await server.StopAsync();

            Console.WriteLine("Press any key to exit.");
            Console.ReadKey();
        }
    }
}
