using FirmwareKit.Sparse.Core;
using FirmwareKit.Sparse.Models;
using Xunit;

namespace FirmwareKit.Sparse.IntegrationTests
{
    public class SparseExtendedHeaderTests
    {
        private const uint BlockSize = 4096;

        [Fact]
        public void Roundtrip_ExtendedHeader_ReadsBack()
        {
            using var sparseFile = new SparseFile(BlockSize, BlockSize * 2);
            sparseFile.Header = sparseFile.Header with
            {
                FileHeaderSize = (ushort)(SparseFormat.SparseHeaderSize + 8),
                ChunkHeaderSize = (ushort)(SparseFormat.ChunkHeaderSize + 4)
            };

            var data = Enumerable.Range(0, (int)BlockSize).Select(i => (byte)(i % 251)).ToArray();
            sparseFile.AddRawChunk(data);

            using var ms = new MemoryStream();
            sparseFile.WriteToStream(ms, sparse: true, includeCrc: false);

            ms.Position = 0;
            using var parsed = SparseFile.FromStream(ms, validateCrc: false);
            Assert.Equal(sparseFile.Header.BlockSize, parsed.Header.BlockSize);
            Assert.Equal(sparseFile.Chunks.Count, parsed.Chunks.Count);
            Assert.Equal(sparseFile.Header.FileHeaderSize, parsed.Header.FileHeaderSize);
            Assert.Equal(sparseFile.Header.ChunkHeaderSize, parsed.Header.ChunkHeaderSize);
        }

        [Fact]
        public void Writer_PadsHeader_WhenExtendedSizes()
        {
            using var sparseFile = new SparseFile(BlockSize, BlockSize);
            sparseFile.Header = sparseFile.Header with
            {
                FileHeaderSize = (ushort)(SparseFormat.SparseHeaderSize + 8),
                ChunkHeaderSize = (ushort)(SparseFormat.ChunkHeaderSize + 4)
            };
            sparseFile.AddRawChunk(new byte[BlockSize]);

            using var ms = new MemoryStream();
            sparseFile.WriteToStream(ms, sparse: true, includeCrc: false);

            var bytes = ms.ToArray();

            // header padding between default header size and extended size should be zero
            for (int i = SparseFormat.SparseHeaderSize; i < sparseFile.Header.FileHeaderSize; i++)
            {
                Assert.Equal(0, bytes[i]);
            }

            // chunk header padding: bytes after the default chunk header up to extended chunk header size
            var chunkHeaderStart = sparseFile.Header.FileHeaderSize;
            for (int i = chunkHeaderStart + SparseFormat.ChunkHeaderSize; i < chunkHeaderStart + sparseFile.Header.ChunkHeaderSize; i++)
            {
                Assert.Equal(0, bytes[i]);
            }
        }

        [Fact]
        public void Resparse_Parts_DoNotExceedMaxFileSize()
        {
            using var sparseFile = new SparseFile(BlockSize, BlockSize * 100);
            for (int i = 0; i < 10; i++) sparseFile.AddRawChunk(new byte[BlockSize * 10]);

            var maxFileSize = (long)SparseFormat.SparseHeaderSize + SparseFormat.ChunkHeaderSize + BlockSize * 15;
            var parts = sparseFile.Resparse(maxFileSize).ToList();

            Assert.True(parts.Count > 1);
            foreach (var p in parts)
            {
                using var outMs = new MemoryStream();
                p.WriteToStream(outMs, sparse: true, includeCrc: false);
                Assert.True(outMs.Length <= maxFileSize, $"Part length {outMs.Length} exceeds max {maxFileSize}");
            }
        }
    }
}
