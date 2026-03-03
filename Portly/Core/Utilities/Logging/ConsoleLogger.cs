namespace Portly.Core.Utilities.Logging
{
    /// <summary>
    /// Logs information to the console.
    /// </summary>
    /// <param name="enableDebug">If debug level should be enabled or not.</param>
    public class ConsoleLogger(bool enableDebug = false) : LogProviderBase(enableDebug)
    {
        private readonly Lock _lock = new();

        /// <inheritdoc/>
        protected override void Write(string message, LogLevel logLevel)
        {
            switch (logLevel)
            {
                case LogLevel.Debug:
                    WriteWithColor(message, ConsoleColor.Magenta);
                    break;
                case LogLevel.Info:
                    WriteWithColor(message, ConsoleColor.Gray);
                    break;
                case LogLevel.Error:
                    WriteWithColor(message, ConsoleColor.Red);
                    break;
                case LogLevel.Warning:
                    WriteWithColor(message, ConsoleColor.Yellow);
                    break;
                default:
                    throw new NotImplementedException(logLevel.ToString());
            }
        }

        private void WriteWithColor(string message, ConsoleColor color)
        {
            lock (_lock)
            {
                var previousColor = Console.ForegroundColor;
                Console.ForegroundColor = color;
                Console.WriteLine(message);
                Console.ForegroundColor = previousColor;
            }
        }
    }
}
