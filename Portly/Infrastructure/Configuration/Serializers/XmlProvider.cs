using System.Text;
using System.Xml.Serialization;

namespace Portly.Infrastructure.Configuration.Serializers
{
    /// <summary>
    /// A basic xml serialization provider.
    /// </summary>
    public class XmlProvider : ISerializer
    {
        /// <inheritdoc/>
        public string FileExtension => ".xml";

        /// <inheritdoc/>
        public T? Deserialize<T>(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return default;

            var serializer = new XmlSerializer(typeof(T));

            using var reader = new StringReader(content);
            return (T?)serializer.Deserialize(reader);
        }

        /// <inheritdoc/>
        public string Serialize<T>(T obj)
        {
            if (obj == null)
                return string.Empty;

            var serializer = new XmlSerializer(typeof(T));

            var namespaces = new XmlSerializerNamespaces();
            namespaces.Add(string.Empty, string.Empty); // removes xsi/xsd

            using var stringWriter = new Utf8StringWriter();
            serializer.Serialize(stringWriter, obj, namespaces);

            return stringWriter.ToString();
        }

        /// <summary>
        /// Ensures UTF-8 encoding instead of UTF-16 (default for StringWriter).
        /// </summary>
        private sealed class Utf8StringWriter : StringWriter
        {
            public override Encoding Encoding => Encoding.UTF8;
        }
    }
}
