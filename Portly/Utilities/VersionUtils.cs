namespace Portly.Utilities
{
    internal static class VersionUtils
    {
        // Converts a Version object to a 16-byte array (4 ints: major, minor, build, revision)
        public static byte[] ToBytes(this Version version)
        {
            byte[] bytes = new byte[16];

            // Major and minor are always >= 0
            Array.Copy(BitConverter.GetBytes(version.Major), 0, bytes, 0, 4);
            Array.Copy(BitConverter.GetBytes(version.Minor), 0, bytes, 4, 4);

            // Build and Revision can be -1 if not specified
            Array.Copy(BitConverter.GetBytes(version.Build), 0, bytes, 8, 4);
            Array.Copy(BitConverter.GetBytes(version.Revision), 0, bytes, 12, 4);

            return bytes;
        }

        // Converts back from a 16-byte array to a Version object
        public static Version FromBytes(byte[] bytes)
        {
            if (bytes.Length != 16)
                throw new ArgumentException("Invalid byte array length for Version.");

            int major = BitConverter.ToInt32(bytes, 0);
            int minor = BitConverter.ToInt32(bytes, 4);
            int build = BitConverter.ToInt32(bytes, 8);
            int revision = BitConverter.ToInt32(bytes, 12);

            // Use correct constructor overload based on which components are valid
            if (build < 0)
            {
                return new Version(major, minor);
            }
            else if (revision < 0)
            {
                return new Version(major, minor, build);
            }
            else
            {
                return new Version(major, minor, build, revision);
            }
        }
    }
}
