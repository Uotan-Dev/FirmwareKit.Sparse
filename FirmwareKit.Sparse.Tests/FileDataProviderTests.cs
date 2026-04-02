using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FirmwareKit.Sparse.DataProviders;
using Xunit;

namespace FirmwareKit.Sparse.IntegrationTests
{
    public class FileDataProviderTests
    {
        [Fact]
        public void Read_SubProvider_ReadsCorrectRange()
        {
            var data = Enumerable.Range(0, 1000).Select(i => (byte)(i % 256)).ToArray();
            var path = Path.Combine(Path.GetTempPath(), $"fks_fdp_{Guid.NewGuid():N}.bin");
            try
            {
                File.WriteAllBytes(path, data);
                var provider = new FileDataProvider(path, 10, 200);
                var buf = new byte[200];
                var read = provider.Read(0, buf, 0, buf.Length);
                Assert.Equal(200, read);
                Assert.Equal(data.Skip(10).Take(200).ToArray(), buf);

                var sub = provider.GetSubProvider(5, 50);
                var sb = new byte[50];
                var sr = sub.Read(0, sb, 0, sb.Length);
                Assert.Equal(50, sr);
                Assert.Equal(data.Skip(15).Take(50).ToArray(), sb);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Fact]
        public async Task WriteToAsync_WritesCorrectData()
        {
            var data = Enumerable.Range(0, 5000).Select(i => (byte)(i % 251)).ToArray();
            var path = Path.Combine(Path.GetTempPath(), $"fks_fdp2_{Guid.NewGuid():N}.bin");
            try
            {
                File.WriteAllBytes(path, data);
                var provider = new FileDataProvider(path, 100, 1000);
                using var ms = new MemoryStream();
                await provider.WriteToAsync(ms);
                var outData = ms.ToArray();
                Assert.Equal(data.Skip(100).Take(1000).ToArray(), outData);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }
}
