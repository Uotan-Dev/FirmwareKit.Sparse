using FirmwareKit.Sparse.DataProviders;
using Xunit;

namespace FirmwareKit.Sparse.IntegrationTests
{
    public class CompatibilityAndDataProviderTests
    {
        private class LimitedReadStream : Stream
        {
            private readonly Stream inner;
            public LimitedReadStream(Stream inner) { this.inner = inner; }
            public override bool CanRead => inner.CanRead;
            public override bool CanSeek => inner.CanSeek;
            public override bool CanWrite => inner.CanWrite;
            public override long Length => inner.Length;
            public override long Position { get => inner.Position; set => inner.Position = value; }
            public override void Flush() => inner.Flush();
            public override int Read(byte[] buffer, int offset, int count)
            {
                int max = Math.Min(1, count);
                return inner.Read(buffer, offset, max);
            }
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP2_1_OR_GREATER || NET5_0_OR_GREATER
            public override int Read(Span<byte> buffer)
            {
                var tmp = new byte[1];
                var read = inner.Read(tmp, 0, 1);
                if (read <= 0) return 0;
                buffer[0] = tmp[0];
                return 1;
            }
#endif
            public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
            public override void SetLength(long value) => inner.SetLength(value);
            public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
        }

        [Fact]
        public void ReadExactly_WithPartialReads_CompletesFullRead()
        {
            var data = new byte[] { 1, 2, 3, 4, 5 };
            using var ms = new MemoryStream(data);
            using var limited = new LimitedReadStream(ms);
            var buffer = new byte[data.Length];
            limited.ReadExactly(buffer, 0, buffer.Length);
            Assert.Equal(data, buffer);

            // Also test Span overload
            ms.Position = 0;
            using var limited2 = new LimitedReadStream(ms);
            var span = new byte[data.Length];
            limited2.ReadExactly(span);
            Assert.Equal(data, span);
        }

        [Fact]
        public void StreamDataProvider_ReadsCorrectly_And_WriteToWritesData()
        {
            var data = Enumerable.Range(0, 1000).Select(i => (byte)(i % 256)).ToArray();
            using var baseMs = new MemoryStream(data);
            var provider = new StreamDataProvider(baseMs, 10, 50, leaveOpen: true);

            var buf = new byte[50];
            var read = provider.Read(0, buf, 0, buf.Length);
            Assert.Equal(50, read);
            Assert.Equal(data.Skip(10).Take(50).ToArray(), buf);

            using var outMs = new MemoryStream();
            provider.WriteTo(outMs);
            var written = outMs.ToArray();
            Assert.Equal(data.Skip(10).Take(50).ToArray(), written);

            // async write
            outMs.SetLength(0);
            outMs.Position = 0;
            provider.WriteToAsync(outMs).GetAwaiter().GetResult();
            Assert.Equal(data.Skip(10).Take(50).ToArray(), outMs.ToArray());
        }

        [Fact]
        public void StreamDataProvider_GetSubProvider_CorrectLength()
        {
            var data = Enumerable.Range(0, 200).Select(i => (byte)i).ToArray();
            using var ms = new MemoryStream(data);
            var provider = new StreamDataProvider(ms, 0, data.Length);
            var sub = provider.GetSubProvider(10, 20);
            Assert.Equal(20, sub.Length);
            var buffer = new byte[20];
            var read = sub.Read(0, buffer, 0, buffer.Length);
            Assert.Equal(20, read);
            Assert.Equal(data.Skip(10).Take(20).ToArray(), buffer);
        }
    }
}
