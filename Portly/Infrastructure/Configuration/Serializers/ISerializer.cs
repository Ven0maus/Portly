namespace Portly.Infrastructure.Configuration.Serializers
{
    /// <summary>
    /// Serializer provider
    /// </summary>
    public interface ISerializer
    {
        /// <summary>
        /// The file extension to match the type of data this serialization provider handles.
        /// </summary>
        string FileExtension { get; }

        /// <summary>
        /// Serializes an object to a string representation.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="obj"></param>
        /// <returns></returns>
        string Serialize<T>(T obj);

        /// <summary>
        /// Deserializes a string representation to an object.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="content"></param>
        /// <returns></returns>
        T? Deserialize<T>(string content);
    }
}
