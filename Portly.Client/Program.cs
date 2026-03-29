using Portly.Infrastructure.Logging;
using Portly.Runtime;

namespace Portly.Client
{
    internal class Program
    {
        private static readonly PortlyClient _client = new(logProvider: LogProviderBase.Default);

        private static async Task Main()
        {
            // Setup client events
            _client.OnConnected += OnConnected;
            _client.OnDisconnected += OnDisconnected;

            await HandleCommand("/clear");
            Write("Input: ");
            var input = Console.ReadLine();

            // Main loop
            while (true)
            {
                // Break while loop when input is exit
                if (input != null && input.Equals("exit", StringComparison.OrdinalIgnoreCase))
                    break;

                // Handle input command
                await HandleCommand(input);

                // Read next input command
                Write("Input: ");
                input = Console.ReadLine();
            }
        }

        private static async Task HandleCommand(string? command)
        {
            if (string.IsNullOrWhiteSpace(command)) return;

            if (command.Equals("/help", StringComparison.OrdinalIgnoreCase))
            {
                WriteLine("Available commands:");
                Write(true, ("/connect ", ConsoleColor.Cyan), ("<ip> <port> ", ConsoleColor.Yellow), (": Connects to the specified server.", null));
                Write(true, ("/disconnect ", ConsoleColor.Cyan), (": Disconnects from the connected server.", null));
                Write(true, ("/clear ", ConsoleColor.Cyan), (": Clears the console.", null));
                Write(true, ("/help ", ConsoleColor.Cyan), (": Shows a list of useable commands.", null));
            }
            else if (command.Equals("/clear", StringComparison.OrdinalIgnoreCase))
            {
                Console.Clear();
                WriteLine("Welcome to the client terminal.");
                Write("Type ");
                Write("/help ", ConsoleColor.Cyan);
                WriteLine("for more information.");
            }
            else if (command.StartsWith("/connect", StringComparison.OrdinalIgnoreCase))
            {
                var parts = command.Split(' ');
                if (parts.Length == 3)
                {
                    if (!int.TryParse(parts[2], out var port))
                    {
                        WriteLine($"Invalid port \"{parts[2]}\".", ConsoleColor.Red);
                        return;
                    }

                    try
                    {
                        WriteLine($"Attempting to connect to {parts[1]}:{port}");
                        await _client.ConnectAsync(parts[1], port);
                    }
                    catch (Exception ex)
                    {
                        WriteLine("Unable to connect: " + ex.Message, ConsoleColor.Red);
                    }
                }
                else
                {
                    WriteLine("Invalid connect statement, use as /connect <ip> <port>", ConsoleColor.Red);
                }
            }
            else if (command.Equals("/disconnect", StringComparison.OrdinalIgnoreCase))
            {
                await _client.DisconnectAsync();
            }
        }

        private static void OnDisconnected(object? sender, EventArgs e)
        {
            WriteLine("Disconnected.", ConsoleColor.White);
        }

        private static void OnConnected(object? sender, EventArgs e)
        {
            WriteLine("Connected.", ConsoleColor.White);
        }

        private static void Write(bool newLineAtEnd = false, params (string message, ConsoleColor? color)[] values)
        {
            foreach (var (message, color) in values)
                Write(message, color);
            if (newLineAtEnd)
                WriteLine();
        }

        private static void Write(string message, ConsoleColor? color = null)
        {
            color ??= Console.ForegroundColor;
            var prev = Console.ForegroundColor;
            Console.ForegroundColor = color.Value;
            Console.Write(message);
            Console.ForegroundColor = prev;
        }

        private static void WriteLine(params (string message, ConsoleColor? color)[] values)
        {
            if (values == null || values.Length == 0)
            {
                Console.WriteLine();
                return;
            }

            foreach (var (message, color) in values)
                WriteLine(message, color);
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
