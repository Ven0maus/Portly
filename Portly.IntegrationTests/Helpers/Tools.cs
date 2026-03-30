namespace Portly.IntegrationTests.Helpers
{
    /// <summary>
    /// Contains a set of useful tools / helper methods.
    /// </summary>
    internal static class Tools
    {
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
