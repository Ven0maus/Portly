namespace Portly.Core.Interfaces
{
    /// <summary>
    /// Represents the client-side data object
    /// </summary>
    public interface IClient
    {
        /// <summary>
        /// Connects asynchronously to a server.
        /// </summary>
        /// <param name="host"></param>
        /// <param name="port"></param>
        /// <returns></returns>
        Task ConnectAsync(string host, int port);

        /// <summary>
        /// Sends a packet asynchronously to the connected server.
        /// </summary>
        /// <param name="packet"></param>
        /// <param name="encrypt"></param>
        /// <returns></returns>
        Task SendPacketAsync(IPacket packet, bool encrypt);

        /// <summary>
        /// Disconnects asynchronously from the connected server.
        /// </summary>
        /// <returns></returns>
        Task DisconnectAsync();
    }
}
