using Portly.Core.Client;
using Portly.Core.PacketHandling;

namespace Portly.Client
{
    internal class Program
    {
        private static readonly PortlyClient _client = new();

        private static async Task Main()
        {
            PacketHandler.SetDebugMode(true);
            await _client.ConnectAsync("localhost", 25565, OnReceivePacket);

            Console.WriteLine("Write shutdown to stop the client.");
            var input = Console.ReadLine();
            while (string.IsNullOrWhiteSpace(input) || !input.Equals("shutdown", StringComparison.OrdinalIgnoreCase))
            {
                input = Console.ReadLine();
            }

            await _client.DisconnectAsync();

            Console.WriteLine("Client disconnected, press any key to exit.");
            Console.ReadKey();
        }

        private static Task OnReceivePacket(Packet packet)
        {
            var identifier = packet?.Identifier.ToString() ?? "invalid";
            Console.WriteLine($"Packet({identifier}): {packet?.As<string>().Payload}");
            return Task.CompletedTask;
        }
    }
}
