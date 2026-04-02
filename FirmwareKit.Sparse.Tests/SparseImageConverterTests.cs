using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FirmwareKit.Sparse.Core;
using FirmwareKit.Sparse.Utils;
using Xunit;

namespace FirmwareKit.Sparse.IntegrationTests
{
    public class SparseImageConverterTests
    {
        private const uint BlockSize = 4096;

        [Fact]
        public async Task ConvertRawToSparseAsync_CreatesSparseFile()
        {
            var raw = Enumerable.Range(0, 5000).Select(i => (byte)(i % 251)).ToArray();
            var input = Path.Combine(Path.GetTempPath(), $"fks_conv_raw_{Guid.NewGuid():N}.bin");
            var output = Path.Combine(Path.GetTempPath(), $"fks_conv_sparse_{Guid.NewGuid():N}.simg");
            try
            {
                File.WriteAllBytes(input, raw);

                await SparseImageConverter.ConvertRawToSparseAsync(input, output, BlockSize);

                Assert.True(File.Exists(output));
                var header = SparseFile.PeekHeader(output);
                Assert.True(header.IsValid());
            }
            finally
            {
                if (File.Exists(input)) File.Delete(input);
                if (File.Exists(output)) File.Delete(output);
            }
        }

        [Fact]
        public async Task ConvertSparseToRawAsync_CreatesRawFile()
        {
            var data = Enumerable.Range(0, (int)BlockSize).Select(i => (byte)(i % 199)).ToArray();
            using var sparse = new SparseFile(BlockSize, BlockSize);
            sparse.AddRawChunk(data);

            var sparsePath = Path.Combine(Path.GetTempPath(), $"fks_conv_src_{Guid.NewGuid():N}.simg");
            var rawOut = Path.Combine(Path.GetTempPath(), $"fks_conv_out_{Guid.NewGuid():N}.bin");
            try
            {
                using (var fs = new FileStream(sparsePath, FileMode.Create, FileAccess.Write))
                {
                    sparse.WriteToStream(fs, sparse: true);
                }

                await SparseImageConverter.ConvertSparseToRawAsync(new[] { sparsePath }, rawOut);

                Assert.True(File.Exists(rawOut));
                var outBytes = File.ReadAllBytes(rawOut);
                Assert.True(outBytes.Length >= data.Length);
                Assert.Equal(data, outBytes.Take(data.Length).ToArray());
            }
            finally
            {
                if (File.Exists(sparsePath)) File.Delete(sparsePath);
                if (File.Exists(rawOut)) File.Delete(rawOut);
            }
        }
    }
}
