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

            Console.WriteLine("Write shutdown to stop the server.");

            string? input;
            while ((input = Console.ReadLine()) == null ||
                   !input.Equals("shutdown", StringComparison.OrdinalIgnoreCase))
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

        private static void Write(string message, ConsoleColor? color = null)
        {
            color ??= Console.ForegroundColor;
            var prev = Console.ForegroundColor;
            Console.ForegroundColor = color.Value;
            Console.Write(message);
            Console.ForegroundColor = prev;
        }

        private static void WriteLine(string message, ConsoleColor? color = null)
        {
            color ??= Console.ForegroundColor;
            var prev = Console.ForegroundColor;
            Console.ForegroundColor = color.Value;
            Console.WriteLine(message);
            Console.ForegroundColor = prev;
        }
    }
}
