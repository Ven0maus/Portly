using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Portly.Core.Networking
{
    /// <summary>
    /// Provider for replay protection, nonce and timestamp creation.
    /// </summary>
    public sealed class ReplayProtection
    {
        private readonly ConcurrentDictionary<string, DateTime> _seenNonces = new();
        private readonly ConcurrentQueue<(string nonce, DateTime time)> _queue = new();
        private readonly TimeSpan _validWindow;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="validWindow"></param>
        public ReplayProtection(TimeSpan validWindow)
        {
            _validWindow = validWindow;
        }

        /// <summary>
        /// Validation to verify if a request is valid based on its nonce and timestamp.
        /// </summary>
        /// <param name="nonce"></param>
        /// <param name="timestampUtc"></param>
        /// <returns></returns>
        public bool ValidateRequest(string? nonce, DateTime? timestampUtc)
        {
            var now = DateTime.UtcNow;

            if (nonce == null || timestampUtc == null)
                return false;

            if (timestampUtc < now - _validWindow || timestampUtc > now + _validWindow)
                return false;

            if (!_seenNonces.TryAdd(nonce, timestampUtc.Value))
                return false;

            _queue.Enqueue((nonce, timestampUtc.Value));

            CleanupExpired(now);

            return true;
        }

        /// <summary>
        /// Creates a unique nonce and timestamp representing UtcNow.
        /// </summary>
        /// <returns></returns>
        public static (string nonce, DateTime timestampUtc) CreateNonceWithTimestamp()
        {
            Span<byte> bytes = stackalloc byte[16];
            RandomNumberGenerator.Fill(bytes);

            string nonce = Convert.ToHexString(bytes);
            DateTime timestamp = DateTime.UtcNow;

            return (nonce, timestamp);
        }

        private void CleanupExpired(DateTime now)
        {
            while (_queue.TryPeek(out var entry))
            {
                if (now - entry.time <= _validWindow)
                    break;

                _queue.TryDequeue(out var removed);
                _seenNonces.TryRemove(removed.nonce, out _);
            }
        }
    }
}
