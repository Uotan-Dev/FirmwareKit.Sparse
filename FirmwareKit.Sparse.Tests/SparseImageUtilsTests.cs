using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Buffers.Binary;
using FirmwareKit.Sparse.Core;
using FirmwareKit.Sparse.Utils;
using Xunit;

namespace FirmwareKit.Sparse.IntegrationTests
{
    public class SparseImageUtilsTests
    {
        private const uint BlockSize = 4096;

        [Fact]
        public void Crc32_Calculate_KnownVector_ReturnsStandardCrc32()
        {
            var input = System.Text.Encoding.ASCII.GetBytes("123456789");
            var checksum = Crc32.Calculate(input);
            Assert.Equal(0xCBF43926u, checksum);
        }

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

        [Fact]
        public void GetFileInfo_WhenFileMissing_ReturnsFailure()
        {
            var missingPath = Path.Combine(Path.GetTempPath(), $"fks_missing_{Guid.NewGuid():N}.img");

            var result = SparseImageUtils.GetFileInfo(missingPath);

            Assert.False(result.Success);
            Assert.Contains("File not found", result.ErrorMessage);
        }

        [Fact]
        public void CompareFiles_WhenSecondFileMissing_ReturnsFailure()
        {
            var existingPath = Path.Combine(Path.GetTempPath(), $"fks_existing_{Guid.NewGuid():N}.bin");
            var missingPath = Path.Combine(Path.GetTempPath(), $"fks_missing_{Guid.NewGuid():N}.bin");
            try
            {
                File.WriteAllBytes(existingPath, new byte[] { 1, 2, 3, 4 });

                var result = SparseImageUtils.CompareFiles(existingPath, missingPath);

                Assert.False(result.Success);
                Assert.Contains("File not found", result.ErrorMessage);
            }
            finally
            {
                if (File.Exists(existingPath)) File.Delete(existingPath);
            }
        }

        [Fact]
        public void VerifyConversion_WhenConvertedFileMissing_ReturnsFailure()
        {
            var originalPath = Path.Combine(Path.GetTempPath(), $"fks_original_{Guid.NewGuid():N}.bin");
            var missingConvertedPath = Path.Combine(Path.GetTempPath(), $"fks_missing_{Guid.NewGuid():N}.bin");
            try
            {
                File.WriteAllBytes(originalPath, Enumerable.Repeat((byte)0x5A, 128).ToArray());

                var result = SparseImageUtils.VerifyConversion(originalPath, missingConvertedPath);

                Assert.False(result.Success);
                Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
            }
            finally
            {
                if (File.Exists(originalPath)) File.Delete(originalPath);
            }
        }

        [Fact]
        public void ExtractValidData_WhenInputIsNotSparse_ReturnsFailure()
        {
            var inputPath = Path.Combine(Path.GetTempPath(), $"fks_not_sparse_{Guid.NewGuid():N}.bin");
            var outputPath = Path.Combine(Path.GetTempPath(), $"fks_not_sparse_out_{Guid.NewGuid():N}.bin");
            try
            {
                File.WriteAllBytes(inputPath, Enumerable.Range(0, 100).Select(i => (byte)i).ToArray());

                var result = SparseImageUtils.ExtractValidData(inputPath, outputPath, partitionOffset: 0);

                Assert.False(result.Success);
                Assert.Contains("not a valid sparse image", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                if (File.Exists(inputPath)) File.Delete(inputPath);
                if (File.Exists(outputPath)) File.Delete(outputPath);
            }
        }

        [Fact]
        public void ExtractValidData_WithOffsetAcrossRawFillDontCare_ExtractsExpectedBytes()
        {
            var sparsePath = Path.Combine(Path.GetTempPath(), $"fks_extract_src_{Guid.NewGuid():N}.simg");
            var outputPath = Path.Combine(Path.GetTempPath(), $"fks_extract_out_{Guid.NewGuid():N}.bin");

            var raw1 = Enumerable.Range(0, (int)BlockSize).Select(i => (byte)(i % 251)).ToArray();
            var raw2 = Enumerable.Range(0, (int)BlockSize).Select(i => (byte)(255 - (i % 251))).ToArray();
            const uint fillValue = 0x11223344;
            var fillPattern = CreateFillPattern(fillValue, (int)BlockSize);
            var partitionOffset = (long)BlockSize / 2;

            try
            {
                using (var sparse = new SparseFile(BlockSize, BlockSize * 4))
                {
                    sparse.AddRawChunk(raw1);
                    sparse.AddFillChunk(fillValue, BlockSize);
                    sparse.AddDontCareChunk(BlockSize);
                    sparse.AddRawChunk(raw2);

                    using var fs = new FileStream(sparsePath, FileMode.Create, FileAccess.Write);
                    sparse.WriteToStream(fs, sparse: true, includeCrc: true);
                }

                var result = SparseImageUtils.ExtractValidData(sparsePath, outputPath, partitionOffset);

                Assert.True(result.Success, result.ErrorMessage);
                Assert.True(result.DataFound);
                Assert.Equal(partitionOffset, result.OffsetInBlock);

                var expected = raw1.Skip((int)partitionOffset)
                    .Concat(fillPattern)
                    .Concat(raw2)
                    .ToArray();
                var actual = File.ReadAllBytes(outputPath);

                Assert.Equal(expected.Length, result.TotalBytesExtracted);
                Assert.Equal(expected, actual);
            }
            finally
            {
                if (File.Exists(sparsePath)) File.Delete(sparsePath);
                if (File.Exists(outputPath)) File.Delete(outputPath);
            }
        }

        [Fact]
        public void ExtractValidData_WhenOffsetBeyondAllData_ReturnsNoData()
        {
            var sparsePath = Path.Combine(Path.GetTempPath(), $"fks_extract_empty_src_{Guid.NewGuid():N}.simg");
            var outputPath = Path.Combine(Path.GetTempPath(), $"fks_extract_empty_out_{Guid.NewGuid():N}.bin");

            try
            {
                using (var sparse = new SparseFile(BlockSize, BlockSize))
                {
                    sparse.AddRawChunk(Enumerable.Repeat((byte)0x4D, (int)BlockSize).ToArray());
                    using var fs = new FileStream(sparsePath, FileMode.Create, FileAccess.Write);
                    sparse.WriteToStream(fs, sparse: true, includeCrc: true);
                }

                var result = SparseImageUtils.ExtractValidData(sparsePath, outputPath, partitionOffset: BlockSize * 10L);

                Assert.True(result.Success, result.ErrorMessage);
                Assert.False(result.DataFound);
                Assert.Equal(0, result.TotalBytesExtracted);
                Assert.True(File.Exists(outputPath));
                Assert.Equal(0, new FileInfo(outputPath).Length);
            }
            finally
            {
                if (File.Exists(sparsePath)) File.Delete(sparsePath);
                if (File.Exists(outputPath)) File.Delete(outputPath);
            }
        }

        [Fact]
        public void ExtractValidDataWithCsv_WithOffsetAcrossChunks_WritesExpectedCsvMap()
        {
            var sparsePath = Path.Combine(Path.GetTempPath(), $"fks_extract_csv_src_{Guid.NewGuid():N}.simg");
            var binPath = Path.Combine(Path.GetTempPath(), $"fks_extract_csv_out_{Guid.NewGuid():N}.bin");
            var csvPath = Path.Combine(Path.GetTempPath(), $"fks_extract_csv_out_{Guid.NewGuid():N}.csv");

            var raw1 = Enumerable.Repeat((byte)0x01, (int)BlockSize).ToArray();
            var raw2 = Enumerable.Repeat((byte)0xA2, (int)BlockSize).ToArray();
            const uint fillValue = 0x99AABBCC;
            var fillPattern = CreateFillPattern(fillValue, (int)BlockSize);
            var partitionOffset = (long)BlockSize / 2;

            try
            {
                using (var sparse = new SparseFile(BlockSize, BlockSize * 4))
                {
                    sparse.AddRawChunk(raw1);
                    sparse.AddFillChunk(fillValue, BlockSize);
                    sparse.AddDontCareChunk(BlockSize);
                    sparse.AddRawChunk(raw2);

                    using var fs = new FileStream(sparsePath, FileMode.Create, FileAccess.Write);
                    sparse.WriteToStream(fs, sparse: true, includeCrc: true);
                }

                var result = SparseImageUtils.ExtractValidDataWithCsv(sparsePath, binPath, csvPath, partitionOffset);

                Assert.True(result.Success, result.ErrorMessage);
                Assert.True(result.DataFound);
                Assert.Equal(3, result.CsvRecordCount);
                Assert.Equal((int)partitionOffset + (int)BlockSize + (int)BlockSize, result.TotalBytesExtracted);

                var expectedBin = raw1.Skip((int)partitionOffset)
                    .Concat(fillPattern)
                    .Concat(raw2)
                    .ToArray();
                Assert.Equal(expectedBin, File.ReadAllBytes(binPath));

                var lines = File.ReadAllLines(csvPath);
                Assert.Equal(4, lines.Length);
                Assert.Equal("Index,File Offset(b),File Length(b),Device Offset(b),Device Length(b)", lines[0]);

                var records = lines.Skip(1).Select(ParseCsvRecord).ToList();
                Assert.Equal(1, records[0].Index);
                Assert.Equal(0, records[0].FileOffset);
                Assert.Equal(partitionOffset, records[0].FileLength);
                Assert.Equal(partitionOffset, records[0].DeviceOffset);

                Assert.Equal(2, records[1].Index);
                Assert.Equal(partitionOffset, records[1].FileOffset);
                Assert.Equal(BlockSize, records[1].FileLength);
                Assert.Equal(BlockSize, records[1].DeviceOffset);

                Assert.Equal(3, records[2].Index);
                Assert.Equal(partitionOffset + BlockSize, records[2].FileOffset);
                Assert.Equal(BlockSize, records[2].FileLength);
                Assert.Equal(BlockSize * 3L, records[2].DeviceOffset);
            }
            finally
            {
                if (File.Exists(sparsePath)) File.Delete(sparsePath);
                if (File.Exists(binPath)) File.Delete(binPath);
                if (File.Exists(csvPath)) File.Delete(csvPath);
            }
        }

        [Fact]
        public void ExtractValidDataWithCsv_WhenInputIsNotSparse_ReturnsFailure()
        {
            var inputPath = Path.Combine(Path.GetTempPath(), $"fks_not_sparse_csv_{Guid.NewGuid():N}.bin");
            var binPath = Path.Combine(Path.GetTempPath(), $"fks_not_sparse_csv_out_{Guid.NewGuid():N}.bin");
            var csvPath = Path.Combine(Path.GetTempPath(), $"fks_not_sparse_csv_out_{Guid.NewGuid():N}.csv");

            try
            {
                File.WriteAllBytes(inputPath, Enumerable.Repeat((byte)0xEF, 256).ToArray());

                var result = SparseImageUtils.ExtractValidDataWithCsv(inputPath, binPath, csvPath, partitionOffset: 0);

                Assert.False(result.Success);
                Assert.Contains("Not a valid sparse image file", result.ErrorMessage);
                Assert.False(File.Exists(binPath));
                Assert.False(File.Exists(csvPath));
            }
            finally
            {
                if (File.Exists(inputPath)) File.Delete(inputPath);
                if (File.Exists(binPath)) File.Delete(binPath);
                if (File.Exists(csvPath)) File.Delete(csvPath);
            }
        }

        private static byte[] CreateFillPattern(uint fillValue, int totalLength)
        {
            var fillBytes = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(fillBytes, fillValue);

            var output = new byte[totalLength];
            for (var i = 0; i < output.Length; i += fillBytes.Length)
            {
                Buffer.BlockCopy(fillBytes, 0, output, i, fillBytes.Length);
            }

            return output;
        }

        private static CsvRecord ParseCsvRecord(string line)
        {
            var parts = line.Split(',');
            return new CsvRecord(
                int.Parse(parts[0]),
                long.Parse(parts[1]),
                long.Parse(parts[2]),
                long.Parse(parts[3]),
                long.Parse(parts[4]));
        }

        private readonly record struct CsvRecord(int Index, long FileOffset, long FileLength, long DeviceOffset, long DeviceLength);
    }
}
