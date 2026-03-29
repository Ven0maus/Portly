using Portly.Infrastructure.Logging;
using Portly.Runtime;

namespace Portly.ExampleServer
{
    internal class Program
    {
        private static readonly PortlyServer _server = new(logProvider: LogProviderBase.Default);

        private static Task? _serverTask;

        private static async Task Main()
        {
            await HandleCommand("/clear");

            Write("Input: ");
            var input = Console.ReadLine();

            while (true)
            {
                if (input != null && input.Equals("exit", StringComparison.OrdinalIgnoreCase))
                    break;

                await HandleCommand(input);

                Write("Input: ");
                input = Console.ReadLine();
            }

            // Ensure server is stopped before exit
            await StopServerIfRunning();
        }

        private static async Task HandleCommand(string? command)
        {
            if (string.IsNullOrWhiteSpace(command)) return;

            if (command.Equals("/help", StringComparison.OrdinalIgnoreCase))
            {
                WriteLine("Available commands:");
                Write(true, ("/start ", ConsoleColor.Cyan), (": Starts the server.", null));
                Write(true, ("/stop ", ConsoleColor.Cyan), (": Stops the server.", null));
                Write(true, ("/status ", ConsoleColor.Cyan), (": Shows server status.", null));
                Write(true, ("/clear ", ConsoleColor.Cyan), (": Clears the console.", null));
                Write(true, ("/help ", ConsoleColor.Cyan), (": Shows this help.", null));
            }
            else if (command.Equals("/clear", StringComparison.OrdinalIgnoreCase))
            {
                Console.Clear();
                WriteLine("Welcome to the server terminal.");
                Write("Type ");
                Write("/help ", ConsoleColor.Cyan);
                WriteLine("for more information.");
            }
            else if (command.Equals("/start", StringComparison.OrdinalIgnoreCase))
            {
                if (_serverTask != null)
                {
                    WriteLine("Server is already running.", ConsoleColor.Yellow);
                    return;
                }

                try
                {
                    WriteLine($"Starting server...");
                    _serverTask = _server.StartAsync();
                }
                catch (Exception ex)
                {
                    WriteLine("Failed to start server: " + ex.Message, ConsoleColor.Red);
                    _serverTask = null;
                }
            }
            else if (command.Equals("/stop", StringComparison.OrdinalIgnoreCase))
            {
                if (_serverTask == null)
                {
                    WriteLine("Server is not running.", ConsoleColor.Yellow);
                    return;
                }

                await StopServerIfRunning();
            }
            else if (command.Equals("/status", StringComparison.OrdinalIgnoreCase))
            {
                WriteLine(_serverTask == null ? "Server is stopped." : "Server is running.");
            }
        }

        private static async Task StopServerIfRunning()
        {
            if (_serverTask == null)
                return;

            try
            {
                WriteLine("Stopping server...");
                await _server.StopAsync();
                await _serverTask;
            }
            catch (OperationCanceledException) { }
            finally
            {
                _serverTask = null;
            }

            WriteLine("Server stopped.");
        }

        // === Same console helpers as client ===

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
