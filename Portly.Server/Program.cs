using Portly.Core.Utilities.Logging;

namespace Portly.Server
{
    internal class Program
    {
        private static readonly PortlyServer _server = new(logProvider: LogProviderBase.Default);

        private static async Task Main()
        {
            // Run server in background
            var serverTask = Task.Run(() => _server.StartAsync());

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

        public enum PacketTypes
        {
            HelloWorld = 101
        }
    }
}
