using FirmwareKit.Sparse.Core;
using FirmwareKit.Sparse.IO;
using FirmwareKit.Sparse.Models;
using FirmwareKit.Sparse.Streams;
using FirmwareKit.Sparse.Utils;
using System.Buffers.Binary;
using System.Linq;
using System.Runtime.InteropServices;
using Xunit;

namespace FirmwareKit.Sparse.IntegrationTests;

public class SparseFileIntegrationTests
{
    private const uint BlockSize = 4096;

    [Fact]
    public void FromRawFile_WhenFileNotBlockAligned_UsesAlignedRawChunkTotalSize()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"fks_raw_align_{Guid.NewGuid():N}.bin");
        var rawData = Enumerable.Range(0, 5000).Select(i => (byte)(i % 251)).ToArray();

        try
        {
            File.WriteAllBytes(tempFile, rawData);
            using var sparseFile = SparseFile.FromRawFile(tempFile, BlockSize);

            var rawChunk = Assert.Single(sparseFile.Chunks);
            var expectedTotalSize = SparseFormat.ChunkHeaderSize + rawChunk.Header.ChunkSize * BlockSize;
            Assert.Equal(expectedTotalSize, rawChunk.Header.TotalSize);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public void FromBuffer_ParsedSparse_CanStillExportRawDataAfterSourceDisposed()
    {
        using var sparseFile = new SparseFile(BlockSize, BlockSize);
        var raw = Enumerable.Range(0, (int)BlockSize).Select(i => (byte)(i % 233)).ToArray();
        sparseFile.AddRawChunk(raw);

        using var sparseOut = new MemoryStream();
        sparseFile.WriteToStream(sparseOut, sparse: true, includeCrc: false);

        using var parsed = SparseFile.FromBuffer(sparseOut.ToArray(), validateCrc: false);
        using var rawOut = new MemoryStream();
        parsed.WriteRawToStream(rawOut);

        Assert.Equal(raw, rawOut.ToArray().Take(raw.Length).ToArray());
    }

    [Fact]
    public void ImportAuto_WithBundledSimgSample_DetectsSparseInstance()
    {
        var samplePath = Path.Combine(AppContext.BaseDirectory, "simg.simg");
        Assert.True(File.Exists(samplePath), $"Sample file not found: {samplePath}");

        var header = SparseFile.PeekHeader(samplePath);
        Assert.Equal(SparseFormat.SparseHeaderMagic, header.Magic);
        Assert.True(header.IsValid());

        using var sparseFile = SparseFile.ImportAuto(samplePath);
        Assert.True(sparseFile.Header.IsValid());
        Assert.True(sparseFile.Chunks.Count > 0);
        Assert.Equal(header.BlockSize, sparseFile.Header.BlockSize);
    }

    [Fact]
    public void WriteAndReadBack_WithMixedChunks_ProducesEquivalentRawData()
    {
        using var sparseFile = new SparseFile(BlockSize, BlockSize * 4);

        var firstRaw = Enumerable.Range(0, 3000).Select(i => (byte)(i % 251)).ToArray();
        var secondRaw = Enumerable.Range(0, (int)BlockSize).Select(i => (byte)(255 - (i % 223))).ToArray();

        sparseFile.AddRawChunk(firstRaw);
        sparseFile.AddFillChunk(0x11223344, BlockSize);
        sparseFile.AddDontCareChunk(BlockSize);
        sparseFile.AddRawChunk(secondRaw);

        var tempSparse = Path.Combine(Path.GetTempPath(), $"fks_sparse_{Guid.NewGuid():N}.img");
        try
        {
            using (var sparseOutput = new FileStream(tempSparse, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                sparseFile.WriteToStream(sparseOutput, sparse: true, includeCrc: true);
            }

            using var parsed = SparseFile.FromImageFile(tempSparse, validateCrc: true);

            using var expectedRaw = new MemoryStream();
            using var parsedRaw = new MemoryStream();
            sparseFile.WriteRawToStream(expectedRaw);
            parsed.WriteRawToStream(parsedRaw);

            Assert.Equal(expectedRaw.ToArray(), parsedRaw.ToArray());
            Assert.Equal(sparseFile.Header.TotalBlocks, parsed.Header.TotalBlocks);
            Assert.Equal(sparseFile.Chunks.Count, parsed.Chunks.Count);
        }
        finally
        {
            if (File.Exists(tempSparse))
            {
                File.Delete(tempSparse);
            }
        }
    }

    [Fact]
    public void ImportAuto_WithRawFile_DetectsAndImportsAsRaw()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"fks_raw_{Guid.NewGuid():N}.bin");
        var rawData = Enumerable.Range(0, 5000).Select(i => (byte)(i % 241)).ToArray();

        try
        {
            File.WriteAllBytes(tempFile, rawData);

            using var sparseFile = SparseFile.ImportAuto(tempFile);
            Assert.Single(sparseFile.Chunks);
            Assert.Equal((ushort)ChunkType.Raw, sparseFile.Chunks[0].Header.ChunkType);

            using var rawOutput = new MemoryStream();
            sparseFile.WriteRawToStream(rawOutput);

            var output = rawOutput.ToArray();
            Assert.True(output.Length >= rawData.Length);
            Assert.Equal(rawData, output.Take(rawData.Length).ToArray());
            Assert.All(output.Skip(rawData.Length), b => Assert.Equal((byte)0, b));
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public void AddChunk_OverlappingRange_ThrowsArgumentException()
    {
        using var sparseFile = new SparseFile(BlockSize, BlockSize * 3);
        sparseFile.AddRawChunk(new byte[BlockSize]);

        var ex = Assert.Throws<ArgumentException>(() => sparseFile.AddFillChunk(0xAABBCCDD, BlockSize, blockIndex: 0));
        Assert.Contains("overlaps", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AddChunk_WithFutureBlockIndex_InsertsDontCareGapBeforeChunk()
    {
        using var sparseFile = new SparseFile(BlockSize, BlockSize * 4);
        sparseFile.AddRawChunk(new byte[BlockSize], blockIndex: 2);

        Assert.Equal(2, sparseFile.Chunks.Count);

        var gapChunk = sparseFile.Chunks[0];
        Assert.Equal((ushort)ChunkType.DontCare, gapChunk.Header.ChunkType);
        Assert.Equal(0u, gapChunk.StartBlock);
        Assert.Equal(2u, gapChunk.Header.ChunkSize);

        var rawChunk = sparseFile.Chunks[1];
        Assert.Equal((ushort)ChunkType.Raw, rawChunk.Header.ChunkType);
        Assert.Equal(2u, rawChunk.StartBlock);
        Assert.Equal(1u, rawChunk.Header.ChunkSize);
    }

    [Fact]
    public void FromBuffer_WhenCrcTampered_ThrowsInvalidDataException()
    {
        using var sparseFile = new SparseFile(BlockSize, BlockSize);
        sparseFile.AddRawChunk(Enumerable.Repeat((byte)0x5A, (int)BlockSize).ToArray());

        using var output = new MemoryStream();
        sparseFile.WriteToStream(output, sparse: true, includeCrc: true);

        var tampered = output.ToArray();
        tampered[^1] ^= 0xFF;

        Assert.Throws<InvalidDataException>(() => SparseFile.FromBuffer(tampered, validateCrc: true));
    }

    [Fact]
    public void Resize_ChangesTotalBlocksAndExportLength()
    {
        using var sparseFile = new SparseFile(BlockSize, BlockSize * 5);
        Assert.Equal(5u, sparseFile.Header.TotalBlocks);

        long newSize = BlockSize * 10 + 123;
        sparseFile.Resize(newSize);
        var expectedBlocks = (uint)((newSize + BlockSize - 1) / BlockSize);
        Assert.Equal(expectedBlocks, sparseFile.Header.TotalBlocks);

        using var outStream = new MemoryStream();
        sparseFile.WriteRawToStream(outStream);
        Assert.Equal((long)expectedBlocks * BlockSize, outStream.Length);
    }

    [Fact]
    public void ForEachChunk_EnumeratesCorrectly()
    {
        using var sparseFile = new SparseFile(BlockSize, BlockSize * 4);
        sparseFile.AddRawChunk(new byte[BlockSize]);
        sparseFile.AddFillChunk(0x1, BlockSize);
        sparseFile.AddDontCareChunk(BlockSize);
        var seen = new List<ushort>();
        sparseFile.ForEachChunk((chunk, start, sz) => seen.Add(chunk.Header.ChunkType));
        Assert.Equal(2, seen.Count); // raw + fill only
    }

    [Fact]
    public void WriteWithCallback_MatchesStreamOutput()
    {
        using var sparseFile = new SparseFile(BlockSize, BlockSize * 2);
        var data = Enumerable.Repeat((byte)0x77, (int)BlockSize).ToArray();
        sparseFile.AddRawChunk(data);

        using var expected = new MemoryStream();
        sparseFile.WriteToStream(expected, sparse: true);

        using var callbackOut = new MemoryStream();
        sparseFile.WriteWithCallback((b,l) => { if(b!=null) callbackOut.Write(b,0,l); return 0; }, sparse: true);

        Assert.Equal(expected.ToArray(), callbackOut.ToArray());
    }

    [Fact]
    public void Resparse_SplitsIntoMultipleFiles()
    {
        using var sparseFile = new SparseFile(BlockSize, BlockSize * 100);
        for (int i = 0; i < 5; i++) sparseFile.AddRawChunk(new byte[BlockSize * 20]);
        var parts = sparseFile.Resparse(BlockSize * 50).ToList();
        Assert.True(parts.Count >= 2);
        var combined = new MemoryStream();
        foreach(var p in parts) p.WriteRawToStream(combined);
        Assert.Equal((long)100 * BlockSize, combined.Length);
    }

    [Fact]
    public void GetExportStream_ReturnsSubsetOfData()
    {
        using var sparseFile = new SparseFile(BlockSize, BlockSize * 4);
        var data = Enumerable.Range(0, (int)BlockSize).Select(i => (byte)i).ToArray();
        sparseFile.AddRawChunk(data);
        sparseFile.AddDontCareChunk(BlockSize);

        // export just first block as sparse image
        using var export = sparseFile.GetExportStream(0, 1);
        var imported = SparseFile.FromStream(export);
        Assert.Equal(1u, imported.Header.TotalBlocks);
        Assert.Single(imported.Chunks);

        var readBack = new byte[BlockSize];
        imported.Chunks[0].DataProvider.Read(0, readBack, 0, readBack.Length);
        Assert.Equal(data, readBack);
    }

    [Fact]
    public void GetResparsedStreams_ProducesStreamsForEachSplit()
    {
        using var sparseFile = new SparseFile(BlockSize, BlockSize * 10);
        for(int i=0;i<10;i++) sparseFile.AddRawChunk(new byte[BlockSize]);
        var streams = sparseFile.GetResparsedStreams(BlockSize*30);
        int count=0;
        foreach(var s in streams)
        {
            Assert.True(s.CanRead);
            count++;
        }
        Assert.True(count>=1);
    }

    [Fact]
    public void SparseStream_ReadsRawAndFillAndDontCare()
    {
        using var sparseFile = new SparseFile(BlockSize, BlockSize * 3);
        sparseFile.AddRawChunk(Enumerable.Range(0,(int)BlockSize).Select(i=>(byte)i).ToArray());
        sparseFile.AddFillChunk(0xAABBCCDD, BlockSize);
        sparseFile.AddDontCareChunk(BlockSize);
        using var ss = new SparseStream(sparseFile);
        ss.Position = 0;
        var buf = new byte[BlockSize*3];
        var read = ss.Read(buf,0,buf.Length);
        Assert.Equal(buf.Length, read);
        // fill value is written little-endian; byte offset 3 of the fill block should be 0xAA
        Assert.Equal((byte)0xAA, buf[BlockSize+3]);
    }

    [Fact]
    public void WriteRawToStream_SparseMode_SeeksInsteadOfWrites()
    {
        using var sparseFile = new SparseFile(BlockSize, BlockSize * 2);
        sparseFile.AddDontCareChunk(BlockSize*2);
        using var ms = new MemoryStream();
        sparseFile.WriteRawToStream(ms, sparseMode:true);
        // when writing to a seekable stream the seek may expand length
        Assert.Equal(ms.Position, ms.Length);
        Assert.True(ms.Length >= BlockSize * 2);
    }

    [Fact]
    public void FromBuffer_WhenDontCareChunkHasPayload_ThrowsInvalidDataException()
    {
        using var sparseFile = new SparseFile(BlockSize, BlockSize);
        sparseFile.AddDontCareChunk(BlockSize);

        using var output = new MemoryStream();
        sparseFile.WriteToStream(output, sparse: true, includeCrc: false);
        var bytes = output.ToArray().ToList();

        BinaryPrimitives.WriteUInt32LittleEndian(CollectionsMarshal.AsSpan(bytes).Slice(SparseFormat.SparseHeaderSize + 8, 4), SparseFormat.ChunkHeaderSize + 4u);
        bytes.AddRange(new byte[] { 0xAA, 0xBB, 0xCC, 0xDD });

        Assert.Throws<InvalidDataException>(() => SparseFile.FromBuffer(bytes.ToArray(), validateCrc: false));
    }

    [Fact]
    public void FromBuffer_WhenCrcChunkSizeIsNotFour_ThrowsInvalidDataException()
    {
        using var sparseFile = new SparseFile(BlockSize, BlockSize);
        sparseFile.AddRawChunk(Enumerable.Repeat((byte)0x5A, (int)BlockSize).ToArray());

        using var output = new MemoryStream();
        sparseFile.WriteToStream(output, sparse: true, includeCrc: true);
        var bytes = output.ToArray().ToList();

        var crcHeaderOffset = SparseFormat.SparseHeaderSize + SparseFormat.ChunkHeaderSize + (int)BlockSize;
        BinaryPrimitives.WriteUInt32LittleEndian(CollectionsMarshal.AsSpan(bytes).Slice(crcHeaderOffset + 8, 4), SparseFormat.ChunkHeaderSize + 8u);
        bytes.AddRange(new byte[] { 0x11, 0x22, 0x33, 0x44 });

        Assert.Throws<InvalidDataException>(() => SparseFile.FromBuffer(bytes.ToArray(), validateCrc: false));
    }

    [Fact]
    public void ImportAuto_WithNonSeekableRawStream_DoesNotDropFirstBytes()
    {
        var rawData = Enumerable.Range(0, 5000).Select(i => (byte)(i % 199)).ToArray();
        using var baseStream = new MemoryStream(rawData);
        using var nonSeekable = new NonSeekableReadStream(baseStream);

        using var sparseFile = SparseFile.ImportAuto(nonSeekable);
        using var rawOut = new MemoryStream();
        sparseFile.WriteRawToStream(rawOut);

        var output = rawOut.ToArray();
        Assert.Equal(rawData, output.Take(rawData.Length).ToArray());
    }

    [Fact]
    public void IsSparseImage_WhenOnlyMagicMatchesButHeaderInvalid_ReturnsFalse()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"fks_fake_sparse_{Guid.NewGuid():N}.bin");
        try
        {
            var fakeHeader = new byte[SparseFormat.SparseHeaderSize];
            BinaryPrimitives.WriteUInt32LittleEndian(fakeHeader.AsSpan(0, 4), SparseFormat.SparseHeaderMagic);
            BinaryPrimitives.WriteUInt16LittleEndian(fakeHeader.AsSpan(4, 2), 2); // invalid major version
            File.WriteAllBytes(tempFile, fakeHeader);

            Assert.False(SparseImageValidator.IsSparseImage(tempFile));
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public void ValidateSparseImage_WithCrcChunk_DoesNotFailByHeaderChunkCount()
    {
        using var sparseFile = new SparseFile(BlockSize, BlockSize);
        sparseFile.AddRawChunk(Enumerable.Repeat((byte)0x42, (int)BlockSize).ToArray());

        var tempFile = Path.Combine(Path.GetTempPath(), $"fks_validate_crc_{Guid.NewGuid():N}.simg");
        try
        {
            using (var output = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                sparseFile.WriteToStream(output, sparse: true, includeCrc: true);
            }

            var result = SparseImageValidator.ValidateSparseImage(tempFile);
            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal(1u, result.CalculatedTotalBlocks);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    private sealed class NonSeekableReadStream : Stream
    {
        private readonly Stream _inner;

        public NonSeekableReadStream(Stream inner)
        {
            _inner = inner;
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => _inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
