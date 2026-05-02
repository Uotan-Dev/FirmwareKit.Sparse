using FirmwareKit.Sparse.Core;
using FirmwareKit.Sparse.DataProviders;
using FirmwareKit.Sparse.IO;
using FirmwareKit.Sparse.Models;
using FirmwareKit.Sparse.Utils;
using Xunit;

namespace FirmwareKit.Sparse.Tests;

public class HighPriorityFixTests
{
    private const uint BlockSize = 4096;

    [Fact]
    public void SparseStreamParser_RawChunk_UsesStreamDataProvider_NotMemoryDataProvider()
    {
        using var sparseFile = new SparseFile(BlockSize, BlockSize * 4);
        var rawData = Enumerable.Range(0, (int)BlockSize * 2).Select(i => (byte)(i % 251)).ToArray();
        sparseFile.AddRawChunk(rawData);
        sparseFile.AddFillChunk(0xAABBCCDD, BlockSize);
        sparseFile.AddDontCareChunk(BlockSize);

        using var ms = new MemoryStream();
        sparseFile.WriteToStream(ms, sparse: true, includeCrc: false);
        ms.Position = 0;

        using var parser = new SparseStreamParser(ms, leaveOpen: true);
        var chunks = parser.EnumerateChunks().ToList();

        Assert.NotEmpty(chunks);
        var rawChunk = chunks.First(c => c.Header.ChunkType == (ushort)ChunkType.Raw);
        Assert.NotNull(rawChunk.DataProvider);
        Assert.IsType<StreamDataProvider>(rawChunk.DataProvider);
    }

    [Fact]
    public void SparseStreamParser_RawChunkData_CanBeReadCorrectly()
    {
        var originalData = Enumerable.Range(0, (int)BlockSize * 2).Select(i => (byte)(i % 251)).ToArray();
        using var sparseFile = new SparseFile(BlockSize, BlockSize * 4);
        sparseFile.AddRawChunk(originalData);
        sparseFile.AddFillChunk(0xAABBCCDD, BlockSize);
        sparseFile.AddDontCareChunk(BlockSize);

        using var ms = new MemoryStream();
        sparseFile.WriteToStream(ms, sparse: true, includeCrc: false);
        ms.Position = 0;

        using var parser = new SparseStreamParser(ms, leaveOpen: true);
        var chunks = parser.EnumerateChunks().ToList();

        var rawChunk = chunks.First(c => c.Header.ChunkType == (ushort)ChunkType.Raw);
        Assert.NotNull(rawChunk.DataProvider);

        var readBuffer = new byte[originalData.Length];
        var bytesRead = rawChunk.DataProvider.Read(0, readBuffer, 0, readBuffer.Length);
        Assert.Equal(originalData.Length, bytesRead);
        Assert.Equal(originalData, readBuffer);
    }

    [Fact]
    public void SparseStreamParser_FillChunk_FillValueIsCorrect()
    {
        const uint fillValue = 0xDEADBEEF;
        using var sparseFile = new SparseFile(BlockSize, BlockSize * 3);
        sparseFile.AddFillChunk(fillValue, BlockSize * 3);

        using var ms = new MemoryStream();
        sparseFile.WriteToStream(ms, sparse: true, includeCrc: false);
        ms.Position = 0;

        using var parser = new SparseStreamParser(ms, leaveOpen: true);
        var chunks = parser.EnumerateChunks().ToList();

        var fillChunk = Assert.Single(chunks);
        Assert.Equal(fillValue, fillChunk.FillValue);
    }

    [Fact]
    public void SparseStreamParser_DontCareChunk_NoDataProvider()
    {
        using var sparseFile = new SparseFile(BlockSize, BlockSize * 3);
        sparseFile.AddDontCareChunk(BlockSize * 3);

        using var ms = new MemoryStream();
        sparseFile.WriteToStream(ms, sparse: true, includeCrc: false);
        ms.Position = 0;

        using var parser = new SparseStreamParser(ms, leaveOpen: true);
        var chunks = parser.EnumerateChunks().ToList();

        var dontCareChunk = Assert.Single(chunks);
        Assert.Null(dontCareChunk.DataProvider);
    }

    [Fact]
    public void ExtractValidData_WithArrayPool_ProducesCorrectOutput()
    {
        var tempInput = Path.Combine(Path.GetTempPath(), $"fks_extract_in_{Guid.NewGuid():N}.simg");
        var tempOutput = Path.Combine(Path.GetTempPath(), $"fks_extract_out_{Guid.NewGuid():N}.bin");

        try
        {
            var rawData = Enumerable.Range(0, (int)BlockSize * 3).Select(i => (byte)(i % 251)).ToArray();
            using var sparseFile = new SparseFile(BlockSize, BlockSize * 4);
            sparseFile.AddRawChunk(rawData);
            sparseFile.AddDontCareChunk(BlockSize);

            using (var fs = new FileStream(tempInput, FileMode.Create, FileAccess.Write))
            {
                sparseFile.WriteToStream(fs, sparse: true, includeCrc: false);
            }

            var result = SparseImageUtils.ExtractValidData(tempInput, tempOutput, partitionOffset: 0);

            Assert.True(result.Success, result.ErrorMessage);
            Assert.True(result.DataFound);
            Assert.Equal(rawData.Length, result.TotalBytesExtracted);

            var extractedData = File.ReadAllBytes(tempOutput);
            Assert.Equal(rawData, extractedData);
        }
        finally
        {
            if (File.Exists(tempInput)) File.Delete(tempInput);
            if (File.Exists(tempOutput)) File.Delete(tempOutput);
        }
    }

    [Fact]
    public void ExtractValidData_WithPartitionOffset_ProducesCorrectOutput()
    {
        var tempInput = Path.Combine(Path.GetTempPath(), $"fks_extract_off_in_{Guid.NewGuid():N}.simg");
        var tempOutput = Path.Combine(Path.GetTempPath(), $"fks_extract_off_out_{Guid.NewGuid():N}.bin");

        try
        {
            var rawData = Enumerable.Range(0, (int)BlockSize * 4).Select(i => (byte)(i % 251)).ToArray();
            using var sparseFile = new SparseFile(BlockSize, BlockSize * 4);
            sparseFile.AddRawChunk(rawData);

            using (var fs = new FileStream(tempInput, FileMode.Create, FileAccess.Write))
            {
                sparseFile.WriteToStream(fs, sparse: true, includeCrc: false);
            }

            long partitionOffset = BlockSize;
            var result = SparseImageUtils.ExtractValidData(tempInput, tempOutput, partitionOffset);

            Assert.True(result.Success, result.ErrorMessage);
            Assert.True(result.DataFound);
            Assert.Equal(rawData.Length - partitionOffset, result.TotalBytesExtracted);

            var extractedData = File.ReadAllBytes(tempOutput);
            var expectedData = rawData.Skip((int)partitionOffset).ToArray();
            Assert.Equal(expectedData, extractedData);
        }
        finally
        {
            if (File.Exists(tempInput)) File.Delete(tempInput);
            if (File.Exists(tempOutput)) File.Delete(tempOutput);
        }
    }

    [Fact]
    public async Task WriteToStreamAsync_WithConfigureAwait_CompletesSuccessfully()
    {
        using var sparseFile = new SparseFile(BlockSize, BlockSize * 2);
        var rawData = Enumerable.Range(0, (int)BlockSize * 2).Select(i => (byte)(i % 251)).ToArray();
        sparseFile.AddRawChunk(rawData);

        using var ms = new MemoryStream();
        await sparseFile.WriteToStreamAsync(ms, sparse: true, includeCrc: false);

        ms.Position = 0;
        using var parsed = SparseFile.FromStream(ms);
        Assert.Equal(2u, parsed.Header.TotalBlocks);
    }

    [Fact]
    public async Task WriteRawToStreamAsync_WithConfigureAwait_CompletesSuccessfully()
    {
        using var sparseFile = new SparseFile(BlockSize, BlockSize * 2);
        var rawData = Enumerable.Range(0, (int)BlockSize * 2).Select(i => (byte)(i % 251)).ToArray();
        sparseFile.AddRawChunk(rawData);

        using var ms = new MemoryStream();
        await sparseFile.WriteRawToStreamAsync(ms, sparseMode: false);

        ms.Position = 0;
        var outputData = ms.ToArray();
        Assert.Equal(rawData, outputData);
    }

    [Fact]
    public async Task FromImageFileAsync_WithConfigureAwait_CompletesSuccessfully()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"fks_ca_in_{Guid.NewGuid():N}.simg");

        try
        {
            using var sparseFile = new SparseFile(BlockSize, BlockSize * 2);
            var rawData = Enumerable.Range(0, (int)BlockSize * 2).Select(i => (byte)(i % 251)).ToArray();
            sparseFile.AddRawChunk(rawData);

            using (var fs = new FileStream(tempFile, FileMode.Create, FileAccess.Write))
            {
                sparseFile.WriteToStream(fs, sparse: true, includeCrc: false);
            }

            using var parsed = await SparseFile.FromImageFileAsync(tempFile, validateCrc: false);
            Assert.Equal(2u, parsed.Header.TotalBlocks);
            Assert.Single(parsed.Chunks);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ConvertSparseToRawAsync_WithConfigureAwait_CompletesSuccessfully()
    {
        var tempInput = Path.Combine(Path.GetTempPath(), $"fks_conv_in_{Guid.NewGuid():N}.simg");
        var tempOutput = Path.Combine(Path.GetTempPath(), $"fks_conv_out_{Guid.NewGuid():N}.raw");

        try
        {
            var rawData = Enumerable.Range(0, (int)BlockSize * 2).Select(i => (byte)(i % 251)).ToArray();
            using var sparseFile = new SparseFile(BlockSize, BlockSize * 2);
            sparseFile.AddRawChunk(rawData);

            using (var fs = new FileStream(tempInput, FileMode.Create, FileAccess.Write))
            {
                sparseFile.WriteToStream(fs, sparse: true, includeCrc: false);
            }

            await SparseImageConverter.ConvertSparseToRawAsync(new[] { tempInput }, tempOutput);

            Assert.True(File.Exists(tempOutput));
            var outputData = File.ReadAllBytes(tempOutput);
            Assert.Equal(rawData, outputData);
        }
        finally
        {
            if (File.Exists(tempInput)) File.Delete(tempInput);
            if (File.Exists(tempOutput)) File.Delete(tempOutput);
        }
    }

    [Fact]
    public async Task ConvertRawToSparseAsync_WithConfigureAwait_CompletesSuccessfully()
    {
        var tempInput = Path.Combine(Path.GetTempPath(), $"fks_conv_raw_in_{Guid.NewGuid():N}.bin");
        var tempOutput = Path.Combine(Path.GetTempPath(), $"fks_conv_raw_out_{Guid.NewGuid():N}.simg");

        try
        {
            var rawData = Enumerable.Range(0, (int)BlockSize * 2).Select(i => (byte)(i % 251)).ToArray();
            File.WriteAllBytes(tempInput, rawData);

            await SparseImageConverter.ConvertRawToSparseAsync(tempInput, tempOutput, BlockSize);

            Assert.True(File.Exists(tempOutput));
            using var parsed = SparseFile.FromImageFile(tempOutput);
            Assert.Equal(2u, parsed.Header.TotalBlocks);
        }
        finally
        {
            if (File.Exists(tempInput)) File.Delete(tempInput);
            if (File.Exists(tempOutput)) File.Delete(tempOutput);
        }
    }

    [Fact]
    public async Task FileDataProvider_WriteToAsync_WithConfigureAwait_CompletesSuccessfully()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"fks_fdp_{Guid.NewGuid():N}.bin");
        var testData = Enumerable.Range(0, 5000).Select(i => (byte)(i % 251)).ToArray();

        try
        {
            File.WriteAllBytes(tempFile, testData);
            var provider = new FileDataProvider(tempFile, 100, 2000);

            using var ms = new MemoryStream();
            await provider.WriteToAsync(ms);

            ms.Position = 0;
            var output = ms.ToArray();
            Assert.Equal(2000, output.Length);
            Assert.Equal(testData.Skip(100).Take(2000).ToArray(), output);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task StreamDataProvider_WriteToAsync_WithConfigureAwait_CompletesSuccessfully()
    {
        var testData = Enumerable.Range(0, 5000).Select(i => (byte)(i % 251)).ToArray();
        using var sourceMs = new MemoryStream(testData);
        var provider = new StreamDataProvider(sourceMs, 100, 2000, leaveOpen: true);

        using var ms = new MemoryStream();
        await provider.WriteToAsync(ms);

        ms.Position = 0;
        var output = ms.ToArray();
        Assert.Equal(2000, output.Length);
        Assert.Equal(testData.Skip(100).Take(2000).ToArray(), output);
    }
}
