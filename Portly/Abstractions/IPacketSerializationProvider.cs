namespace Portly.Abstractions
{
    /// <summary>
    /// Serialization provider for packets
    /// </summary>
    public interface IPacketSerializationProvider
    {
        /// <summary>
        /// Method to serialize data from the packet into a string.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="data"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        byte[] Serialize<T>(T data, CancellationToken token = default) where T : IPacket;

        /// <summary>
        /// Method to deserialize data from a string into a packet.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        T Deserialize<T>(in ReadOnlyMemory<byte> bytes, CancellationToken token = default) where T : IPacket;
    }
}
