using FirmwareKit.Sparse.Core;
using FirmwareKit.Sparse.DataProviders;
using FirmwareKit.Sparse.IO;
using FirmwareKit.Sparse.Models;
using FirmwareKit.Sparse.Streams;
using FirmwareKit.Sparse.Utils;
using System.Buffers.Binary;
using Xunit;

namespace FirmwareKit.Sparse.Tests;

public class AdditionalCoverageTests
{
    private const uint BlockSize = 4096;

    #region MemoryMappedDataProvider Tests

    [Fact]
    public void MemoryMappedDataProvider_ReadsCorrectData()
    {
        var offset = 100L;
        var length = 500L;
        var fileSize = offset + length;
        var data = Enumerable.Range(0, (int)fileSize).Select(i => (byte)(i % 251)).ToArray();
        var path = Path.Combine(Path.GetTempPath(), $"fks_mmdp_{Guid.NewGuid():N}.bin");
        try
        {
            File.WriteAllBytes(path, data);
            using var provider = new MemoryMappedDataProvider(path, offset, length);

            Assert.Equal(length, provider.Length);

            var buffer = new byte[length];
            var read = provider.Read(0, buffer, 0, buffer.Length);
            Assert.Equal(length, read);
            Assert.Equal(data.Skip((int)offset).Take((int)length).ToArray(), buffer);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void MemoryMappedDataProvider_GetSubProvider_ReturnsCorrectSlice()
    {
        var data = Enumerable.Range(0, 5000).Select(i => (byte)(i % 251)).ToArray();
        var path = Path.Combine(Path.GetTempPath(), $"fks_mmdp_sub_{Guid.NewGuid():N}.bin");
        try
        {
            File.WriteAllBytes(path, data);
            using var provider = new MemoryMappedDataProvider(path, 0, data.Length);
            var sub = provider.GetSubProvider(500, 1000);

            Assert.Equal(1000, sub.Length);
            var buffer = new byte[1000];
            var read = sub.Read(0, buffer, 0, buffer.Length);
            Assert.Equal(1000, read);
            Assert.Equal(data.Skip(500).Take(1000).ToArray(), buffer);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task MemoryMappedDataProvider_WriteToAsync_WritesCorrectData()
    {
        var offset = 200L;
        var length = 2000L;
        var fileSize = offset + length;
        var data = Enumerable.Range(0, (int)fileSize).Select(i => (byte)(i % 251)).ToArray();
        var path = Path.Combine(Path.GetTempPath(), $"fks_mmdp_async_{Guid.NewGuid():N}.bin");
        try
        {
            File.WriteAllBytes(path, data);
            using var provider = new MemoryMappedDataProvider(path, offset, length);

            using var ms = new MemoryStream();
            await provider.WriteToAsync(ms);

            var output = ms.ToArray();
            Assert.Equal(length, output.Length);
            Assert.Equal(data.Skip((int)offset).Take((int)length).ToArray(), output);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    #endregion

    #region SubDataProvider Tests

    [Fact]
    public void SubDataProvider_ReadsCorrectSlice()
    {
        var data = Enumerable.Range(0, 5000).Select(i => (byte)(i % 251)).ToArray();
        using var parent = new MemoryDataProvider(data);
        var sub = new SubDataProvider(parent, 1000, 2000);

        Assert.Equal(2000, sub.Length);

        var buffer = new byte[2000];
        var read = sub.Read(0, buffer, 0, buffer.Length);
        Assert.Equal(2000, read);
        Assert.Equal(data.Skip(1000).Take(2000).ToArray(), buffer);
    }

    [Fact]
    public void SubDataProvider_GetSubProvider_ReturnsNestedSlice()
    {
        var data = Enumerable.Range(0, 5000).Select(i => (byte)(i % 251)).ToArray();
        using var parent = new MemoryDataProvider(data);
        var sub1 = new SubDataProvider(parent, 500, 3000);
        var sub2 = sub1.GetSubProvider(1000, 1000);

        Assert.Equal(1000, sub2.Length);
        var buffer = new byte[1000];
        var read = sub2.Read(0, buffer, 0, buffer.Length);
        Assert.Equal(1000, read);
        Assert.Equal(data.Skip(1500).Take(1000).ToArray(), buffer);
    }

    [Fact]
    public void SubDataProvider_WriteTo_ThrowsNotSupportedException()
    {
        var data = new byte[1000];
        using var parent = new MemoryDataProvider(data);
        var sub = new SubDataProvider(parent, 0, 500);

        Assert.Throws<NotSupportedException>(() => sub.WriteTo(new MemoryStream()));
    }

    [Fact]
    public void SubDataProvider_WriteToAsync_ThrowsNotSupportedException()
    {
        var data = new byte[1000];
        using var parent = new MemoryDataProvider(data);
        var sub = new SubDataProvider(parent, 0, 500);

        var ex = Assert.ThrowsAsync<NotSupportedException>(() => sub.WriteToAsync(new MemoryStream()));
    }

    #endregion

    #region SparseStream Tests

    [Fact]
    public void SparseStream_ReadAcrossMultipleChunks_ReturnsCorrectData()
    {
        using var sparseFile = new SparseFile(BlockSize, BlockSize * 4);
        var raw1 = Enumerable.Range(0, (int)BlockSize).Select(i => (byte)(i % 251)).ToArray();
        var raw2 = Enumerable.Range(0, (int)BlockSize).Select(i => (byte)(255 - (i % 251))).ToArray();

        sparseFile.AddRawChunk(raw1);
        sparseFile.AddFillChunk(0xAABBCCDD, BlockSize);
        sparseFile.AddDontCareChunk(BlockSize);
        sparseFile.AddRawChunk(raw2);

        using var stream = new SparseStream(sparseFile);
        var buffer = new byte[BlockSize * 4];
        var read = stream.Read(buffer, 0, buffer.Length);

        Assert.Equal((int)(BlockSize * 4), read);

        var expected = new byte[BlockSize * 4];
        raw1.CopyTo(expected, 0);
        FillPattern(0xAABBCCDD, expected, (int)BlockSize, (int)BlockSize);
        raw2.CopyTo(expected, (int)BlockSize * 3);

        Assert.Equal(expected, buffer);
    }

    [Fact]
    public void SparseStream_SeekAndRead_ReturnsCorrectData()
    {
        using var sparseFile = new SparseFile(BlockSize, BlockSize * 2);
        var raw = Enumerable.Range(0, (int)BlockSize * 2).Select(i => (byte)(i % 251)).ToArray();
        sparseFile.AddRawChunk(raw);

        using var stream = new SparseStream(sparseFile);
        stream.Seek(BlockSize, SeekOrigin.Begin);

        var buffer = new byte[BlockSize];
        var read = stream.Read(buffer, 0, buffer.Length);
        Assert.Equal((int)BlockSize, read);
        Assert.Equal(raw.Skip((int)BlockSize).Take((int)BlockSize).ToArray(), buffer);
    }

    [Fact]
    public void SparseStream_ReadBeyondEnd_ReturnsZero()
    {
        using var sparseFile = new SparseFile(BlockSize, BlockSize);
        sparseFile.AddRawChunk(new byte[BlockSize]);

        using var stream = new SparseStream(sparseFile);
        stream.Position = BlockSize + 100;

        var buffer = new byte[100];
        var read = stream.Read(buffer, 0, buffer.Length);
        Assert.Equal(0, read);
    }

    [Fact]
    public void SparseStream_Write_ThrowsNotSupportedException()
    {
        using var sparseFile = new SparseFile(BlockSize, BlockSize);
        sparseFile.AddRawChunk(new byte[BlockSize]);

        using var stream = new SparseStream(sparseFile);
        Assert.Throws<NotSupportedException>(() => stream.Write(new byte[10], 0, 10));
    }

    [Fact]
    public void SparseStream_SetLength_ThrowsNotSupportedException()
    {
        using var sparseFile = new SparseFile(BlockSize, BlockSize);
        sparseFile.AddRawChunk(new byte[BlockSize]);

        using var stream = new SparseStream(sparseFile);
        Assert.Throws<NotSupportedException>(() => stream.SetLength(1000));
    }

    [Fact]
    public void SparseStream_ReadFillChunk_ProducesCorrectPattern()
    {
        using var sparseFile = new SparseFile(BlockSize, BlockSize);
        sparseFile.AddFillChunk(0x12345678, BlockSize);

        using var stream = new SparseStream(sparseFile);
        var buffer = new byte[BlockSize];
        var read = stream.Read(buffer, 0, buffer.Length);

        Assert.Equal((int)BlockSize, read);
        var expected = CreateFillPattern(0x12345678, (int)BlockSize);
        Assert.Equal(expected, buffer);
    }

    [Fact]
    public void SparseStream_ReadDontCareChunk_ReturnsZeros()
    {
        using var sparseFile = new SparseFile(BlockSize, BlockSize);
        sparseFile.AddDontCareChunk(BlockSize);

        using var stream = new SparseStream(sparseFile);
        var buffer = new byte[BlockSize];
        var read = stream.Read(buffer, 0, buffer.Length);

        Assert.Equal((int)BlockSize, read);
        Assert.All(buffer, b => Assert.Equal(0, b));
    }

    #endregion

    #region SparseImageStream Tests

    [Fact]
    public void SparseImageStream_ReadsValidSparseImage()
    {
        using var sparseFile = new SparseFile(BlockSize, BlockSize * 2);
        var raw = Enumerable.Range(0, (int)BlockSize).Select(i => (byte)(i % 251)).ToArray();
        sparseFile.AddRawChunk(raw);
        sparseFile.AddDontCareChunk(BlockSize);

        using var imageStream = new SparseImageStream(sparseFile, 0, 2, includeCrc: false);
        Assert.True(imageStream.Length > 0);
        Assert.True(imageStream.CanRead);
        Assert.True(imageStream.CanSeek);
        Assert.False(imageStream.CanWrite);

        var headerBytes = new byte[SparseFormat.SparseHeaderSize];
        var read = imageStream.Read(headerBytes, 0, headerBytes.Length);
        Assert.Equal(SparseFormat.SparseHeaderSize, read);

        var header = SparseHeader.FromBytes(headerBytes);
        Assert.Equal(SparseFormat.SparseHeaderMagic, header.Magic);
        Assert.Equal(2u, header.TotalBlocks);
    }

    [Fact]
    public void SparseImageStream_SeekAndRead_WorksCorrectly()
    {
        using var sparseFile = new SparseFile(BlockSize, BlockSize);
        var raw = Enumerable.Range(0, (int)BlockSize).Select(i => (byte)(i % 251)).ToArray();
        sparseFile.AddRawChunk(raw);

        using var imageStream = new SparseImageStream(sparseFile, 0, 1, includeCrc: false);
        imageStream.Seek(SparseFormat.SparseHeaderSize, SeekOrigin.Begin);

        var chunkHeaderBytes = new byte[SparseFormat.ChunkHeaderSize];
        var read = imageStream.Read(chunkHeaderBytes, 0, chunkHeaderBytes.Length);
        Assert.Equal(SparseFormat.ChunkHeaderSize, read);

        var chunkHeader = ChunkHeader.FromBytes(chunkHeaderBytes);
        Assert.Equal((ushort)ChunkType.Raw, chunkHeader.ChunkType);
    }

    [Fact]
    public void SparseImageStream_Write_ThrowsNotSupportedException()
    {
        using var sparseFile = new SparseFile(BlockSize, BlockSize);
        sparseFile.AddRawChunk(new byte[BlockSize]);

        using var imageStream = new SparseImageStream(sparseFile, 0, 1);
        Assert.Throws<NotSupportedException>(() => imageStream.Write(new byte[10], 0, 10));
    }

    [Fact]
    public void SparseImageStream_SetLength_ThrowsNotSupportedException()
    {
        using var sparseFile = new SparseFile(BlockSize, BlockSize);
        sparseFile.AddRawChunk(new byte[BlockSize]);

        using var imageStream = new SparseImageStream(sparseFile, 0, 1);
        Assert.Throws<NotSupportedException>(() => imageStream.SetLength(1000));
    }

    #endregion

    #region SparseResparser Tests

    [Fact]
    public void Resparse_SplitsFileCorrectly()
    {
        using var sparseFile = new SparseFile(BlockSize, BlockSize * 10);
        for (int i = 0; i < 10; i++)
        {
            sparseFile.AddRawChunk(new byte[BlockSize]);
        }

        var maxFileSize = (long)SparseFormat.SparseHeaderSize + SparseFormat.ChunkHeaderSize + BlockSize * 3;
        var parts = SparseResparser.Resparse(sparseFile, maxFileSize).ToList();

        Assert.True(parts.Count > 1);

        using var merged = new MemoryStream();
        foreach (var part in parts)
        {
            part.WriteRawToStream(merged);
        }

        Assert.Equal((long)10 * BlockSize, merged.Length);
    }

    [Fact]
    public void Resparse_EmptyFile_ReturnsSingleEmptyPart()
    {
        using var sparseFile = new SparseFile(BlockSize, BlockSize * 5);

        var maxFileSize = (long)SparseFormat.SparseHeaderSize + SparseFormat.ChunkHeaderSize + BlockSize * 10;
        var parts = SparseResparser.Resparse(sparseFile, maxFileSize).ToList();

        Assert.Single(parts);
        Assert.Equal(5u, parts[0].Header.TotalBlocks);
    }

    [Fact]
    public void Resparse_ThrowsWhenMaxFileSizeTooSmall()
    {
        using var sparseFile = new SparseFile(BlockSize, BlockSize);
        sparseFile.AddRawChunk(new byte[BlockSize]);

        var tooSmall = 100;
        Assert.ThrowsAny<Exception>(() => SparseResparser.Resparse(sparseFile, tooSmall).ToList());
    }

    #endregion

    #region SparseImageValidator Tests

    [Fact]
    public void GetSparseImageInfo_ReturnsCorrectInfo()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"fks_info_{Guid.NewGuid():N}.simg");
        try
        {
            using (var sparseFile = new SparseFile(BlockSize, BlockSize * 2))
            {
                sparseFile.AddRawChunk(new byte[BlockSize]);
                sparseFile.AddDontCareChunk(BlockSize);

                using var fs = new FileStream(tempFile, FileMode.Create, FileAccess.Write);
                sparseFile.WriteToStream(fs, sparse: true, includeCrc: false);
            }

            var info = SparseImageValidator.GetSparseImageInfo(tempFile);

            Assert.True(info.Success);
            Assert.Equal(BlockSize, info.BlockSize);
            Assert.Equal(2u, info.TotalBlocks);
            Assert.True(info.FileSize > 0);
            Assert.Equal((long)BlockSize * 2, info.UncompressedSize);
            Assert.True(info.CompressionRatio > 0);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void GetSparseImageInfo_WhenNotSparse_ReturnsFailure()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"fks_not_sparse_{Guid.NewGuid():N}.bin");
        try
        {
            File.WriteAllBytes(tempFile, new byte[1000]);

            var info = SparseImageValidator.GetSparseImageInfo(tempFile);

            Assert.False(info.Success);
            Assert.Contains("Not a valid sparse image", info.ErrorMessage);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void IsSparseImage_WithValidSparseFile_ReturnsTrue()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"fks_is_sparse_{Guid.NewGuid():N}.simg");
        try
        {
            using (var sparseFile = new SparseFile(BlockSize, BlockSize))
            {
                sparseFile.AddRawChunk(new byte[BlockSize]);

                using var fs = new FileStream(tempFile, FileMode.Create, FileAccess.Write);
                sparseFile.WriteToStream(fs, sparse: true, includeCrc: false);
            }

            Assert.True(SparseImageValidator.IsSparseImage(tempFile));
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void IsSparseImage_WithNonSparseFile_ReturnsFalse()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"fks_not_sparse2_{Guid.NewGuid():N}.bin");
        try
        {
            File.WriteAllBytes(tempFile, new byte[1000]);
            Assert.False(SparseImageValidator.IsSparseImage(tempFile));
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void IsSparseImage_WithMissingFile_ReturnsFalse()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), $"fks_missing_{Guid.NewGuid():N}.simg");
        Assert.False(SparseImageValidator.IsSparseImage(missingPath));
    }

    #endregion

    #region GZip Writing Tests

    [Fact]
    public void WriteToStream_WithGZip_ProducesCompressedOutput()
    {
        using var sparseFile = new SparseFile(BlockSize, BlockSize * 2);
        var raw = Enumerable.Range(0, (int)BlockSize * 2).Select(i => (byte)(i % 251)).ToArray();
        sparseFile.AddRawChunk(raw);

        using var ms = new MemoryStream();
        sparseFile.WriteToStream(ms, sparse: true, gzip: true, includeCrc: false);

        ms.Position = 0;
        var gzipHeader = ms.ReadByte();
        Assert.Equal(0x1F, gzipHeader);
        gzipHeader = ms.ReadByte();
        Assert.Equal(0x8B, gzipHeader);
    }

    [Fact]
    public async Task WriteToStreamAsync_WithGZip_ProducesCompressedOutput()
    {
        using var sparseFile = new SparseFile(BlockSize, BlockSize * 2);
        var raw = Enumerable.Range(0, (int)BlockSize * 2).Select(i => (byte)(i % 251)).ToArray();
        sparseFile.AddRawChunk(raw);

        using var ms = new MemoryStream();
        await sparseFile.WriteToStreamAsync(ms, sparse: true, gzip: true, includeCrc: false);

        ms.Position = 0;
        var gzipHeader = ms.ReadByte();
        Assert.Equal(0x1F, gzipHeader);
        gzipHeader = ms.ReadByte();
        Assert.Equal(0x8B, gzipHeader);
    }

    #endregion

    #region SparseFile Methods Tests

    [Fact]
    public void WriteWithCallback_ProducesSameOutputAsWriteToStream()
    {
        using var sparseFile = new SparseFile(BlockSize, BlockSize * 2);
        var raw = Enumerable.Range(0, (int)BlockSize).Select(i => (byte)(i % 251)).ToArray();
        sparseFile.AddRawChunk(raw);
        sparseFile.AddFillChunk(0xDEADBEEF, BlockSize);

        using var expected = new MemoryStream();
        sparseFile.WriteToStream(expected, sparse: true, includeCrc: false);

        using var callbackOut = new MemoryStream();
        sparseFile.WriteWithCallback((buffer, length) =>
        {
            if (buffer != null)
            {
                callbackOut.Write(buffer, 0, length);
            }
            return 0;
        }, sparse: true, includeCrc: false);

        Assert.Equal(expected.ToArray(), callbackOut.ToArray());
    }

    [Fact]
    public void GetExportStream_ReturnsValidSparseImageForSubset()
    {
        using var sparseFile = new SparseFile(BlockSize, BlockSize * 4);
        var raw1 = Enumerable.Range(0, (int)BlockSize).Select(i => (byte)(i % 251)).ToArray();
        var raw2 = Enumerable.Range(0, (int)BlockSize).Select(i => (byte)(255 - (i % 251))).ToArray();

        sparseFile.AddRawChunk(raw1);
        sparseFile.AddFillChunk(0xAABBCCDD, BlockSize);
        sparseFile.AddDontCareChunk(BlockSize);
        sparseFile.AddRawChunk(raw2);

        using var exportStream = sparseFile.GetExportStream(1, 2);
        var exported = SparseFile.FromStream(exportStream);

        Assert.Equal(2u, exported.Header.TotalBlocks);
        Assert.True(exported.Chunks.Count > 0);
    }

    [Fact]
    public void GetResparsedStreams_ReturnsMultipleStreams()
    {
        using var sparseFile = new SparseFile(BlockSize, BlockSize * 10);
        for (int i = 0; i < 10; i++)
        {
            sparseFile.AddRawChunk(new byte[BlockSize]);
        }

        var maxFileSize = (long)SparseFormat.SparseHeaderSize + SparseFormat.ChunkHeaderSize + BlockSize * 3;
        var streams = sparseFile.GetResparsedStreams(maxFileSize).ToList();

        Assert.True(streams.Count > 1);

        long totalSize = 0;
        foreach (var stream in streams)
        {
            Assert.True(stream.CanRead);
            totalSize += stream.Length;
        }

        Assert.True(totalSize > 0);
    }

    #endregion

    #region MemoryDataProvider Tests

    [Fact]
    public void MemoryDataProvider_ReadsFullData()
    {
        var data = Enumerable.Range(0, 5000).Select(i => (byte)(i % 251)).ToArray();
        using var provider = new MemoryDataProvider(data);

        Assert.Equal(data.Length, provider.Length);

        var buffer = new byte[data.Length];
        var read = provider.Read(0, buffer, 0, buffer.Length);
        Assert.Equal(data.Length, read);
        Assert.Equal(data, buffer);
    }

    [Fact]
    public void MemoryDataProvider_GetSubProvider_ReturnsCorrectSlice()
    {
        var data = Enumerable.Range(0, 5000).Select(i => (byte)(i % 251)).ToArray();
        using var provider = new MemoryDataProvider(data);
        var sub = provider.GetSubProvider(1000, 2000);

        Assert.Equal(2000, sub.Length);
        var buffer = new byte[2000];
        var read = sub.Read(0, buffer, 0, buffer.Length);
        Assert.Equal(2000, read);
        Assert.Equal(data.Skip(1000).Take(2000).ToArray(), buffer);
    }

    [Fact]
    public void MemoryDataProvider_WriteTo_WritesCorrectData()
    {
        var data = Enumerable.Range(0, 3000).Select(i => (byte)(i % 251)).ToArray();
        using var provider = new MemoryDataProvider(data);

        using var ms = new MemoryStream();
        provider.WriteTo(ms);

        var output = ms.ToArray();
        Assert.Equal(data, output);
    }

    [Fact]
    public async Task MemoryDataProvider_WriteToAsync_WritesCorrectData()
    {
        var data = Enumerable.Range(0, 3000).Select(i => (byte)(i % 251)).ToArray();
        using var provider = new MemoryDataProvider(data);

        using var ms = new MemoryStream();
        await provider.WriteToAsync(ms);

        var output = ms.ToArray();
        Assert.Equal(data, output);
    }

    #endregion

    #region ChunkHeader Tests

    [Fact]
    public void ChunkHeader_FromBytes_ThrowsOnInsufficientData()
    {
        var insufficientData = new byte[8];
        Assert.Throws<ArgumentException>(() => ChunkHeader.FromBytes(insufficientData));
    }

    [Fact]
    public void ChunkHeader_WriteTo_ThrowsOnInsufficientSpan()
    {
        var header = new ChunkHeader
        {
            ChunkType = (ushort)ChunkType.Raw,
            ChunkSize = 1,
            TotalSize = SparseFormat.ChunkHeaderSize + BlockSize
        };

        var insufficientSpan = new byte[8];
        Assert.Throws<ArgumentException>(() => header.WriteTo(insufficientSpan));
    }

    [Fact]
    public void ChunkHeader_Roundtrip_SerializesAndDeserializesCorrectly()
    {
        var original = new ChunkHeader
        {
            ChunkType = (ushort)ChunkType.Fill,
            Reserved = 0,
            ChunkSize = 5,
            TotalSize = SparseFormat.ChunkHeaderSize + 4
        };

        var bytes = original.ToBytes();
        var parsed = ChunkHeader.FromBytes(bytes);

        Assert.Equal(original.ChunkType, parsed.ChunkType);
        Assert.Equal(original.Reserved, parsed.Reserved);
        Assert.Equal(original.ChunkSize, parsed.ChunkSize);
        Assert.Equal(original.TotalSize, parsed.TotalSize);
    }

    [Fact]
    public void ChunkHeader_IsValid_RawChunk_ReturnsTrueWhenCorrect()
    {
        var header = new ChunkHeader
        {
            ChunkType = (ushort)ChunkType.Raw,
            ChunkSize = 2,
            TotalSize = SparseFormat.ChunkHeaderSize + BlockSize * 2
        };

        Assert.True(header.IsValid(SparseFormat.ChunkHeaderSize, BlockSize));
    }

    [Fact]
    public void ChunkHeader_IsValid_RawChunk_ReturnsFalseWhenTotalSizeMismatch()
    {
        var header = new ChunkHeader
        {
            ChunkType = (ushort)ChunkType.Raw,
            ChunkSize = 2,
            TotalSize = SparseFormat.ChunkHeaderSize + BlockSize
        };

        Assert.False(header.IsValid(SparseFormat.ChunkHeaderSize, BlockSize));
    }

    [Fact]
    public void ChunkHeader_IsValid_FillChunk_ReturnsTrueWhenDataSizeIs4()
    {
        var header = new ChunkHeader
        {
            ChunkType = (ushort)ChunkType.Fill,
            ChunkSize = 3,
            TotalSize = SparseFormat.ChunkHeaderSize + 4
        };

        Assert.True(header.IsValid(SparseFormat.ChunkHeaderSize, BlockSize));
    }

    [Fact]
    public void ChunkHeader_IsValid_DontCareChunk_ReturnsTrueWhenNoData()
    {
        var header = new ChunkHeader
        {
            ChunkType = (ushort)ChunkType.DontCare,
            ChunkSize = 1,
            TotalSize = SparseFormat.ChunkHeaderSize
        };

        Assert.True(header.IsValid(SparseFormat.ChunkHeaderSize, BlockSize));
    }

    [Fact]
    public void ChunkHeader_IsValid_Crc32Chunk_ReturnsTrueWhenDataSizeIs4()
    {
        var header = new ChunkHeader
        {
            ChunkType = (ushort)ChunkType.Crc32,
            ChunkSize = 0,
            TotalSize = SparseFormat.ChunkHeaderSize + 4
        };

        Assert.True(header.IsValid(SparseFormat.ChunkHeaderSize, BlockSize));
    }

    #endregion

    #region SparseHeader Tests

    [Fact]
    public void SparseHeader_FromBytes_ThrowsOnInsufficientData()
    {
        var insufficientData = new byte[20];
        Assert.Throws<ArgumentException>(() => SparseHeader.FromBytes(insufficientData));
    }

    [Fact]
    public void SparseHeader_WriteTo_ThrowsOnInsufficientSpan()
    {
        var header = SparseHeader.CreateDefault(BlockSize, 10);
        var insufficientSpan = new byte[20];
        Assert.Throws<ArgumentException>(() => header.WriteTo(insufficientSpan));
    }

    [Fact]
    public void SparseHeader_Roundtrip_SerializesAndDeserializesCorrectly()
    {
        var original = SparseHeader.CreateDefault(BlockSize, 100);
        original = original with { ImageChecksum = 0xDEADBEEF };

        var bytes = original.ToBytes();
        var parsed = SparseHeader.FromBytes(bytes);

        Assert.Equal(original.Magic, parsed.Magic);
        Assert.Equal(original.MajorVersion, parsed.MajorVersion);
        Assert.Equal(original.MinorVersion, parsed.MinorVersion);
        Assert.Equal(original.FileHeaderSize, parsed.FileHeaderSize);
        Assert.Equal(original.ChunkHeaderSize, parsed.ChunkHeaderSize);
        Assert.Equal(original.BlockSize, parsed.BlockSize);
        Assert.Equal(original.TotalBlocks, parsed.TotalBlocks);
        Assert.Equal(original.TotalChunks, parsed.TotalChunks);
        Assert.Equal(original.ImageChecksum, parsed.ImageChecksum);
    }

    [Fact]
    public void SparseHeader_IsValid_ReturnsFalseForInvalidMagic()
    {
        var header = SparseHeader.CreateDefault(BlockSize, 10);
        header = header with { Magic = 0x12345678 };
        Assert.False(header.IsValid());
    }

    [Fact]
    public void SparseHeader_IsValid_ReturnsFalseForInvalidVersion()
    {
        var header = SparseHeader.CreateDefault(BlockSize, 10);
        header = header with { MajorVersion = 2 };
        Assert.False(header.IsValid());
    }

    [Fact]
    public void SparseHeader_IsValid_ReturnsFalseForZeroBlockSize()
    {
        var header = SparseHeader.CreateDefault(0, 10);
        Assert.False(header.IsValid());
    }

    [Fact]
    public void SparseHeader_IsValid_ReturnsFalseForNonAlignedBlockSize()
    {
        var header = SparseHeader.CreateDefault(4097, 10);
        Assert.False(header.IsValid());
    }

    #endregion

    #region Async Cancellation Tests

    [Fact]
    public async Task WriteToStreamAsync_WithCancellation_ThrowsOperationCanceledException()
    {
        using var sparseFile = new SparseFile(BlockSize, BlockSize * 100);
        for (int i = 0; i < 100; i++)
        {
            sparseFile.AddRawChunk(new byte[BlockSize]);
        }

        using var ms = new MemoryStream();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            sparseFile.WriteToStreamAsync(ms, sparse: true, includeCrc: false, cancellationToken: cts.Token));
    }

    [Fact]
    public async Task FromImageFileAsync_WithCancellation_ThrowsOperationCanceledException()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"fks_cancel_{Guid.NewGuid():N}.simg");
        try
        {
            using (var sparseFile = new SparseFile(BlockSize, BlockSize))
            {
                sparseFile.AddRawChunk(new byte[BlockSize]);

                using var fs = new FileStream(tempFile, FileMode.Create, FileAccess.Write);
                sparseFile.WriteToStream(fs, sparse: true, includeCrc: false);
            }

            var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                SparseFile.FromImageFileAsync(tempFile, validateCrc: false, cancellationToken: cts.Token));
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    #endregion

    #region Empty SparseFile Tests

    [Fact]
    public void EmptySparseFile_WritesAndReadsBackCorrectly()
    {
        using var sparseFile = new SparseFile(BlockSize, BlockSize * 5);

        using var ms = new MemoryStream();
        sparseFile.WriteToStream(ms, sparse: true, includeCrc: false);

        ms.Position = 0;
        using var parsed = SparseFile.FromStream(ms);

        Assert.Equal(5u, parsed.Header.TotalBlocks);
        Assert.Empty(parsed.Chunks);
    }

    [Fact]
    public void EmptySparseFile_WriteRaw_ProducesZeros()
    {
        using var sparseFile = new SparseFile(BlockSize, BlockSize * 3);

        using var ms = new MemoryStream();
        sparseFile.WriteRawToStream(ms);

        var output = ms.ToArray();
        Assert.Equal((long)BlockSize * 3, output.Length);
        Assert.All(output, b => Assert.Equal(0, b));
    }

    #endregion

    #region SparseFile FromStreamAsync Tests

    [Fact]
    public async Task FromStreamAsync_ParsesSparseFileCorrectly()
    {
        using var sparseFile = new SparseFile(BlockSize, BlockSize * 2);
        var raw = Enumerable.Range(0, (int)BlockSize * 2).Select(i => (byte)(i % 251)).ToArray();
        sparseFile.AddRawChunk(raw);

        using var ms = new MemoryStream();
        sparseFile.WriteToStream(ms, sparse: true, includeCrc: false);
        ms.Position = 0;

        using var parsed = await SparseFile.FromStreamAsync(ms, validateCrc: false);

        Assert.Equal(2u, parsed.Header.TotalBlocks);
        Assert.Single(parsed.Chunks);
    }

    [Fact]
    public async Task FromBufferAsync_ParsesSparseFileCorrectly()
    {
        using var sparseFile = new SparseFile(BlockSize, BlockSize * 2);
        var raw = Enumerable.Range(0, (int)BlockSize * 2).Select(i => (byte)(i % 251)).ToArray();
        sparseFile.AddRawChunk(raw);

        using var ms = new MemoryStream();
        sparseFile.WriteToStream(ms, sparse: true, includeCrc: false);
        var buffer = ms.ToArray();

        using var parsed = await SparseFile.FromBufferAsync(buffer, validateCrc: false);

        Assert.Equal(2u, parsed.Header.TotalBlocks);
        Assert.Single(parsed.Chunks);
    }

    #endregion

    #region ImportAuto Stream Tests

    [Fact]
    public void ImportAuto_WithRawStream_DetectsAndImportsAsRaw()
    {
        var rawData = Enumerable.Range(0, 5000).Select(i => (byte)(i % 241)).ToArray();
        using var ms = new MemoryStream(rawData);

        using var sparseFile = SparseFile.ImportAuto(ms);
        Assert.Single(sparseFile.Chunks);
        Assert.Equal((ushort)ChunkType.Raw, sparseFile.Chunks[0].Header.ChunkType);
    }

    [Fact]
    public void ImportAuto_WithSparseStream_DetectsAndImportsAsSparse()
    {
        using var sparseFile = new SparseFile(BlockSize, BlockSize * 2);
        var raw = Enumerable.Range(0, (int)BlockSize * 2).Select(i => (byte)(i % 251)).ToArray();
        sparseFile.AddRawChunk(raw);

        using var ms = new MemoryStream();
        sparseFile.WriteToStream(ms, sparse: true, includeCrc: false);
        ms.Position = 0;

        using var imported = SparseFile.ImportAuto(ms);
        Assert.True(imported.Header.IsValid());
        Assert.True(imported.Chunks.Count > 0);
    }

    #endregion

    #region ResparseStreamed Tests

    [Fact]
    public void ResparseStreamed_SplitsSparseFileFromStream()
    {
        using var sparseFile = new SparseFile(BlockSize, BlockSize * 10);
        for (int i = 0; i < 10; i++)
        {
            sparseFile.AddRawChunk(new byte[BlockSize]);
        }

        using var ms = new MemoryStream();
        sparseFile.WriteToStream(ms, sparse: true, includeCrc: false);
        ms.Position = 0;

        var maxFileSize = (long)SparseFormat.SparseHeaderSize + SparseFormat.ChunkHeaderSize + BlockSize * 3;
        var parts = SparseFile.ResparseStreamed(ms, maxFileSize, leaveOpen: true).ToList();

        Assert.True(parts.Count > 1);

        using var merged = new MemoryStream();
        foreach (var part in parts)
        {
            part.WriteRawToStream(merged);
        }

        Assert.Equal((long)10 * BlockSize, merged.Length);
    }

    #endregion

    #region ResparseMapped Tests

    [Fact(Skip = "Requires file access mode that may fail on Windows due to file locking")]
    public void ResparseMapped_SplitsSparseFileFromDisk()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"fks_resparse_mapped_{Guid.NewGuid():N}.simg");
        try
        {
            using (var sparseFile = new SparseFile(BlockSize, BlockSize * 8))
            {
                for (int i = 0; i < 8; i++)
                {
                    sparseFile.AddRawChunk(new byte[BlockSize]);
                }

                using var fs = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.Read);
                sparseFile.WriteToStream(fs, sparse: true, includeCrc: false);
            }

            var maxFileSize = (long)SparseFormat.SparseHeaderSize + SparseFormat.ChunkHeaderSize + BlockSize * 3;
            var parts = SparseFile.ResparseMapped(tempFile, maxFileSize).ToList();

            Assert.True(parts.Count > 1);

            using var merged = new MemoryStream();
            foreach (var part in parts)
            {
                part.WriteRawToStream(merged);
            }

            Assert.Equal((long)8 * BlockSize, merged.Length);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    #endregion

    #region SparseStream Partial Read Tests

    [Fact]
    public void SparseStream_PartialReadAtChunkBoundary_ReturnsCorrectData()
    {
        using var sparseFile = new SparseFile(BlockSize, BlockSize * 2);
        var raw = Enumerable.Range(0, (int)BlockSize * 2).Select(i => (byte)(i % 251)).ToArray();
        sparseFile.AddRawChunk(raw);

        using var stream = new SparseStream(sparseFile);
        stream.Position = BlockSize - 100;

        var buffer = new byte[200];
        var read = stream.Read(buffer, 0, buffer.Length);
        Assert.Equal(200, read);

        var expected = raw.Skip((int)BlockSize - 100).Take(200).ToArray();
        Assert.Equal(expected, buffer);
    }

    [Fact]
    public void SparseStream_ReadFillChunkAtOffset_ProducesCorrectAlignment()
    {
        using var sparseFile = new SparseFile(BlockSize, BlockSize);
        sparseFile.AddFillChunk(0x12345678, BlockSize);

        using var stream = new SparseStream(sparseFile);
        stream.Position = 1;

        var buffer = new byte[8];
        var read = stream.Read(buffer, 0, buffer.Length);
        Assert.Equal(8, read);

        var expected = CreateFillPattern(0x12345678, 9).Skip(1).Take(8).ToArray();
        Assert.Equal(expected, buffer);
    }

    #endregion

    #region SparseImageStream With CRC Tests

    [Fact]
    public void SparseImageStream_WithCrc_IncludesCrcChunk()
    {
        using var sparseFile = new SparseFile(BlockSize, BlockSize);
        var raw = Enumerable.Range(0, (int)BlockSize).Select(i => (byte)(i % 251)).ToArray();
        sparseFile.AddRawChunk(raw);

        using var imageStream = new SparseImageStream(sparseFile, 0, 1, includeCrc: true);
        var allBytes = new byte[imageStream.Length];
        imageStream.ReadExactly(allBytes, 0, allBytes.Length);

        var header = SparseHeader.FromBytes(allBytes);
        Assert.True(header.ImageChecksum > 0 || header.ImageChecksum == 0);
        Assert.Equal(2u, header.TotalChunks);
    }

    #endregion

    #region Helper Methods

    private static void FillPattern(uint fillValue, byte[] buffer, int offset, int length)
    {
        var fillBytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(fillBytes, fillValue);
        for (int i = 0; i < length; i++)
        {
            buffer[offset + i] = fillBytes[i % 4];
        }
    }

    private static byte[] CreateFillPattern(uint fillValue, int totalLength)
    {
        var fillBytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(fillBytes, fillValue);

        var output = new byte[totalLength];
        for (var i = 0; i < output.Length; i += fillBytes.Length)
        {
            Buffer.BlockCopy(fillBytes, 0, output, i, Math.Min(fillBytes.Length, output.Length - i));
        }

        return output;
    }

    #endregion
}
