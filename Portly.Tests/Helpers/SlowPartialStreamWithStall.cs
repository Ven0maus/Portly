namespace Portly.Tests.Helpers
{
    internal sealed class SlowPartialStreamWithStall : Stream
    {
        private readonly byte[] _data;
        private readonly int _fastBytes;
        private readonly int _slowDelayMs;
        private int _position;

        public SlowPartialStreamWithStall(byte[] data, int fastBytes, int slowDelayMs)
        {
            _data = data;
            _fastBytes = fastBytes;
            _slowDelayMs = slowDelayMs;
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (_position >= _data.Length)
                return 0;

            if (_position >= _fastBytes)
                await Task.Delay(_slowDelayMs, cancellationToken);

            int toCopy = Math.Min(1, _data.Length - _position);
            _data.AsMemory(_position, toCopy).CopyTo(buffer);

            _position += toCopy;
            return toCopy;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position >= _data.Length)
                return 0;

            if (_position >= _fastBytes)
                Thread.Sleep(_slowDelayMs);

            int toCopy = Math.Min(1, _data.Length - _position);
            Array.Copy(_data, _position, buffer, offset, toCopy);

            _position += toCopy;
            return toCopy;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;

        public override long Length => _data.Length;

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override void Flush() { }

        public override long Seek(long offset, SeekOrigin origin)
            => throw new NotSupportedException();

        public override void SetLength(long value)
            => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
            => throw new NotSupportedException();
    }
}
