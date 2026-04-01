namespace Portly.Tests.Helpers
{
    public class FragmentedStream : Stream
    {
        private readonly byte[] _data;
        private readonly int _chunkSize;
        private int _position;

        public FragmentedStream(byte[] data, int chunkSize)
        {
            _data = data;
            _chunkSize = chunkSize;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_position >= _data.Length)
                return 0;

            var size = Math.Min(_chunkSize, _data.Length - _position);
            _data.AsMemory(_position, size).CopyTo(buffer);
            _position += size;

            await Task.Yield(); // force async boundary
            return size;
        }

        // Boilerplate:
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _data.Length;
        public override long Position { get => _position; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
