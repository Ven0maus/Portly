using Portly.Core.PacketHandling;
using Portly.Core.Server;

namespace Portly.Server
{
    internal class Program
    {
        private static readonly PortlyServer _server = new PortlyServer(25565);

        private static async Task Main()
        {
            PacketHandler.SetDebugMode(true);
            _server.OnClientConnected += Server_OnClientConnected;

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

        private static async void Server_OnClientConnected(object? sender, Guid e)
        {
            await _server.SendToClientAsync(e, Packet.Create(PacketIdentifier.Create(101), "Hello World", true));
        }
    }
}
