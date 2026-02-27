namespace FirmwareKit.Sparse.IO;

using FirmwareKit.Sparse.Core;
using FirmwareKit.Sparse.Models;
using FirmwareKit.Sparse.DataProviders;
using FirmwareKit.Sparse.Utils;
using System.Buffers.Binary;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Buffers;
using System.Runtime.InteropServices;

/// <summary>
/// Provides methods for reading and importing sparse image data.
/// </summary>
public static class SparseReader
{
    /// <summary>
    /// Peeks at the sparse header of a file without reading the entire content.
    /// </summary>
    /// <param name="filePath">The path to the sparse image file.</param>
    /// <returns>A <see cref="SparseHeader"/> containing the metadata of the sparse image.</returns>
    public static SparseHeader PeekHeader(string filePath)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        Span<byte> headerData = stackalloc byte[SparseFormat.SparseHeaderSize];
        stream.ReadExactly(headerData);
        return SparseHeader.FromBytes(headerData);
    }

    /// <summary>
    /// Loads a sparse file from the specified stream.
    /// </summary>
    public static SparseFile FromStream(Stream stream, bool validateCrc = false, bool verbose = false, ISparseLogger? logger = null)
    {
        return FromStreamInternal(stream, null, validateCrc, verbose, logger);
    }

    /// <summary>
    /// Loads a sparse file from a byte array buffer.
    /// </summary>
    public static SparseFile FromBuffer(byte[] buffer, bool validateCrc = false, bool verbose = false, ISparseLogger? logger = null)
    {
        using var ms = new MemoryStream(buffer);
        return FromStream(ms, validateCrc, verbose, logger);
    }

    /// <summary>
    /// Loads a sparse file from the specified image file.
    /// </summary>
    public static SparseFile FromImageFile(string filePath, bool validateCrc = false, bool verbose = false, ISparseLogger? logger = null)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);
        return FromStreamInternal(stream, filePath, validateCrc, verbose, logger);
    }

    internal static SparseFile FromStreamInternal(Stream stream, string? filePath, bool validateCrc, bool verbose, ISparseLogger? logger)
    {
        var sparseFile = new SparseFile { Verbose = verbose, Logger = logger };
        var activeLogger = logger ?? SparseLogger.Instance;

        Span<byte> headerData = stackalloc byte[SparseFormat.SparseHeaderSize];
        stream.ReadExactly(headerData);

        sparseFile.Header = SparseHeader.FromBytes(headerData);

        if (verbose)
        {
            activeLogger.LogInformation($"Parsing Sparse image header: BlockSize={sparseFile.Header.BlockSize}, TotalBlocks={sparseFile.Header.TotalBlocks}, TotalChunks={sparseFile.Header.TotalChunks}");
        }

        if (!sparseFile.Header.IsValid())
        {
            throw new InvalidDataException("Invalid sparse header");
        }

        if (sparseFile.Header.ChunkHeaderSize > SparseFormat.ChunkHeaderSize)
        {
            stream.Seek(sparseFile.Header.ChunkHeaderSize - SparseFormat.ChunkHeaderSize, SeekOrigin.Current);
        }

        uint? checksum = validateCrc ? Crc32.Begin() : null;
        byte[]? buffer = validateCrc ? ArrayPool<byte>.Shared.Rent(1024 * 1024) : null;
        try
        {
            uint currentBlock = 0;

            Span<byte> chunkHeaderData = stackalloc byte[SparseFormat.ChunkHeaderSize];
            Span<byte> buffer4 = stackalloc byte[4];
            for (uint i = 0; i < sparseFile.Header.TotalChunks; i++)
            {
                stream.ReadExactly(chunkHeaderData);

                var chunkHeader = ChunkHeader.FromBytes(chunkHeaderData);

                if (verbose)
                {
                    activeLogger.LogInformation($"Chunk #{i}: Type=0x{chunkHeader.ChunkType:X4}, Size={chunkHeader.ChunkSize} blocks, Total Size={chunkHeader.TotalSize}");
                }

                if (sparseFile.Header.ChunkHeaderSize > SparseFormat.ChunkHeaderSize)
                {
                    stream.Seek(sparseFile.Header.ChunkHeaderSize - SparseFormat.ChunkHeaderSize, SeekOrigin.Current);
                }

                var chunk = new SparseChunk(chunkHeader) { StartBlock = currentBlock };

                if (!chunkHeader.IsValid())
                {
                    throw new InvalidDataException($"Invalid chunk header for chunk {i}: Type 0x{chunkHeader.ChunkType:X4}");
                }

                var dataSize = (long)chunkHeader.TotalSize - sparseFile.Header.ChunkHeaderSize;
                var expectedRawSize = (long)chunkHeader.ChunkSize * sparseFile.Header.BlockSize;

                switch (chunkHeader.ChunkType)
                {
                    case (ushort)ChunkType.Raw:
                        if (dataSize != expectedRawSize)
                        {
                            throw new InvalidDataException($"Total size ({chunkHeader.TotalSize}) for RAW chunk {i} does not match expected data size ({expectedRawSize})");
                        }

                        if (validateCrc && buffer != null && checksum.HasValue)
                        {
                            var dataOffset = stream.Position;
                            if (filePath != null)
                            {
                                var remaining = dataSize;
                                while (remaining > 0)
                                {
                                    var toRead = (int)Math.Min(buffer.Length, remaining);
                                    stream.ReadExactly(buffer, 0, toRead);
                                    checksum = Crc32.Update(checksum.Value, buffer, 0, toRead);
                                    remaining -= toRead;
                                }
                                chunk.DataProvider = new FileDataProvider(filePath, dataOffset, dataSize);
                            }
                            else if (stream.CanSeek)
                            {
                                var remaining = dataSize;
                                while (remaining > 0)
                                {
                                    var toRead = (int)Math.Min(buffer.Length, remaining);
                                    stream.ReadExactly(buffer, 0, toRead);
                                    checksum = Crc32.Update(checksum.Value, buffer, 0, toRead);
                                    remaining -= toRead;
                                }
                                chunk.DataProvider = new StreamDataProvider(stream, dataOffset, dataSize, true);
                            }
                            else
                            {
                                if (dataSize > int.MaxValue)
                                {
                                    throw new NotSupportedException($"Raw data for chunk {i} is too large ({dataSize} bytes), exceeding memory buffer limits.");
                                }
                                var rawData = new byte[dataSize];
                                stream.ReadExactly(rawData);
                                checksum = Crc32.Update(checksum.Value, rawData);
                                chunk.DataProvider = new MemoryDataProvider(rawData);
                            }
                        }
                        else if (filePath != null)
                        {
                            chunk.DataProvider = new FileDataProvider(filePath, stream.Position, dataSize);
                            stream.Seek(dataSize, SeekOrigin.Current);
                        }
                        else if (stream.CanSeek)
                        {
                            chunk.DataProvider = new StreamDataProvider(stream, stream.Position, dataSize, true);
                            stream.Seek(dataSize, SeekOrigin.Current);
                        }
                        else
                        {
                            if (dataSize > int.MaxValue)
                            {
                                throw new NotSupportedException($"Raw data for chunk {i} is too large ({dataSize} bytes), exceeding memory buffer limits.");
                            }
                            var rawData = new byte[dataSize];
                            stream.ReadExactly(rawData);
                            chunk.DataProvider = new MemoryDataProvider(rawData);
                        }
                        break;

                    case (ushort)ChunkType.Fill:
                        if (dataSize < 4)
                        {
                            throw new InvalidDataException($"Data size ({dataSize}) for FILL chunk {i} is less than 4 bytes");
                        }

                        stream.ReadExactly(buffer4);

                        chunk.FillValue = BinaryPrimitives.ReadUInt32LittleEndian(buffer4);

                        if (validateCrc && checksum.HasValue)
                        {
                            checksum = Crc32.UpdateRepeated(checksum.Value, chunk.FillValue, expectedRawSize);
                        }

                        if (dataSize > 4)
                        {
                            stream.Seek(dataSize - 4, SeekOrigin.Current);
                        }
                        break;

                    case (ushort)ChunkType.DontCare:
                        if (validateCrc && checksum.HasValue)
                        {
                            checksum = Crc32.UpdateZero(checksum.Value, expectedRawSize);
                        }
                        if (dataSize > 0)
                        {
                            stream.Seek(dataSize, SeekOrigin.Current);
                        }
                        break;

                    case (ushort)ChunkType.Crc32:
                        if (dataSize >= 4)
                        {
                            if (stream.Read(buffer4) != 4)
                            {
                                throw new InvalidDataException($"Failed to read CRC32 value for chunk {i}");
                            }
                            var fileCrc = BinaryPrimitives.ReadUInt32LittleEndian(buffer4);
                            if (validateCrc && checksum.HasValue && fileCrc != Crc32.Finish(checksum.Value))
                            {
                                throw new InvalidDataException($"CRC32 checksum mismatch: file has 0x{fileCrc:X8}, computed 0x{Crc32.Finish(checksum.Value):X8}");
                            }
                            if (dataSize > 4)
                            {
                                stream.Seek(dataSize - 4, SeekOrigin.Current);
                            }
                        }
                        break;

                    default:
                        throw new InvalidDataException($"Unknown chunk type for chunk {i}: 0x{chunkHeader.ChunkType:X4}");
                }

                if (chunkHeader.ChunkType != (ushort)ChunkType.Crc32)
                {
                    sparseFile.AddChunkRaw(chunk);
                    currentBlock += chunkHeader.ChunkSize;
                }
            }

            if (verbose)
            {
                activeLogger.LogInformation($"Sparse image parsing completed: {sparseFile.Chunks.Count} chunks, {currentBlock} blocks total");
            }

            if (sparseFile.Header.TotalBlocks != currentBlock)
            {
                throw new InvalidDataException($"Block count mismatch: Sparse header expects {sparseFile.Header.TotalBlocks} blocks, but parsed {currentBlock}");
            }

            return sparseFile;
        }
        finally
        {
            if (buffer != null)
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }

    /// <summary>
    /// Loads a sparse file from the specified stream asynchronously.
    /// </summary>
    public static Task<SparseFile> FromStreamAsync(Stream stream, bool validateCrc = false, bool verbose = false, ISparseLogger? logger = null, CancellationToken cancellationToken = default)
    {
        return FromStreamInternalAsync(stream, null, validateCrc, verbose, logger, cancellationToken);
    }

    /// <summary>
    /// Loads a sparse file from a byte array buffer asynchronously.
    /// </summary>
    public static async Task<SparseFile> FromBufferAsync(byte[] buffer, bool validateCrc = false, bool verbose = false, ISparseLogger? logger = null, CancellationToken cancellationToken = default)
    {
        using var ms = new MemoryStream(buffer);
        return await FromStreamAsync(ms, validateCrc, verbose, logger, cancellationToken);
    }

    /// <summary>
    /// Loads a sparse file from the specified image file asynchronously.
    /// </summary>
    public static async Task<SparseFile> FromImageFileAsync(string filePath, bool validateCrc = false, bool verbose = false, ISparseLogger? logger = null, CancellationToken cancellationToken = default)
    {
#if NET6_0_OR_GREATER
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
#else
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
#endif
        return await FromStreamInternalAsync(stream, filePath, validateCrc, verbose, logger, cancellationToken);
    }

    internal static async Task<SparseFile> FromStreamInternalAsync(Stream stream, string? filePath, bool validateCrc, bool verbose, ISparseLogger? logger, CancellationToken cancellationToken)
    {
        var sparseFile = new SparseFile { Verbose = verbose, Logger = logger };
        var activeLogger = logger ?? SparseLogger.Instance;

        var headerData = new byte[SparseFormat.SparseHeaderSize];
        await ReadExactlyAsync(stream, headerData, 0, SparseFormat.SparseHeaderSize, cancellationToken);

        sparseFile.Header = SparseHeader.FromBytes(headerData);

        if (verbose)
        {
            activeLogger.LogInformation($"Parsing Sparse image header: BlockSize={sparseFile.Header.BlockSize}, TotalBlocks={sparseFile.Header.TotalBlocks}, TotalChunks={sparseFile.Header.TotalChunks}");
        }

        if (!sparseFile.Header.IsValid())
        {
            throw new InvalidDataException("Invalid sparse header");
        }

        if (sparseFile.Header.FileHeaderSize > SparseFormat.SparseHeaderSize)
        {
            stream.Seek(sparseFile.Header.FileHeaderSize - SparseFormat.SparseHeaderSize, SeekOrigin.Current);
        }

        var checksum = validateCrc ? Crc32.Begin() : (uint?)null;
        var buffer = validateCrc ? ArrayPool<byte>.Shared.Rent(1024 * 1024) : null;

        try
        {
            uint currentBlock = 0;

            var chunkHeaderData = new byte[SparseFormat.ChunkHeaderSize];
            var buffer4 = new byte[4];
            for (uint i = 0; i < sparseFile.Header.TotalChunks; i++)
            {
                await ReadExactlyAsync(stream, chunkHeaderData, 0, SparseFormat.ChunkHeaderSize, cancellationToken);

                var chunkHeader = ChunkHeader.FromBytes(chunkHeaderData);

                if (verbose)
                {
                    activeLogger.LogInformation($"Chunk #{i}: Type=0x{chunkHeader.ChunkType:X4}, Size={chunkHeader.ChunkSize} blocks, Total Size={chunkHeader.TotalSize}");
                }

                if (sparseFile.Header.ChunkHeaderSize > SparseFormat.ChunkHeaderSize)
                {
                    stream.Seek(sparseFile.Header.ChunkHeaderSize - SparseFormat.ChunkHeaderSize, SeekOrigin.Current);
                }

                var chunk = new SparseChunk(chunkHeader) { StartBlock = currentBlock };

                if (!chunkHeader.IsValid())
                {
                    throw new InvalidDataException($"Invalid chunk header for chunk {i}: Type 0x{chunkHeader.ChunkType:X4}");
                }

                var dataSize = (long)chunkHeader.TotalSize - sparseFile.Header.ChunkHeaderSize;
                var expectedRawSize = (long)chunkHeader.ChunkSize * sparseFile.Header.BlockSize;

                switch (chunkHeader.ChunkType)
                {
                    case (ushort)ChunkType.Raw:
                        if (dataSize != expectedRawSize)
                        {
                            throw new InvalidDataException($"Total size ({chunkHeader.TotalSize}) for RAW chunk {i} does not match expected data size ({expectedRawSize})");
                        }

                        if (validateCrc && buffer != null && checksum.HasValue)
                        {
                            var dataOffset = stream.Position;
                            if (filePath != null)
                            {
                                var remaining = dataSize;
                                while (remaining > 0)
                                {
                                    var toRead = (int)Math.Min(buffer.Length, remaining);
                                    await ReadExactlyAsync(stream, buffer, 0, toRead, cancellationToken);
                                    checksum = Crc32.Update(checksum.Value, buffer, 0, toRead);
                                    remaining -= toRead;
                                }
                                chunk.DataProvider = new FileDataProvider(filePath, dataOffset, dataSize);
                            }
                            else if (stream.CanSeek)
                            {
                                var remaining = dataSize;
                                while (remaining > 0)
                                {
                                    var toRead = (int)Math.Min(buffer.Length, remaining);
                                    await ReadExactlyAsync(stream, buffer, 0, toRead, cancellationToken);
                                    checksum = Crc32.Update(checksum.Value, buffer, 0, toRead);
                                    remaining -= toRead;
                                }
                                chunk.DataProvider = new StreamDataProvider(stream, dataOffset, dataSize, true);
                            }
                            else
                            {
                                if (dataSize > int.MaxValue)
                                {
                                    throw new NotSupportedException($"Raw data for chunk {i} is too large ({dataSize} bytes), exceeding memory buffer limits.");
                                }
                                var rawData = new byte[dataSize];
                                await ReadExactlyAsync(stream, rawData, 0, (int)dataSize, cancellationToken);
                                checksum = Crc32.Update(checksum.Value, rawData);
                                chunk.DataProvider = new MemoryDataProvider(rawData);
                            }
                        }
                        else if (filePath != null)
                        {
                            chunk.DataProvider = new FileDataProvider(filePath, stream.Position, dataSize);
                            stream.Seek(dataSize, SeekOrigin.Current);
                        }
                        else if (stream.CanSeek)
                        {
                            chunk.DataProvider = new StreamDataProvider(stream, stream.Position, dataSize, true);
                            stream.Seek(dataSize, SeekOrigin.Current);
                        }
                        else
                        {
                            if (dataSize > int.MaxValue)
                            {
                                throw new NotSupportedException($"Raw data for chunk {i} is too large ({dataSize} bytes), exceeding memory buffer limits.");
                            }
                            var rawData = new byte[dataSize];
                            await ReadExactlyAsync(stream, rawData, 0, (int)dataSize, cancellationToken);
                            chunk.DataProvider = new MemoryDataProvider(rawData);
                        }
                        break;

                    case (ushort)ChunkType.Fill:
                        if (dataSize < 4)
                        {
                            throw new InvalidDataException($"Data size ({dataSize}) for FILL chunk {i} is less than 4 bytes");
                        }

                        await ReadExactlyAsync(stream, buffer4.ToArray(), 0, 4, cancellationToken); // buffer4 is span

                        chunk.FillValue = BinaryPrimitives.ReadUInt32LittleEndian(buffer4);

                        if (validateCrc && checksum.HasValue)
                        {
                            checksum = Crc32.UpdateRepeated(checksum.Value, chunk.FillValue, expectedRawSize);
                        }

                        if (dataSize > 4)
                        {
                            stream.Seek(dataSize - 4, SeekOrigin.Current);
                        }
                        break;

                    case (ushort)ChunkType.DontCare:
                        if (validateCrc && checksum.HasValue)
                        {
                            checksum = Crc32.UpdateZero(checksum.Value, expectedRawSize);
                        }
                        if (dataSize > 0)
                        {
                            stream.Seek(dataSize, SeekOrigin.Current);
                        }
                        break;

                    case (ushort)ChunkType.Crc32:
                        if (dataSize >= 4)
                        {
                            var crcFileData = new byte[4];
                            if (await stream.ReadAsync(crcFileData, 0, 4, cancellationToken) != 4)
                            {
                                throw new InvalidDataException($"Failed to read CRC32 value for chunk {i}");
                            }
                            var fileCrc = BinaryPrimitives.ReadUInt32LittleEndian(crcFileData);
                            if (validateCrc && checksum.HasValue && fileCrc != Crc32.Finish(checksum.Value))
                            {
                                throw new InvalidDataException($"CRC32 checksum mismatch: file has 0x{fileCrc:X8}, computed 0x{Crc32.Finish(checksum.Value):X8}");
                            }
                            if (dataSize > 4)
                            {
                                stream.Seek(dataSize - 4, SeekOrigin.Current);
                            }
                        }
                        break;

                    default:
                        throw new InvalidDataException($"Unknown chunk type for chunk {i}: 0x{chunkHeader.ChunkType:X4}");
                }

                if (chunkHeader.ChunkType != (ushort)ChunkType.Crc32)
                {
                    sparseFile.AddChunkRaw(chunk);
                    currentBlock += chunkHeader.ChunkSize;
                }
            }

            if (verbose)
            {
                activeLogger.LogInformation($"Sparse image parsing completed: {sparseFile.Chunks.Count} chunks, {currentBlock} blocks total");
            }

            if (sparseFile.Header.TotalBlocks != currentBlock)
            {
                throw new InvalidDataException($"Block count mismatch: Sparse header expects {sparseFile.Header.TotalBlocks} blocks, but parsed {currentBlock}");
            }

            return sparseFile;
        }
        finally
        {
            if (buffer != null)
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }

    private static async Task ReadExactlyAsync(Stream stream, byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
#if NET7_0_OR_GREATER
        await stream.ReadExactlyAsync(buffer.AsMemory(offset, count), cancellationToken);
#else
        var totalRead = 0;
        while (totalRead < count)
        {
            var read = await stream.ReadAsync(buffer, offset + totalRead, count - totalRead, cancellationToken);
            if (read == 0) throw new EndOfStreamException();
            totalRead += read;
        }
#endif
    }

    /// <summary>
    /// Smarter import: detects if file is sparse or raw.
    /// </summary>
    public static SparseFile ImportAuto(string filePath, bool validateCrc = false, bool verbose = false, ISparseLogger? logger = null)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return ImportAuto(stream, validateCrc, verbose, logger, filePath);
    }

    /// <summary>
    /// Automatically imports data from a stream, detecting whether it is in sparse or raw format.
    /// </summary>
    public static SparseFile ImportAuto(Stream stream, bool validateCrc = false, bool verbose = false, ISparseLogger? logger = null, string? filePath = null)
    {
        var magicData = new byte[4];
        var pos = stream.CanSeek ? stream.Position : 0;
        if (stream.Read(magicData, 0, 4) == 4)
        {
            var magic = BinaryPrimitives.ReadUInt32LittleEndian(magicData);
            if (stream.CanSeek)
            {
                stream.Seek(pos, SeekOrigin.Begin);
            }

            if (magic == SparseFormat.SparseHeaderMagic)
            {
                return FromStreamInternal(stream, filePath, validateCrc, verbose, logger);
            }
        }

        if (filePath != null)
        {
            return FromRawFile(filePath, 4096, verbose, logger);
        }

        // Treat as raw stream
        var rawFile = new SparseFile(4096, stream.Length, verbose) { Logger = logger };
        ReadFromStream(rawFile, stream, SparseReadMode.Normal);
        return rawFile;
    }

    /// <summary>
    /// Creates a <see cref="SparseFile"/> by importing a raw binary file.
    /// </summary>
    public static SparseFile FromRawFile(string filePath, uint blockSize = 4096, bool verbose = false, ISparseLogger? logger = null)
    {
        var fi = new FileInfo(filePath);
        var sparseFile = new SparseFile(blockSize, fi.Length, verbose) { Logger = logger };
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        ReadFromStream(sparseFile, fs, SparseReadMode.Normal);
        return sparseFile;
    }

    /// <summary>
    /// Reads data from a stream and incorporates it into the sparse file (sparsification).
    /// </summary>
    public static void ReadFromStream(SparseFile sparseFile, Stream stream, SparseReadMode mode, bool validateCrc = false)
    {
        if (mode == SparseReadMode.Sparse)
        {
            var headerData = new byte[SparseFormat.SparseHeaderSize];
            if (stream.Read(headerData, 0, headerData.Length) != headerData.Length)
            {
                throw new InvalidDataException("Failed to read sparse header");
            }

            var importedHeader = SparseHeader.FromBytes(headerData);
            if (!importedHeader.IsValid())
            {
                throw new InvalidDataException("Invalid sparse header");
            }

            if (sparseFile.Header.BlockSize != importedHeader.BlockSize)
            {
                throw new ArgumentException("Imported sparse file block size does not match the current file");
            }

            if (sparseFile.Verbose)
            {
                SparseLogger.LogInformation($"ReadFromStream (Sparse mode): BlockSize={importedHeader.BlockSize}, TotalBlocks={importedHeader.TotalBlocks}, TotalChunks={importedHeader.TotalChunks}");
            }

            stream.Seek(importedHeader.FileHeaderSize - SparseFormat.SparseHeaderSize, SeekOrigin.Current);

            var checksum = Crc32.Begin();
            var currentBlockStart = sparseFile.CurrentBlock;

            for (uint i = 0; i < importedHeader.TotalChunks; i++)
            {
                var chunkHeaderData = new byte[SparseFormat.ChunkHeaderSize];
                stream.ReadExactly(chunkHeaderData, 0, chunkHeaderData.Length);
                var chunkHeader = ChunkHeader.FromBytes(chunkHeaderData);

                if (sparseFile.Verbose)
                {
                    SparseLogger.LogInformation($"Imported Chunk #{i}: Type=0x{chunkHeader.ChunkType:X4}, Size={chunkHeader.ChunkSize} blocks");
                }

                stream.Seek(importedHeader.ChunkHeaderSize - SparseFormat.ChunkHeaderSize, SeekOrigin.Current);

                var dataSize = (long)chunkHeader.TotalSize - importedHeader.ChunkHeaderSize;
                var expectedRawSize = (long)chunkHeader.ChunkSize * sparseFile.Header.BlockSize;

                var chunk = new SparseChunk(chunkHeader) { StartBlock = currentBlockStart };

                switch (chunkHeader.ChunkType)
                {
                    case (ushort)ChunkType.Raw:
                        var rawData = new byte[dataSize];
                        stream.ReadExactly(rawData, 0, (int)dataSize);
                        if (validateCrc)
                        {
                            checksum = Crc32.Update(checksum, rawData);
                        }
                        chunk.DataProvider = new MemoryDataProvider(rawData);
                        sparseFile.AddChunkRaw(chunk);
                        currentBlockStart += chunkHeader.ChunkSize;
                        break;
                    case (ushort)ChunkType.Fill:
                        var fillData = new byte[4];
                        stream.ReadExactly(fillData, 0, 4);
                        var fillValue = BinaryPrimitives.ReadUInt32LittleEndian(fillData);
                        if (validateCrc)
                        {
                            checksum = Crc32.UpdateRepeated(checksum, fillValue, expectedRawSize);
                        }
                        chunk.FillValue = fillValue;
                        sparseFile.AddChunkRaw(chunk);
                        currentBlockStart += chunkHeader.ChunkSize;
                        if (dataSize > 4)
                        {
                            stream.Seek(dataSize - 4, SeekOrigin.Current);
                        }
                        break;
                    case (ushort)ChunkType.DontCare:
                        if (validateCrc)
                        {
                            checksum = Crc32.UpdateZero(checksum, expectedRawSize);
                        }
                        sparseFile.AddChunkRaw(chunk);
                        currentBlockStart += chunkHeader.ChunkSize;
                        break;
                    case (ushort)ChunkType.Crc32:
                        var crcFileData = new byte[4];
                        stream.ReadExactly(crcFileData, 0, 4);
                        if (validateCrc)
                        {
                            var fileCrc = BinaryPrimitives.ReadUInt32LittleEndian(crcFileData);
                            if (fileCrc != Crc32.Finish(checksum))
                            {
                                throw new InvalidDataException("CRC32 validation failed");
                            }
                        }
                        break;
                    default:
                        break;
                }
            }
            return;
        }

        // Normal or Hole mode: scan stream and sparsify
        var blockSize = sparseFile.Header.BlockSize;
        var bufferScan = new byte[blockSize];
        long currentPos = 0;
        var streamLen = stream.Length;
        long rawStart = -1;

        while (currentPos < streamLen)
        {
            if (stream.CanSeek)
            {
                stream.Position = currentPos;
            }
            var bytesRead = stream.Read(bufferScan, 0, (int)Math.Min(blockSize, streamLen - currentPos));
            if (bytesRead == 0)
            {
                break;
            }

            uint fillValue = 0;
            var isZero = IsZeroBlock(bufferScan, bytesRead);
            var isFill = !isZero && bytesRead == blockSize && IsFillBlock(bufferScan, out fillValue);

            if (isZero || isFill)
            {
                if (rawStart != -1)
                {
                    sparseFile.AddStreamChunk(stream, rawStart, (uint)(currentPos - rawStart));
                    rawStart = -1;
                }

                if (isZero)
                {
                    var zeroStart = currentPos;
                    currentPos += bytesRead;
                    while (currentPos < streamLen)
                    {
                        var innerRead = stream.Read(bufferScan, 0, (int)Math.Min(blockSize, streamLen - currentPos));
                        if (innerRead > 0 && IsZeroBlock(bufferScan, innerRead))
                        {
                            currentPos += innerRead;
                        }
                        else
                        {
                            break;
                        }
                    }
                    if (mode == SparseReadMode.Hole)
                    {
                        sparseFile.AddDontCareChunk(currentPos - zeroStart);
                    }
                    else
                    {
                        sparseFile.AddFillChunk(0, currentPos - zeroStart);
                    }
                }
                else
                {
                    var fillStart = currentPos;
                    var currentFillValue = fillValue;
                    currentPos += bytesRead;
                    while (currentPos < streamLen)
                    {
                        var innerRead = stream.Read(bufferScan, 0, (int)Math.Min(blockSize, streamLen - currentPos));
                        if (innerRead == blockSize && IsFillBlock(bufferScan, out var innerFill) && innerFill == currentFillValue)
                        {
                            currentPos += innerRead;
                        }
                        else
                        {
                            break;
                        }
                    }
                    sparseFile.AddFillChunk(currentFillValue, currentPos - fillStart);
                }
            }
            else
            {
                if (rawStart == -1)
                {
                    rawStart = currentPos;
                }
                currentPos += bytesRead;
            }
        }

        if (rawStart != -1)
        {
            sparseFile.AddStreamChunk(stream, rawStart, (uint)(streamLen - rawStart));
        }
    }

    private static bool IsZeroBlock(byte[] buffer, int length)
    {
        if (length == 0) return true;
        var span = buffer.AsSpan(0, length);
        var ulongSpan = MemoryMarshal.Cast<byte, ulong>(span);
        foreach (var v in ulongSpan) if (v != 0) return false;
        for (var i = ulongSpan.Length * 8; i < length; i++) if (buffer[i] != 0) return false;
        return true;
    }

    private static bool IsFillBlock(byte[] buffer, out uint fillValue)
    {
        fillValue = 0;
        if (buffer.Length < 4) return false;
        var pattern = BinaryPrimitives.ReadUInt32LittleEndian(buffer);
        var uintSpan = MemoryMarshal.Cast<byte, uint>(buffer.AsSpan());
        foreach (var v in uintSpan) if (v != pattern) return false;
        for (var i = uintSpan.Length * 4; i < buffer.Length; i++) if (buffer[i] != (byte)(pattern >> (i % 4 * 8))) return false;
        fillValue = pattern;
        return true;
    }
}
