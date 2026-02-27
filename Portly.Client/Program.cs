using Portly.Core.Client;
using Portly.Core.PacketHandling;

namespace Portly.Client
{
    internal class Program
    {
        private static async Task Main()
        {
            var client = new PortlyClient();
            await client.ConnectAsync("localhost", 25565, OnReceivePacket);

            Console.WriteLine("Write shutdown to stop the client.");
            var input = Console.ReadLine();
            while (string.IsNullOrWhiteSpace(input) || !input.Equals("shutdown", StringComparison.OrdinalIgnoreCase))
            {
                input = Console.ReadLine();
            }

            await client.DisconnectAsync();

            Console.WriteLine("Client disconnected, press any key to exit.");
            Console.ReadKey();
        }

        private static Task OnReceivePacket(Packet packet)
        {
            Console.WriteLine("Received task: " + (packet?.Type.ToString() ?? "invalid"));
            return Task.CompletedTask;
        }
    }
}
