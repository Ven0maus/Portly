using System.Text.Json;

namespace Portly.Infrastructure.Configuration.Serializers
{
    /// <summary>
    /// A basic json serialization provider.
    /// </summary>
    public class JsonProvider : ISerializer
    {
        private static readonly JsonSerializerOptions _serializerOptions = new() { WriteIndented = true };

        /// <inheritdoc/>
        public string FileExtension => ".json";

        /// <inheritdoc/>
        public T? Deserialize<T>(string content)
        {
            return JsonSerializer.Deserialize<T>(content, _serializerOptions);
        }

        /// <inheritdoc/>
        public string Serialize<T>(T obj)
        {
            return JsonSerializer.Serialize(obj, _serializerOptions);
        }
    }
}
