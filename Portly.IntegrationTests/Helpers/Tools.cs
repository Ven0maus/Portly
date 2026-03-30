using System.Net;
using System.Net.Sockets;

namespace Portly.IntegrationTests.Helpers
{
    /// <summary>
    /// Contains a set of useful tools / helper methods.
    /// </summary>
    internal static class Tools
    {
        /// <summary>
        /// Returns a free unused port.
        /// </summary>
        /// <returns></returns>
        internal static int GetFreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        internal static void CleanupClientSetup()
        {
            File.Delete("known_servers.json");
        }

        internal static void CleanupServerSetup()
        {
            File.Delete("server_key.json");
            File.Delete("server_config.xml");
            File.Delete("ip-blacklist.txt");
            File.Delete("ip-whitelist.txt");
        }
    }
}
