using Portly.Infrastructure.Logging;
using Portly.Runtime;

namespace Portly.Server
{
    internal class Program
    {
        private static readonly PortlyServer _server = new(logProvider: LogProviderBase.Default);

        private static async Task Main()
        {
            // Run server in background
            var serverTask = _server.StartAsync();

            Console.WriteLine("Write exit to stop the server.");

            string? input;
            while ((input = Console.ReadLine()) == null ||
                   !input.Equals("exit", StringComparison.OrdinalIgnoreCase))
            {
            }

            await _server.StopAsync();

            // Wait for server to fully exit
            try
            {
                await serverTask;
            }
            catch (OperationCanceledException) { }

            Console.WriteLine("Press any key to exit.");
            Console.ReadKey();
        }
    }
}
