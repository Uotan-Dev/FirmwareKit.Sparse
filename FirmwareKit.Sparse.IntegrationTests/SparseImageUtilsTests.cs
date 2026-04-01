using System;
using System.IO;
using System.Linq;
using FirmwareKit.Sparse.Core;
using FirmwareKit.Sparse.Utils;
using Xunit;

namespace FirmwareKit.Sparse.IntegrationTests
{
    public class SparseImageUtilsTests
    {
        private const uint BlockSize = 4096;

        [Fact]
        public void GetFileInfo_ReturnsSparseInfo()
        {
            var data = Enumerable.Range(0, (int)BlockSize).Select(i => (byte)(i % 251)).ToArray();
            using var sparse = new SparseFile(BlockSize, BlockSize);
            sparse.AddRawChunk(data);

            var temp = Path.Combine(Path.GetTempPath(), $"fks_utils_{Guid.NewGuid():N}.simg");
            try
            {
                using (var fs = new FileStream(temp, FileMode.Create, FileAccess.Write))
                {
                    sparse.WriteToStream(fs, sparse: true);
                }

                var info = SparseImageUtils.GetFileInfo(temp);
                Assert.True(info.Success);
                Assert.True(info.IsSparseImage);
                Assert.NotNull(info.SparseInfo);
                Assert.Equal(BlockSize, info.SparseInfo.BlockSize);
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }

        [Fact]
        public void CompareFiles_RawVsSparse_ReturnsTypeMismatch()
        {
            var raw = Enumerable.Range(0, 5000).Select(i => (byte)(i % 251)).ToArray();
            var rawPath = Path.Combine(Path.GetTempPath(), $"fks_raw_{Guid.NewGuid():N}.bin");
            File.WriteAllBytes(rawPath, raw);

            using var sparse = new SparseFile(BlockSize, BlockSize * 2);
            sparse.AddRawChunk(raw.Take((int)BlockSize).ToArray());
            var sparsePath = Path.Combine(Path.GetTempPath(), $"fks_sparse_{Guid.NewGuid():N}.simg");
            try
            {
                using (var fs = new FileStream(sparsePath, FileMode.Create, FileAccess.Write))
                {
                    sparse.WriteToStream(fs, sparse: true);
                }

                var cmp = SparseImageUtils.CompareFiles(rawPath, sparsePath);
                Assert.True(cmp.Success);
                Assert.False(cmp.TypeMatches);
            }
            finally
            {
                if (File.Exists(rawPath)) File.Delete(rawPath);
                if (File.Exists(sparsePath)) File.Delete(sparsePath);
            }
        }

        [Fact]
        public void CreateTestSparseImage_CreatesFile()
        {
            var outPath = Path.Combine(Path.GetTempPath(), $"fks_create_{Guid.NewGuid():N}.simg");
            try
            {
                var res = SparseImageUtils.CreateTestSparseImage(outPath, sizeInMB: 1, blockSize: BlockSize);
                Assert.True(res.Success);
                Assert.True(File.Exists(outPath));
                var fi = new FileInfo(outPath);
                Assert.True(fi.Length > 0);
            }
            finally
            {
                if (File.Exists(outPath)) File.Delete(outPath);
            }
        }
    }
}
