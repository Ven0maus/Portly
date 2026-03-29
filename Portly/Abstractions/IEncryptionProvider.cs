namespace Portly.Abstractions
{
    /// <summary>
    /// Encryption provider
    /// </summary>
    public interface IEncryptionProvider
    {
        /// <summary>
        /// Encrypts the data.
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        byte[] Encrypt(byte[] data);

        /// <summary>
        /// Decrypts the data.
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        byte[] Decrypt(byte[] data);
    }
}
