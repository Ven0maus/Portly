namespace Portly.Extensions
{
    internal static class ByteExtensions
    {
        internal static byte[] Combine(this byte[] first, params byte[][] arrays)
        {
            int totalLength = first.Length;
            for (int i = 0; i < arrays.Length; i++)
                totalLength += arrays[i]?.Length ?? 0;

            byte[] result = new byte[totalLength];

            int offset = 0;
            Buffer.BlockCopy(first, 0, result, offset, first.Length);
            offset += first.Length;

            for (int i = 0; i < arrays.Length; i++)
            {
                var arr = arrays[i];
                if (arr == null) continue;

                Buffer.BlockCopy(arr, 0, result, offset, arr.Length);
                offset += arr.Length;
            }

            return result;
        }
    }
}
