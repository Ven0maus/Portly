using Portly.Core.PacketHandling;

namespace Portly.Server
{
    internal class Program
    {
        private static readonly PortlyServer _server = new(25565);

        private static async Task Main()
        {
            PacketProtocol.SetDebugMode(true);

            // run the server in background so the main thread continues
            await _server.StartAsync();

            Console.WriteLine("Write shutdown to stop the server.");
            var input = Console.ReadLine();
            while (string.IsNullOrWhiteSpace(input) || !input.Equals("shutdown", StringComparison.OrdinalIgnoreCase))
            {
                input = Console.ReadLine();
            }

            await _server.StopAsync();

            Console.WriteLine("Press any key to exit.");
            Console.ReadKey();
        }

        public enum PacketTypes
        {
            HelloWorld = 101
        }
    }
}
