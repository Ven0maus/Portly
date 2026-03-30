namespace Portly.Tests.Helpers
{
    /// <summary>
    /// Contains a set of useful tools / helper methods.
    /// </summary>
    internal static class Tools
    {
        internal static (string serverDir, string clientDir) CreateIsolatedTestDirectories()
        {
            var mainFolder = Path.Combine("PortlyTests", $"{Guid.NewGuid()}");
            var clientFolder = Path.Combine(mainFolder, "client");
            var serverFolder = Path.Combine(mainFolder, "server");

            Directory.CreateDirectory(clientFolder);
            Directory.CreateDirectory(serverFolder);

            return (serverFolder, clientFolder);
        }
    }
}
