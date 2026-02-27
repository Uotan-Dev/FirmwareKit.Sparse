namespace FirmwareKit.Sparse.Core;

/// <summary>
/// Represents a sparse file structure, providing methods to read, write, and manipulate Android sparse images.
/// </summary>
public class SparseFile : IDisposable
{
    private readonly List<SparseChunk> _chunks = new List<SparseChunk>();

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
    /// Gets or sets the sparse header.
    /// </summary>
    public SparseHeader Header { get; set; }

    /// <summary>
    /// Gets or sets the logger for this instance. If null, <see cref="SparseLogger.Instance"/> is used.
    /// </summary>
    public ISparseLogger? Logger { get; set; }

    /// <summary>
    /// Gets the list of sparse chunks in the file.
    /// </summary>
    public IReadOnlyList<SparseChunk> Chunks => _chunks;

    /// <summary>
    /// Gets or sets a value indicating whether verbose logging is enabled.
    /// </summary>
    public bool Verbose { get; set; } = false;

    /// <summary>
    /// Gets the total number of blocks added (representing the current maximum logical extent).
    /// </summary>
    public uint CurrentBlock
    {
        get
        {
            if (_chunks.Count == 0)
            {
                return 0;
            }
            // Optimization: Assume chunks are added in order. If not, fallback to Max for correctness.
            var last = _chunks[^1];
            return last.StartBlock + last.Header.ChunkSize;
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SparseFile"/> class with default settings.
    /// </summary>
    public SparseFile()
    {
        Header = new SparseHeader
        {
            Magic = SparseFormat.SparseHeaderMagic,
            MajorVersion = 1,
            MinorVersion = 0,
            FileHeaderSize = SparseFormat.SparseHeaderSize,
            ChunkHeaderSize = SparseFormat.ChunkHeaderSize,
            BlockSize = 4096,
            TotalBlocks = 0,
            TotalChunks = 0,
            ImageChecksum = 0
        };
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SparseFile"/> class with specified block size and total size.
    /// </summary>
    /// <param name="blockSize">The size of each block in bytes.</param>
    /// <param name="totalSize">The total logical size of the image.</param>
    /// <param name="verbose">Whether to enable verbose logging.</param>
    public SparseFile(uint blockSize, long totalSize, bool verbose = false)
    {
        Verbose = verbose;
        var totalBlocks = (uint)((totalSize + blockSize - 1) / blockSize);
        Header = new SparseHeader
        {
            Magic = SparseFormat.SparseHeaderMagic,
            MajorVersion = 1,
            MinorVersion = 0,
            FileHeaderSize = SparseFormat.SparseHeaderSize,
            ChunkHeaderSize = SparseFormat.ChunkHeaderSize,
            BlockSize = blockSize,
            TotalBlocks = totalBlocks,
            TotalChunks = 0,
            ImageChecksum = 0
        };
    }

    /// <summary>
    /// Loads a sparse file from the specified stream.
    /// </summary>
    /// <param name="stream">The stream to read from.</param>
    /// <param name="validateCrc">Whether to validate the CRC-32 checksum during loading.</param>
    /// <param name="verbose">Whether to enable verbose logging.</param>
    /// <param name="logger">Optional logger to use for this operation.</param>
    /// <returns>A new <see cref="SparseFile"/> instance loaded from the stream.</returns>
    public static SparseFile FromStream(Stream stream, bool validateCrc = false, bool verbose = false, ISparseLogger? logger = null)
    {
        return FromStreamInternal(stream, null, validateCrc, verbose, logger);
    }

    /// <summary>
    /// Loads a sparse file from a byte array buffer.
    /// </summary>
    /// <param name="buffer">The byte array containing the sparse image data.</param>
    /// <param name="validateCrc">Whether to validate the CRC-32 checksum during loading.</param>
    /// <param name="verbose">Whether to enable verbose logging.</param>
    /// <param name="logger">Optional logger to use for this operation.</param>
    /// <returns>A new <see cref="SparseFile"/> instance loaded from the buffer.</returns>
    public static SparseFile FromBuffer(byte[] buffer, bool validateCrc = false, bool verbose = false, ISparseLogger? logger = null)
    {
        using var ms = new MemoryStream(buffer);
        return FromStream(ms, validateCrc, verbose, logger);
    }

    /// <summary>
    /// Loads a sparse file from the specified image file.
    /// </summary>
    /// <param name="filePath">The path to the sparse image file.</param>
    /// <param name="validateCrc">Whether to validate the CRC-32 checksum during loading.</param>
    /// <param name="verbose">Whether to enable verbose logging.</param>
    /// <param name="logger">Optional logger to use for this operation.</param>
    /// <returns>A new <see cref="SparseFile"/> instance loaded from the file.</returns>
    public static SparseFile FromImageFile(string filePath, bool validateCrc = false, bool verbose = false, ISparseLogger? logger = null)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);
        return FromStreamInternal(stream, filePath, validateCrc, verbose, logger);
    }

    private static SparseFile FromStreamInternal(Stream stream, string? filePath, bool validateCrc, bool verbose, ISparseLogger? logger)
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
        byte[]? buffer = validateCrc ? System.Buffers.ArrayPool<byte>.Shared.Rent(1024 * 1024) : null;
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
                    sparseFile._chunks.Add(chunk);
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
                System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }

    /// <summary>
    /// Loads a sparse file from the specified stream asynchronously.
    /// </summary>
    /// <param name="stream">The stream to read from.</param>
    /// <param name="validateCrc">Whether to validate the CRC-32 checksum during loading.</param>
    /// <param name="verbose">Whether to enable verbose logging.</param>
    /// <param name="logger">Optional logger to use for this operation.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous load operation.</returns>
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

    private static async Task<SparseFile> FromStreamInternalAsync(Stream stream, string? filePath, bool validateCrc, bool verbose, ISparseLogger? logger, CancellationToken cancellationToken)
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
        var buffer = validateCrc ? System.Buffers.ArrayPool<byte>.Shared.Rent(1024 * 1024) : null;

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

                        await ReadExactlyAsync(stream, buffer4, 0, 4, cancellationToken);

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
                    sparseFile._chunks.Add(chunk);
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
                System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
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
    /// Resizes the sparse file total logical size.
    /// </summary>
    /// <param name="newSize">The new total size in bytes.</param>
    public void Resize(long newSize)
    {
        var newTotalBlocks = (uint)((newSize + Header.BlockSize - 1) / Header.BlockSize);
        Header = Header with { TotalBlocks = newTotalBlocks };
    }

    /// <summary>
    /// Writes the sparse file content to the specified stream asynchronously.
    /// </summary>
    /// <param name="stream">The target stream to write into.</param>
    /// <param name="sparse">Whether to write in sparse format (<c>true</c>) or raw format (<c>false</c>).</param>
    /// <param name="gzip">Whether to compress the output using GZip.</param>
    /// <param name="includeCrc">Whether to include a CRC-32 chunk at the end for validation.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous write operation.</returns>
    public async Task WriteToStreamAsync(Stream stream, bool sparse = true, bool gzip = false, bool includeCrc = false, CancellationToken cancellationToken = default)
    {
        if (!sparse)
        {
            await WriteRawToStreamAsync(stream, false, cancellationToken);
            return;
        }

        var targetStream = stream;
        if (gzip)
        {
            targetStream = new System.IO.Compression.GZipStream(stream, System.IO.Compression.CompressionMode.Compress, true);
        }

        try
        {
            // Pre-processing: merge/fill gaps
            var sortedChunks = _chunks.OrderBy(c => c.StartBlock).ToList();
            var finalChunks = new List<SparseChunk>();
            uint currentBlock = 0;

            foreach (var chunk in sortedChunks)
            {
                if (chunk.StartBlock > currentBlock)
                {
                    // Fill gaps
                    var gapBlocks = chunk.StartBlock - currentBlock;
                    finalChunks.Add(new SparseChunk(new ChunkHeader
                    {
                        ChunkType = (ushort)ChunkType.DontCare,
                        ChunkSize = gapBlocks,
                        TotalSize = SparseFormat.ChunkHeaderSize
                    })
                    { StartBlock = currentBlock });
                }
                finalChunks.Add(chunk);
                currentBlock = chunk.StartBlock + chunk.Header.ChunkSize;
            }

            var outHeader = Header;
            var sumBlocks = currentBlock;

            var needsTrailingSkip = outHeader.TotalBlocks > sumBlocks;

            var totalChunks = (uint)finalChunks.Count;
            if (needsTrailingSkip)
            {
                totalChunks++;
            }
            if (includeCrc)
            {
                totalChunks++;
            }

            outHeader = Header with { TotalChunks = totalChunks };
            if (sumBlocks > outHeader.TotalBlocks)
            {
                outHeader = outHeader with { TotalBlocks = sumBlocks };
            }

            var headerData = new byte[SparseFormat.SparseHeaderSize];
            outHeader.WriteTo(headerData);
            await targetStream.WriteAsync(headerData, 0, headerData.Length, cancellationToken);

            var checksum = Crc32.Begin();
            var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(1024 * 1024);
            try
            {
                var chunkHeaderData = new byte[SparseFormat.ChunkHeaderSize];
                var fillValData = new byte[4];
                foreach (var chunk in finalChunks)
                {
                    chunk.Header.WriteTo(chunkHeaderData);
                    await targetStream.WriteAsync(chunkHeaderData, 0, chunkHeaderData.Length, cancellationToken);

                    var expectedDataSize = (long)chunk.Header.ChunkSize * outHeader.BlockSize;

                    switch (chunk.Header.ChunkType)
                    {
                        case (ushort)ChunkType.Raw:
                            if (chunk.DataProvider != null)
                            {
                                await chunk.DataProvider.WriteToAsync(targetStream, cancellationToken);

                                if (includeCrc)
                                {
                                    long providerOffset = 0;
                                    while (providerOffset < chunk.DataProvider.Length)
                                    {
                                        var toRead = (int)Math.Min(buffer.Length, chunk.DataProvider.Length - providerOffset);
                                        var read = chunk.DataProvider.Read(providerOffset, buffer, 0, toRead);
                                        if (read <= 0) break;
                                        checksum = Crc32.Update(checksum, buffer, 0, read);
                                        providerOffset += read;
                                    }
                                }

                                var providerLength = chunk.DataProvider.Length;
                                var padding = expectedDataSize - providerLength;
                                if (padding > 0)
                                {
                                    Array.Clear(buffer, 0, (int)Math.Min(buffer.Length, padding));
                                    while (padding > 0)
                                    {
                                        var toWrite = (int)Math.Min(buffer.Length, padding);
                                        await targetStream.WriteAsync(buffer, 0, toWrite, cancellationToken);
                                        if (includeCrc)
                                        {
                                            checksum = Crc32.UpdateZero(checksum, toWrite);
                                        }
                                        padding -= toWrite;
                                    }
                                }
                            }
                            else
                            {
                                // Write dummy data or seek? Usually RAW chunks have data providers.
                                // If not, we write zeros.
                                Array.Clear(buffer, 0, buffer.Length);
                                var remaining = expectedDataSize;
                                while (remaining > 0)
                                {
                                    var toWrite = (int)Math.Min(buffer.Length, remaining);
                                    await targetStream.WriteAsync(buffer, 0, toWrite, cancellationToken);
                                    if (includeCrc)
                                    {
                                        checksum = Crc32.UpdateZero(checksum, toWrite);
                                    }
                                    remaining -= toWrite;
                                }
                            }
                            break;

                        case (ushort)ChunkType.Fill:
                            BinaryPrimitives.WriteUInt32LittleEndian(fillValData, chunk.FillValue);
                            await targetStream.WriteAsync(fillValData, 0, fillValData.Length, cancellationToken);
                            if (includeCrc)
                            {
                                checksum = Crc32.UpdateRepeated(checksum, chunk.FillValue, expectedDataSize);
                            }
                            break;

                        case (ushort)ChunkType.DontCare:
                            if (includeCrc)
                            {
                                checksum = Crc32.UpdateZero(checksum, expectedDataSize);
                            }
                            break;
                    }
                }

                if (needsTrailingSkip)
                {
                    var skipBlocks = outHeader.TotalBlocks - sumBlocks;
                    var skipChunkHeader = new ChunkHeader
                    {
                        ChunkType = (ushort)ChunkType.DontCare,
                        ChunkSize = skipBlocks,
                        TotalSize = SparseFormat.ChunkHeaderSize
                    };
                    skipChunkHeader.WriteTo(chunkHeaderData);
                    await targetStream.WriteAsync(chunkHeaderData, 0, chunkHeaderData.Length, cancellationToken);

                    if (includeCrc)
                    {
                        checksum = Crc32.UpdateZero(checksum, (long)skipBlocks * outHeader.BlockSize);
                    }
                }

                if (includeCrc)
                {
                    var finalChecksum = Crc32.Finish(checksum);
                    var crcChunkHeader = new ChunkHeader
                    {
                        ChunkType = (ushort)ChunkType.Crc32,
                        ChunkSize = 0,
                        TotalSize = SparseFormat.ChunkHeaderSize + 4
                    };
                    crcChunkHeader.WriteTo(chunkHeaderData);
                    await targetStream.WriteAsync(chunkHeaderData, 0, chunkHeaderData.Length, cancellationToken);
                    BinaryPrimitives.WriteUInt32LittleEndian(fillValData, finalChecksum);
                    await targetStream.WriteAsync(fillValData, 0, 4, cancellationToken);
                }
            }
            finally
            {
                System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
            }

            if (gzip)
            {
                await targetStream.FlushAsync(cancellationToken);
            }
        }
        finally
        {
            if (gzip && targetStream != null)
            {
#if NET6_0_OR_GREATER
                await targetStream.DisposeAsync();
#else
                targetStream.Dispose();
#endif
            }
        }
    }

    /// <summary>
    /// Writes the raw (uncompressed) data of the sparse file to the specified stream asynchronously.
    /// </summary>
    /// <param name="stream">The target stream to write into.</param>
    /// <param name="sparseMode">Whether to use sparse seeking if the stream supports it (<c>true</c>).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous write operation.</returns>
    public async Task WriteRawToStreamAsync(Stream stream, bool sparseMode = false, CancellationToken cancellationToken = default)
    {
        var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(1024 * 1024);
        uint? currentBufferFillValue = null;
        try
        {
            foreach (var chunk in _chunks)
            {
                var size = (long)chunk.Header.ChunkSize * Header.BlockSize;
                switch (chunk.Header.ChunkType)
                {
                    case (ushort)ChunkType.Raw:
                        currentBufferFillValue = null; // Buffer is contaminated
                        if (chunk.DataProvider != null)
                        {
                            await chunk.DataProvider.WriteToAsync(stream, cancellationToken);
                            var remainingPadding = size - chunk.DataProvider.Length;
                            if (remainingPadding > 0)
                            {
                                Array.Clear(buffer, 0, (int)Math.Min(buffer.Length, remainingPadding));
                                while (remainingPadding > 0)
                                {
                                    var toWrite = (int)Math.Min(buffer.Length, remainingPadding);
                                    await stream.WriteAsync(buffer, 0, toWrite, cancellationToken);
                                    remainingPadding -= toWrite;
                                }
                            }
                        }
                        else
                        {
                            if (sparseMode && stream.CanSeek)
                            {
                                stream.Seek(size, SeekOrigin.Current);
                            }
                            else
                            {
                                Array.Clear(buffer, 0, (int)Math.Min(buffer.Length, size));
                                var remaining = size;
                                while (remaining > 0)
                                {
                                    var toWrite = (int)Math.Min(buffer.Length, remaining);
                                    await stream.WriteAsync(buffer, 0, toWrite, cancellationToken);
                                    remaining -= toWrite;
                                }
                            }
                        }
                        break;

                    case (ushort)ChunkType.Fill:
                        if (currentBufferFillValue != chunk.FillValue)
                        {
                            for (var r = 0; r <= buffer.Length - 4; r += 4)
                            {
                                BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(r), chunk.FillValue);
                            }
                            currentBufferFillValue = chunk.FillValue;
                        }

                        var remainingFill = size;
                        while (remainingFill > 0)
                        {
                            var toWrite = (int)Math.Min(buffer.Length, remainingFill);
                            await stream.WriteAsync(buffer, 0, toWrite, cancellationToken);
                            remainingFill -= toWrite;
                        }
                        break;

                    case (ushort)ChunkType.DontCare:
                        currentBufferFillValue = null; // Buffer may be contaminated later if we reuse it for raw/padding
                        if (sparseMode && stream.CanSeek)
                        {
                            stream.Seek(size, SeekOrigin.Current);
                        }
                        else
                        {
                            Array.Clear(buffer, 0, (int)Math.Min(buffer.Length, size));
                            var remainingSkip = size;
                            while (remainingSkip > 0)
                            {
                                var toWrite = (int)Math.Min(buffer.Length, remainingSkip);
                                await stream.WriteAsync(buffer, 0, toWrite, cancellationToken);
                                remainingSkip -= toWrite;
                            }
                        }
                        break;
                }
            }
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Writes the sparse file content to the specified stream.
    /// </summary>
    /// <param name="stream">The target stream to write into.</param>
    /// <param name="sparse">Whether to write in sparse format (<c>true</c>) or raw format (<c>false</c>).</param>
    /// <param name="gzip">Whether to compress the output using GZip.</param>
    /// <param name="includeCrc">Whether to include a CRC-32 chunk at the end for validation.</param>
    public void WriteToStream(Stream stream, bool sparse = true, bool gzip = false, bool includeCrc = false)
    {
        if (!sparse)
        {
            WriteRawToStream(stream);
            return;
        }

        var targetStream = stream;
        if (gzip)
        {
            targetStream = new System.IO.Compression.GZipStream(stream, System.IO.Compression.CompressionMode.Compress, true);
        }

        try
        {
            // Pre-processing: merge/fill gaps
            var sortedChunks = _chunks.OrderBy(c => c.StartBlock).ToList();
            var finalChunks = new List<SparseChunk>();
            uint currentBlock = 0;

            foreach (var chunk in sortedChunks)
            {
                if (chunk.StartBlock > currentBlock)
                {
                    // Fill gaps
                    var gapBlocks = chunk.StartBlock - currentBlock;
                    finalChunks.Add(new SparseChunk(new ChunkHeader
                    {
                        ChunkType = (ushort)ChunkType.DontCare,
                        ChunkSize = gapBlocks,
                        TotalSize = SparseFormat.ChunkHeaderSize
                    })
                    { StartBlock = currentBlock });
                }
                finalChunks.Add(chunk);
                currentBlock = chunk.StartBlock + chunk.Header.ChunkSize;
            }

            var outHeader = Header;
            var sumBlocks = currentBlock;

            var needsTrailingSkip = outHeader.TotalBlocks > sumBlocks;

            var totalChunks = (uint)finalChunks.Count;
            if (needsTrailingSkip)
            {
                totalChunks++;
            }
            if (includeCrc)
            {
                totalChunks++;
            }

            outHeader = Header with { TotalChunks = totalChunks };
            if (sumBlocks > outHeader.TotalBlocks)
            {
                outHeader = outHeader with { TotalBlocks = sumBlocks };
            }

            Span<byte> headerData = stackalloc byte[SparseFormat.SparseHeaderSize];
            outHeader.WriteTo(headerData);
            targetStream.Write(headerData);

            var checksum = Crc32.Begin();
            var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(1024 * 1024);
            try
            {
                Span<byte> chunkHeaderData = stackalloc byte[SparseFormat.ChunkHeaderSize];
                Span<byte> fillValData = stackalloc byte[4];
                foreach (var chunk in finalChunks)
                {
                    chunk.Header.WriteTo(chunkHeaderData);
                    targetStream.Write(chunkHeaderData);

                    var expectedDataSize = (long)chunk.Header.ChunkSize * outHeader.BlockSize;

                    switch (chunk.Header.ChunkType)
                    {
                        case (ushort)ChunkType.Raw:
                            if (chunk.DataProvider != null)
                            {
                                long providerOffset = 0;
                                while (providerOffset < chunk.DataProvider.Length)
                                {
                                    var toRead = (int)Math.Min(buffer.Length, chunk.DataProvider.Length - providerOffset);
                                    var read = chunk.DataProvider.Read(providerOffset, buffer, 0, toRead);
                                    if (read <= 0)
                                    {
                                        break;
                                    }

                                    targetStream.Write(buffer, 0, read);
                                    if (includeCrc)
                                    {
                                        checksum = Crc32.Update(checksum, buffer, 0, read);
                                    }
                                    providerOffset += read;
                                }

                                var padding = expectedDataSize - providerOffset;
                                if (padding > 0)
                                {
                                    Array.Clear(buffer, 0, (int)Math.Min(buffer.Length, padding));
                                    while (padding > 0)
                                    {
                                        var toWrite = (int)Math.Min(buffer.Length, padding);
                                        targetStream.Write(buffer, 0, toWrite);
                                        if (includeCrc)
                                        {
                                            checksum = Crc32.UpdateZero(checksum, toWrite);
                                        }
                                        padding -= toWrite;
                                    }
                                }
                            }
                            else
                            {
                                Array.Clear(buffer, 0, buffer.Length);
                                var remaining = expectedDataSize;
                                while (remaining > 0)
                                {
                                    var toWrite = (int)Math.Min(buffer.Length, remaining);
                                    targetStream.Write(buffer, 0, toWrite);
                                    if (includeCrc)
                                    {
                                        checksum = Crc32.UpdateZero(checksum, toWrite);
                                    }
                                    remaining -= toWrite;
                                }
                            }
                            break;

                        case (ushort)ChunkType.Fill:
                            BinaryPrimitives.WriteUInt32LittleEndian(fillValData, chunk.FillValue);
                            targetStream.Write(fillValData);
                            if (includeCrc)
                            {
                                checksum = Crc32.UpdateRepeated(checksum, chunk.FillValue, expectedDataSize);
                            }
                            break;

                        case (ushort)ChunkType.DontCare:
                            if (includeCrc)
                            {
                                checksum = Crc32.UpdateZero(checksum, expectedDataSize);
                            }
                            break;

                        case (ushort)ChunkType.Crc32:
                            break;
                        default:
                            break;
                    }
                }

                if (needsTrailingSkip)
                {
                    var skipSize = outHeader.TotalBlocks - sumBlocks;
                    var skipHeader = new ChunkHeader
                    {
                        ChunkType = (ushort)ChunkType.DontCare,
                        Reserved = 0,
                        ChunkSize = skipSize,
                        TotalSize = SparseFormat.ChunkHeaderSize
                    };
                    targetStream.Write(skipHeader.ToBytes(), 0, SparseFormat.ChunkHeaderSize);

                    if (includeCrc)
                    {
                        checksum = Crc32.UpdateZero(checksum, (long)skipSize * outHeader.BlockSize);
                    }
                }

                if (includeCrc)
                {
                    var finalChecksum = Crc32.Finish(checksum);
                    var crcHeader = new ChunkHeader
                    {
                        ChunkType = (ushort)ChunkType.Crc32,
                        Reserved = 0,
                        ChunkSize = 0,
                        TotalSize = SparseFormat.ChunkHeaderSize + 4
                    };
                    var finalCrcData = new byte[4];
                    BinaryPrimitives.WriteUInt32LittleEndian(finalCrcData, finalChecksum);
                    targetStream.Write(crcHeader.ToBytes(), 0, SparseFormat.ChunkHeaderSize);
                    targetStream.Write(finalCrcData, 0, 4);

                    outHeader = outHeader with { ImageChecksum = finalChecksum };
                    if (targetStream.CanSeek)
                    {
                        var currentPos = targetStream.Position;
                        targetStream.Seek(0, SeekOrigin.Begin);
                        targetStream.Write(outHeader.ToBytes(), 0, SparseFormat.SparseHeaderSize);
                        targetStream.Seek(currentPos, SeekOrigin.Begin);
                    }
                }
            }
            finally
            {
                System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
            }
        }
        finally
        {
            if (gzip)
            {
                targetStream.Dispose();
            }
        }
    }

    /// <summary>
    /// Writes the raw (uncompressed) data of the sparse file to the specified stream.
    /// </summary>
    /// <param name="stream">The target stream to write into.</param>
    /// <param name="sparseMode">Whether to use sparse seeking if the stream supports it (<c>true</c>).</param>
    public void WriteRawToStream(Stream stream, bool sparseMode = false)
    {
        var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(1024 * 1024);
        try
        {
            foreach (var chunk in _chunks)
            {
                var size = (long)chunk.Header.ChunkSize * Header.BlockSize;
                switch (chunk.Header.ChunkType)
                {
                    case (ushort)ChunkType.Raw:
                        if (chunk.DataProvider != null)
                        {
                            long written = 0;
                            while (written < chunk.DataProvider.Length)
                            {
                                var toRead = (int)Math.Min(buffer.Length, chunk.DataProvider.Length - written);
                                var read = chunk.DataProvider.Read(written, buffer, 0, toRead);
                                if (read <= 0)
                                {
                                    break;
                                }

                                stream.Write(buffer, 0, read);
                                written += read;
                            }
                            if (written < size)
                            {
                                Array.Clear(buffer, 0, (int)Math.Min(buffer.Length, size - written));
                                while (written < size)
                                {
                                    var toWrite = (int)Math.Min(buffer.Length, size - written);
                                    stream.Write(buffer, 0, toWrite);
                                    written += toWrite;
                                }
                            }
                        }
                        else
                        {
                            if (sparseMode && stream.CanSeek)
                            {
                                stream.Seek(size, SeekOrigin.Current);
                            }
                            else
                            {
                                Array.Clear(buffer, 0, buffer.Length);
                                var remaining = size;
                                while (remaining > 0)
                                {
                                    var toWrite = (int)Math.Min(buffer.Length, remaining);
                                    stream.Write(buffer, 0, toWrite);
                                    remaining -= toWrite;
                                }
                            }
                        }
                        break;
                    case (ushort)ChunkType.Fill:
                        var fillValBytes = new byte[4];
                        BinaryPrimitives.WriteUInt32LittleEndian(fillValBytes, chunk.FillValue);
                        for (var i = 0; i <= buffer.Length - 4; i += 4)
                        {
                            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(i), chunk.FillValue);
                        }

                        var fillRemaining = size;
                        while (fillRemaining > 0)
                        {
                            var toWrite = (int)Math.Min(buffer.Length, fillRemaining);
                            stream.Write(buffer, 0, toWrite);
                            fillRemaining -= toWrite;
                        }
                        break;
                    case (ushort)ChunkType.DontCare:
                        if (sparseMode && stream.CanSeek)
                        {
                            stream.Seek(size, SeekOrigin.Current);
                        }
                        else
                        {
                            Array.Clear(buffer, 0, buffer.Length);
                            var remaining = size;
                            while (remaining > 0)
                            {
                                var toWrite = (int)Math.Min(buffer.Length, remaining);
                                stream.Write(buffer, 0, toWrite);
                                remaining -= toWrite;
                            }
                        }
                        break;
                    default:
                        break;
                }
            }
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
        }

        var expectedFullLength = (long)Header.TotalBlocks * Header.BlockSize;
        if (stream.CanSeek && stream.Length < expectedFullLength)
        {
            stream.SetLength(expectedFullLength);
        }
    }

    /// <summary>
    /// Gets the specified block start index and checks for overlaps
    /// </summary>
    private uint GetNextBlockAndCheckOverlap(uint? blockIndex, uint sizeInBlocks)
    {
        var start = blockIndex ?? CurrentBlock;
        foreach (var chunk in _chunks)
        {
            if (start < chunk.StartBlock + chunk.Header.ChunkSize && start + sizeInBlocks > chunk.StartBlock)
            {
                throw new ArgumentException($"Block region [{start}, {start + sizeInBlocks}) overlaps with existing chunk [{chunk.StartBlock}, {chunk.StartBlock + chunk.Header.ChunkSize}).");
            }
        }

        if (blockIndex.HasValue && blockIndex.Value > CurrentBlock)
        {
            var gapSizeBlocks = blockIndex.Value - CurrentBlock;
            AddDontCareChunk((long)gapSizeBlocks * Header.BlockSize, CurrentBlock);
        }

        return start;
    }

    private void AddChunkSorted(SparseChunk chunk)
    {
        var index = _chunks.FindIndex(c => c.StartBlock > chunk.StartBlock);
        if (index == -1)
        {
            _chunks.Add(chunk);
        }
        else
        {
            _chunks.Insert(index, chunk);
        }
    }

    /// <summary>
    /// Represents a callback method used for processing written data chunks.
    /// </summary>
    /// <param name="data">The byte array containing the data, or <c>null</c> for gaps.</param>
    /// <param name="length">The length of the data to process.</param>
    /// <returns>A status code where negative values typically indicate failure.</returns>
    public delegate int SparseWriteCallback(byte[]? data, int length);

    /// <summary>
    /// Writes the sparse file content using a custom callback for each data block.
    /// </summary>
    /// <param name="callback">The callback method to invoke for each data block.</param>
    /// <param name="sparse">Whether to provide data in sparse format (<c>true</c>) or raw format (<c>false</c>).</param>
    /// <param name="includeCrc">Whether to calculate and include CRC-32 checksums.</param>
    public void WriteWithCallback(SparseWriteCallback callback, bool sparse = true, bool includeCrc = false)
    {
        if (!sparse)
        {
            var buffer = new byte[1024 * 1024];
            foreach (var chunk in _chunks)
            {
                var size = (long)chunk.Header.ChunkSize * Header.BlockSize;
                switch (chunk.Header.ChunkType)
                {
                    case (ushort)ChunkType.Raw:
                        if (chunk.DataProvider != null)
                        {
                            long written = 0;
                            while (written < chunk.DataProvider.Length)
                            {
                                var toRead = (int)Math.Min(buffer.Length, chunk.DataProvider.Length - written);
                                var read = chunk.DataProvider.Read(written, buffer, 0, toRead);
                                if (read <= 0)
                                {
                                    break;
                                }

                                if (callback(buffer, read) < 0)
                                {
                                    return;
                                }

                                written += read;
                            }
                            if (written < size)
                            {
                                Array.Clear(buffer, 0, (int)Math.Min(buffer.Length, size - written));
                                while (written < size)
                                {
                                    var toWrite = (int)Math.Min(buffer.Length, size - written);
                                    if (callback(buffer, toWrite) < 0)
                                    {
                                        return;
                                    }

                                    written += toWrite;
                                }
                            }
                        }
                        else
                        {
                            if (callback(null, (int)size) < 0)
                            {
                                return;
                            }
                        }
                        break;
                    case (ushort)ChunkType.Fill:
                        var fillValBytes = new byte[4];
                        BinaryPrimitives.WriteUInt32LittleEndian(fillValBytes, chunk.FillValue);
                        for (var i = 0; i <= buffer.Length - 4; i += 4)
                        {
                            Array.Copy(fillValBytes, 0, buffer, i, 4);
                        }

                        var fillRemaining = size;
                        while (fillRemaining > 0)
                        {
                            var toWrite = (int)Math.Min(buffer.Length, fillRemaining);
                            if (callback(buffer, toWrite) < 0)
                            {
                                return;
                            }

                            fillRemaining -= toWrite;
                        }
                        break;
                    case (ushort)ChunkType.DontCare:
                        if (callback(null, (int)size) < 0)
                        {
                            return;
                        }

                        break;
                    default:
                        break;
                }
            }
            return;
        }

        // Callback writing in Sparse mode
        using var ms = new MemoryStream();
        WriteToStream(ms, true, false, includeCrc);
        var bytes = ms.ToArray();
        callback(bytes, bytes.Length);
    }

    /// <summary>
    /// Adds a RAW chunk that references data from an external file.
    /// </summary>
    /// <param name="filePath">The path to the source file.</param>
    /// <param name="offset">The byte offset within the source file.</param>
    /// <param name="size">The size of the data to include, in bytes.</param>
    /// <param name="blockIndex">The starting logical block index. If <c>null</c>, appends to the end.</param>
    public void AddRawFileChunk(string filePath, long offset, uint size, uint? blockIndex = null)
    {
        var blockSize = Header.BlockSize;
        var totalBlocks = (size + blockSize - 1) / blockSize;
        var currentBlockStart = GetNextBlockAndCheckOverlap(blockIndex, totalBlocks);

        var remaining = size;
        var currentOffset = offset;

        while (remaining > 0)
        {
            var partSize = Math.Min(remaining, SparseFormat.MaxChunkDataSize);
            if (partSize < remaining && partSize % blockSize != 0)
            {
                partSize = partSize / blockSize * blockSize;
                if (partSize == 0)
                {
                    partSize = remaining;
                }
            }

            var chunkBlocks = (partSize + blockSize - 1) / blockSize;
            var chunkHeader = new ChunkHeader
            {
                ChunkType = (ushort)ChunkType.Raw,
                Reserved = 0,
                ChunkSize = chunkBlocks,
                TotalSize = (uint)(SparseFormat.ChunkHeaderSize + ((long)chunkBlocks * blockSize))
            };

            var chunk = new SparseChunk(chunkHeader)
            {
                StartBlock = currentBlockStart,
                DataProvider = new FileDataProvider(filePath, currentOffset, partSize)
            };

            AddChunkSorted(chunk);
            currentBlockStart += chunkBlocks;
            remaining -= partSize;
            currentOffset += partSize;
        }
    }

    /// <summary>
    /// Adds a RAW chunk using data from a byte array buffer.
    /// </summary>
    /// <param name="data">The byte array containing the raw data.</param>
    /// <param name="blockIndex">The starting logical block index. If <c>null</c>, appends to the end.</param>
    public void AddRawChunk(byte[] data, uint? blockIndex = null)
    {
        var blockSize = Header.BlockSize;
        var totalBlocks = (uint)((data.Length + blockSize - 1) / blockSize);
        var currentBlockStart = GetNextBlockAndCheckOverlap(blockIndex, totalBlocks);

        var remaining = (uint)data.Length;
        var currentOffset = 0;

        while (remaining > 0)
        {
            var partSize = Math.Min(remaining, SparseFormat.MaxChunkDataSize);
            if (partSize < remaining && partSize % blockSize != 0)
            {
                partSize = partSize / blockSize * blockSize;
                if (partSize == 0)
                {
                    partSize = remaining;
                }
            }

            var chunkBlocks = (partSize + blockSize - 1) / blockSize;
            var chunkHeader = new ChunkHeader
            {
                ChunkType = (ushort)ChunkType.Raw,
                Reserved = 0,
                ChunkSize = chunkBlocks,
                TotalSize = (uint)(SparseFormat.ChunkHeaderSize + ((long)chunkBlocks * blockSize))
            };

            var chunk = new SparseChunk(chunkHeader)
            {
                StartBlock = currentBlockStart,
                DataProvider = new MemoryDataProvider(data, currentOffset, (int)partSize)
            };

            AddChunkSorted(chunk);
            currentBlockStart += chunkBlocks;
            remaining -= partSize;
            currentOffset += (int)partSize;
        }
    }

    /// <summary>
    /// Adds a RAW chunk using data from a stream.
    /// </summary>
    /// <param name="stream">The source stream to read from.</param>
    /// <param name="offset">The starting byte offset in the stream.</param>
    /// <param name="size">The size of the data to include, in bytes.</param>
    /// <param name="blockIndex">The starting logical block index. If <c>null</c>, appends to the end.</param>
    /// <param name="leaveOpen">Whether to keep the stream open after use.</param>
    public void AddStreamChunk(Stream stream, long offset, uint size, uint? blockIndex = null, bool leaveOpen = true)
    {
        var blockSize = Header.BlockSize;
        var totalBlocks = (size + blockSize - 1) / blockSize;
        var currentBlockStart = GetNextBlockAndCheckOverlap(blockIndex, totalBlocks);

        var remaining = size;
        var currentOffset = offset;

        while (remaining > 0)
        {
            var partSize = Math.Min(remaining, SparseFormat.MaxChunkDataSize);
            if (partSize < remaining && partSize % blockSize != 0)
            {
                partSize = partSize / blockSize * blockSize;
                if (partSize == 0)
                {
                    partSize = remaining;
                }
            }

            var chunkBlocks = (partSize + blockSize - 1) / blockSize;
            var chunkHeader = new ChunkHeader
            {
                ChunkType = (ushort)ChunkType.Raw,
                Reserved = 0,
                ChunkSize = chunkBlocks,
                TotalSize = (uint)(SparseFormat.ChunkHeaderSize + ((long)chunkBlocks * blockSize))
            };

            var chunk = new SparseChunk(chunkHeader)
            {
                StartBlock = currentBlockStart,
                DataProvider = new StreamDataProvider(stream, currentOffset, partSize, leaveOpen)
            };

            AddChunkSorted(chunk);
            currentBlockStart += chunkBlocks;
            remaining -= partSize;
            currentOffset += partSize;
        }
    }

    /// <summary>
    /// Adds a FILL chunk that repeats a 4-byte value.
    /// </summary>
    /// <param name="fillValue">The 32-bit value to repeat.</param>
    /// <param name="size">The total logical size of the fill region, in bytes.</param>
    /// <param name="blockIndex">The starting logical block index. If <c>null</c>, appends to the end.</param>
    public void AddFillChunk(uint fillValue, long size, uint? blockIndex = null)
    {
        var blockSize = Header.BlockSize;
        var totalBlocks = (uint)((size + blockSize - 1) / blockSize);
        var currentBlockStart = GetNextBlockAndCheckOverlap(blockIndex, totalBlocks);

        var remaining = size;

        while (remaining > 0)
        {
            var partSize = Math.Min(remaining, (long)0x00FFFFFF * blockSize);

            var partBlocks = (uint)((partSize + blockSize - 1) / blockSize);
            if (partBlocks > 0x00FFFFFF)
            {
                partBlocks = 0x00FFFFFF;
            }

            var actualPartSize = (long)partBlocks * blockSize;
            if (actualPartSize > remaining)
            {
                actualPartSize = remaining;
            }

            var chunkHeader = new ChunkHeader
            {
                ChunkType = (ushort)ChunkType.Fill,
                Reserved = 0,
                ChunkSize = partBlocks,
                TotalSize = SparseFormat.ChunkHeaderSize + 4
            };

            var chunk = new SparseChunk(chunkHeader)
            {
                StartBlock = currentBlockStart,
                FillValue = fillValue
            };

            AddChunkSorted(chunk);
            currentBlockStart += partBlocks;
            remaining -= actualPartSize;
        }
    }

    /// <summary>
    /// Adds a DONT_CARE (skip) chunk representing an unallocated or empty region.
    /// </summary>
    /// <param name="size">The size of the region to skip, in bytes.</param>
    /// <param name="blockIndex">The starting logical block index. If <c>null</c>, appends to the end.</param>
    public void AddDontCareChunk(long size, uint? blockIndex = null)
    {
        var blockSize = Header.BlockSize;
        var totalBlocks = (uint)((size + blockSize - 1) / blockSize);
        var currentBlockStart = GetNextBlockAndCheckOverlap(blockIndex, totalBlocks);

        var remaining = size;

        while (remaining > 0)
        {
            var partBlocks = (uint)((remaining + blockSize - 1) / blockSize);
            if (partBlocks > 0x00FFFFFFu)
            {
                partBlocks = 0x00FFFFFFu;
            }

            var actualPartSize = (long)partBlocks * blockSize;

            var chunkHeader = new ChunkHeader
            {
                ChunkType = (ushort)ChunkType.DontCare,
                Reserved = 0,
                ChunkSize = partBlocks,
                TotalSize = SparseFormat.ChunkHeaderSize
            };

            var chunk = new SparseChunk(chunkHeader) { StartBlock = currentBlockStart };
            AddChunkSorted(chunk);
            currentBlockStart += partBlocks;
            remaining -= Math.Min(remaining, actualPartSize);
        }
    }

    /// <summary>
    /// Releases all resources used by the <see cref="SparseFile"/> instance.
    /// </summary>
    public void Dispose()
    {
        foreach (var chunk in _chunks)
        {
            chunk.Dispose();
        }
    }

    /// <summary>
    /// Splits the current sparse file into multiple sparse files, each not exceeding the specified maximum size.
    /// </summary>
    /// <param name="maxFileSize">The maximum size in bytes for each output sparse file.</param>
    /// <returns>A list of <see cref="SparseFile"/> instances representing the split result.</returns>
    public List<SparseFile> Resparse(long maxFileSize)
    {
        var result = new List<SparseFile>();
        long overhead = SparseFormat.SparseHeaderSize + (2 * SparseFormat.ChunkHeaderSize) + 4;

        if (maxFileSize <= overhead)
        {
            throw new ArgumentException($"maxFileSize must be greater than the infrastructure overhead ({overhead} bytes)");
        }

        var fileLimit = maxFileSize - overhead;
        var entries = BuildResparseEntries();

        if (entries.Count == 0)
        {
            var emptyFile = CreateNewSparseForResparse();
            emptyFile.Header = emptyFile.Header with { TotalBlocks = Header.TotalBlocks };
            if (Header.TotalBlocks > 0)
            {
                emptyFile._chunks.Add(CreateDontCareChunk(Header.TotalBlocks));
            }
            FinishCurrentResparseFile(emptyFile);
            result.Add(emptyFile);
            return result;
        }

        var startIndex = 0;
        while (startIndex < entries.Count)
        {
            long fileLen = 0;
            uint lastBlock = 0;
            var lastIncludedIndex = -1;

            for (var i = startIndex; i < entries.Count; i++)
            {
                var entry = entries[i];
                var count = GetSparseChunkSize(entry.Chunk);
                if (entry.StartBlock > lastBlock)
                {
                    count += SparseFormat.ChunkHeaderSize;
                }

                lastBlock = entry.StartBlock + entry.Chunk.Header.ChunkSize;

                if (fileLen + count > fileLimit)
                {
                    fileLen += SparseFormat.ChunkHeaderSize;
                    var availableForData = fileLimit - fileLen;
                    var canSplit = lastIncludedIndex < 0 || availableForData > (fileLimit / 8);

                    if (canSplit)
                    {
                        var blocksToTake = availableForData > 0
                            ? (uint)(availableForData / Header.BlockSize)
                            : 0u;

                        if (blocksToTake > 0 && blocksToTake < entry.Chunk.Header.ChunkSize)
                        {
                            var (part1, part2) = SplitChunkInternal(entry.Chunk, blocksToTake);
                            entries[i] = new ResparseEntry(entry.StartBlock, part1);
                            entries.Insert(i + 1, new ResparseEntry(entry.StartBlock + blocksToTake, part2));
                            lastIncludedIndex = i;
                        }
                    }

                    break;
                }

                fileLen += count;
                lastIncludedIndex = i;
            }

            if (lastIncludedIndex < startIndex)
            {
                throw new InvalidOperationException("Cannot fit chunk into SparseFile, please increase maxFileSize.");
            }

            var currentFile = BuildResparseFile(entries, startIndex, lastIncludedIndex);
            result.Add(currentFile);
            startIndex = lastIncludedIndex + 1;
        }

        return result;
    }

    private (SparseChunk First, SparseChunk Second) SplitChunkInternal(SparseChunk chunk, uint blocksToTake)
    {
        var h1 = chunk.Header with { ChunkSize = blocksToTake };
        var h2 = chunk.Header with { ChunkSize = chunk.Header.ChunkSize - blocksToTake };

        if (chunk.Header.ChunkType == (ushort)ChunkType.Raw)
        {
            h1 = h1 with { TotalSize = (uint)(SparseFormat.ChunkHeaderSize + ((long)blocksToTake * Header.BlockSize)) };
            h2 = h2 with { TotalSize = (uint)(SparseFormat.ChunkHeaderSize + ((long)h2.ChunkSize * Header.BlockSize)) };
        }
        else if (chunk.Header.ChunkType == (ushort)ChunkType.Fill)
        {
            h1 = h1 with { TotalSize = SparseFormat.ChunkHeaderSize + 4 };
            h2 = h2 with { TotalSize = SparseFormat.ChunkHeaderSize + 4 };
        }
        else
        {
            h1 = h1 with { TotalSize = SparseFormat.ChunkHeaderSize };
            h2 = h2 with { TotalSize = SparseFormat.ChunkHeaderSize };
        }

        var part1 = new SparseChunk(h1);
        var part2 = new SparseChunk(h2);

        if (chunk.Header.ChunkType == (ushort)ChunkType.Raw && chunk.DataProvider != null)
        {
            part1.DataProvider = chunk.DataProvider.GetSubProvider(0, (long)blocksToTake * Header.BlockSize);
            part2.DataProvider = chunk.DataProvider.GetSubProvider((long)blocksToTake * Header.BlockSize, (long)h2.ChunkSize * Header.BlockSize);
        }
        else if (chunk.Header.ChunkType == (ushort)ChunkType.Fill)
        {
            part1.FillValue = chunk.FillValue;
            part2.FillValue = chunk.FillValue;
        }

        return (part1, part2);
    }

    private SparseFile CreateNewSparseForResparse()
    {
        return new SparseFile
        {
            Header = new SparseHeader
            {
                Magic = SparseFormat.SparseHeaderMagic,
                MajorVersion = Header.MajorVersion,
                MinorVersion = Header.MinorVersion,
                FileHeaderSize = Header.FileHeaderSize,
                ChunkHeaderSize = Header.ChunkHeaderSize,
                BlockSize = Header.BlockSize
            }
        };
    }

    private void FinishCurrentResparseFile(SparseFile file)
    {
        file.Header = file.Header with
        {
            TotalChunks = (uint)file._chunks.Count,
            TotalBlocks = (uint)file._chunks.Sum(c => c.Header.ChunkSize)
        };
    }

    private sealed class ResparseEntry
    {
        public ResparseEntry(uint startBlock, SparseChunk chunk)
        {
            StartBlock = startBlock;
            Chunk = chunk;
        }

        public uint StartBlock { get; }
        public SparseChunk Chunk { get; }
    }

    private List<ResparseEntry> BuildResparseEntries()
    {
        var entries = new List<ResparseEntry>();
        uint currentBlock = 0;

        foreach (var chunk in _chunks)
        {
            switch (chunk.Header.ChunkType)
            {
                case (ushort)ChunkType.Raw:
                case (ushort)ChunkType.Fill:
                    entries.Add(new ResparseEntry(currentBlock, chunk));
                    break;
                default:
                    break;
            }

            currentBlock += chunk.Header.ChunkSize;
        }

        return entries;
    }

    private long GetSparseChunkSize(SparseChunk chunk)
    {
        return chunk.Header.ChunkType switch
        {
            (ushort)ChunkType.Raw => SparseFormat.ChunkHeaderSize + ((long)chunk.Header.ChunkSize * Header.BlockSize),
            (ushort)ChunkType.Fill => SparseFormat.ChunkHeaderSize + 4,
            _ => SparseFormat.ChunkHeaderSize
        };
    }

    private SparseChunk CreateDontCareChunk(uint blocks)
    {
        return new SparseChunk(new ChunkHeader
        {
            ChunkType = (ushort)ChunkType.DontCare,
            Reserved = 0,
            ChunkSize = blocks,
            TotalSize = SparseFormat.ChunkHeaderSize
        });
    }

    private SparseFile BuildResparseFile(List<ResparseEntry> entries, int startIndex, int endIndex)
    {
        var file = CreateNewSparseForResparse();
        file.Header = file.Header with { TotalBlocks = Header.TotalBlocks };

        uint currentBlock = 0;
        for (var i = startIndex; i <= endIndex; i++)
        {
            var entry = entries[i];
            if (entry.StartBlock > currentBlock)
            {
                file._chunks.Add(CreateDontCareChunk(entry.StartBlock - currentBlock));
            }

            file._chunks.Add(entry.Chunk);
            currentBlock = entry.StartBlock + entry.Chunk.Header.ChunkSize;
        }

        if (currentBlock < Header.TotalBlocks)
        {
            file._chunks.Add(CreateDontCareChunk(Header.TotalBlocks - currentBlock));
        }

        FinishCurrentResparseFile(file);
        return file;
    }

    /// <summary>
    /// Gets a stream for exporting a specific range of blocks from the sparse file.
    /// </summary>
    /// <param name="startBlock">The starting block index.</param>
    /// <param name="blockCount">The number of blocks to include in the export.</param>
    /// <param name="includeCrc">Whether to include a CRC-32 checksum in the exported stream.</param>
    /// <returns>A <see cref="Stream"/> representing the specified range in sparse format.</returns>
    public Stream GetExportStream(uint startBlock, uint blockCount, bool includeCrc = false)
    {
        return new SparseImageStream(this, startBlock, blockCount, includeCrc);
    }

    /// <summary>
    /// Gets a collection of streams representing the resparsed (split) image files.
    /// </summary>
    /// <param name="maxFileSize">The maximum size for each resparsed chunk file in bytes.</param>
    /// <param name="includeCrc">Whether to include CRC-32 checksums at the end of each stream.</param>
    /// <returns>An enumerable of <see cref="Stream"/> objects, one for each resulting split file.</returns>
    public IEnumerable<Stream> GetResparsedStreams(long maxFileSize, bool includeCrc = false)
    {
        foreach (var file in Resparse(maxFileSize))
        {
            yield return new SparseImageStream(file, 0, file.Header.TotalBlocks, includeCrc, false, true);
        }
    }

    /// <summary>
    /// Iterates through all chunks that contain actual data (RAW or FILL), similar to libsparse's <c>sparse_file_foreach_chunk</c>.
    /// </summary>
    /// <param name="action">The action to perform for each chunk, receiving the chunk object, start block, and block count.</param>
    public void ForEachChunk(Action<SparseChunk, uint, uint> action)
    {
        uint currentBlock = 0;
        foreach (var chunk in _chunks)
        {
            if (chunk.Header.ChunkType is (ushort)ChunkType.Raw or
                (ushort)ChunkType.Fill)
            {
                action(chunk, currentBlock, chunk.Header.ChunkSize);
            }
            currentBlock += chunk.Header.ChunkSize;
        }
    }

    /// <summary>
    /// Iterates through all chunks in the file, including DONT_CARE (skip) chunks.
    /// </summary>
    /// <param name="action">The action to perform for each chunk, receiving the chunk object, start block, and block count.</param>
    public void ForEachChunkAll(Action<SparseChunk, uint, uint> action)
    {
        uint currentBlock = 0;
        foreach (var chunk in _chunks)
        {
            action(chunk, currentBlock, chunk.Header.ChunkSize);
            currentBlock += chunk.Header.ChunkSize;
        }
    }

    /// <summary>
    /// Gets the length when written to disk (aligns with libsparse's sparse_file_len)
    /// </summary>
    public long GetLength(bool sparse, bool includeCrc)
    {
        if (!sparse)
        {
            return (long)Header.TotalBlocks * Header.BlockSize;
        }

        long length = SparseFormat.SparseHeaderSize;
        foreach (var chunk in _chunks)
        {
            length += chunk.Header.TotalSize;
        }

        var sumBlocks = (uint)_chunks.Sum(c => c.Header.ChunkSize);
        if (Header.TotalBlocks > sumBlocks)
        {
            length += SparseFormat.ChunkHeaderSize;
        }

        if (includeCrc)
        {
            length += SparseFormat.ChunkHeaderSize + 4;
        }

        return length;
    }

    /// <summary>
    /// Smart import: automatically determines if it's a Sparse or Raw image (aligns with libsparse's sparse_file_import_auto)
    /// </summary>
    /// <summary>
    /// Automatically imports a file, detecting whether it is in sparse or raw format.
    /// </summary>
    /// <param name="filePath">The path to the image file.</param>
    /// <param name="validateCrc">Whether to validate CRC-32 checksums if the file is in sparse format.</param>
    /// <param name="verbose">Whether to enable verbose logging.</param>
    /// <returns>A <see cref="SparseFile"/> instance representing the imported data.</returns>
    public static SparseFile ImportAuto(string filePath, bool validateCrc = false, bool verbose = false)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return ImportAuto(stream, validateCrc, verbose, filePath);
    }

    /// <summary>
    /// Automatically imports data from a stream, detecting whether it is in sparse or raw format.
    /// </summary>
    /// <param name="stream">The source stream to read from.</param>
    /// <param name="validateCrc">Whether to validate CRC-32 checksums if the input is in sparse format.</param>
    /// <param name="verbose">Whether to enable verbose logging.</param>
    /// <param name="filePath">An optional original file path for the source data.</param>
    /// <returns>A <see cref="SparseFile"/> instance representing the imported data.</returns>
    public static SparseFile ImportAuto(Stream stream, bool validateCrc = false, bool verbose = false, string? filePath = null)
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
                return FromStreamInternal(stream, filePath, validateCrc, verbose, null);
            }
        }

        if (filePath != null)
        {
            return FromRawFile(filePath, 4096, verbose);
        }

        // If no path is provided and it's not Sparse, treat it as a Raw stream
        var rawFile = new SparseFile(4096, stream.Length, verbose);
        rawFile.ReadFromStream(stream, SparseReadMode.Normal);
        return rawFile;
    }

    /// <summary>
    /// Creates a <see cref="SparseFile"/> by importing a raw binary file.
    /// </summary>
    /// <param name="filePath">The path to the raw binary file.</param>
    /// <param name="blockSize">The block size to use for dividing the file.</param>
    /// <param name="verbose">Whether to enable verbose logging during import.</param>
    /// <returns>A new <see cref="SparseFile"/> instance containing the raw data.</returns>
    public static SparseFile FromRawFile(string filePath, uint blockSize = 4096, bool verbose = false)
    {
        var fi = new FileInfo(filePath);
        var sparseFile = new SparseFile(blockSize, fi.Length, verbose);
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        sparseFile.ReadFromStream(fs, SparseReadMode.Normal);
        return sparseFile;
    }

    /// <summary>
    /// Reads data from a stream and incorporates it into the current sparse file according to the specified mode.
    /// </summary>
    /// <param name="stream">The stream providing source data.</param>
    /// <param name="mode">The <see cref="SparseReadMode"/> to use for parsing (e.g., Sparse, Normal, or Hole).</param>
    /// <param name="validateCrc">Whether to validate CRC-32 checksums if parsing in sparse mode.</param>
    public void ReadFromStream(Stream stream, SparseReadMode mode, bool validateCrc = false)
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

            if (Header.BlockSize != importedHeader.BlockSize)
            {
                throw new ArgumentException("Imported sparse file block size does not match the current file");
            }

            if (Verbose)
            {
                SparseLogger.LogInformation($"ReadFromStream (Sparse mode): BlockSize={importedHeader.BlockSize}, TotalBlocks={importedHeader.TotalBlocks}, TotalChunks={importedHeader.TotalChunks}");
            }

            stream.Seek(importedHeader.FileHeaderSize - SparseFormat.SparseHeaderSize, SeekOrigin.Current);

            var checksum = Crc32.Begin();
            var currentBlockStart = CurrentBlock;

            for (uint i = 0; i < importedHeader.TotalChunks; i++)
            {
                var chunkHeaderData = new byte[SparseFormat.ChunkHeaderSize];
                stream.ReadExactly(chunkHeaderData, 0, chunkHeaderData.Length);
                var chunkHeader = ChunkHeader.FromBytes(chunkHeaderData);

                if (Verbose)
                {
                    SparseLogger.LogInformation($"Imported Chunk #{i}: Type=0x{chunkHeader.ChunkType:X4}, Size={chunkHeader.ChunkSize} blocks");
                }

                stream.Seek(importedHeader.ChunkHeaderSize - SparseFormat.ChunkHeaderSize, SeekOrigin.Current);

                var dataSize = (long)chunkHeader.TotalSize - importedHeader.ChunkHeaderSize;
                var expectedRawSize = (long)chunkHeader.ChunkSize * Header.BlockSize;

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
                        _chunks.Add(chunk);
                        currentBlockStart += chunkHeader.ChunkSize;
                        break;
                    case (ushort)ChunkType.Fill:
                        var fillData = new byte[4];
                        stream.ReadExactly(fillData, 0, 4);
                        var fillValue = BinaryPrimitives.ReadUInt32LittleEndian(fillData);
                        if (validateCrc)
                        {
                            // Simplified CRC calculation
                            for (var j = 0; j < expectedRawSize / 4; j++)
                            {
                                checksum = Crc32.Update(checksum, fillData);
                            }
                        }
                        chunk.FillValue = fillValue;
                        _chunks.Add(chunk);
                        currentBlockStart += chunkHeader.ChunkSize;
                        if (dataSize > 4)
                        {
                            stream.Seek(dataSize - 4, SeekOrigin.Current);
                        }
                        break;
                    case (ushort)ChunkType.DontCare:
                        if (validateCrc)
                        {
                            var zero4 = new byte[4];
                            for (var j = 0; j < expectedRawSize / 4; j++)
                            {
                                checksum = Crc32.Update(checksum, zero4);
                            }
                        }
                        _chunks.Add(chunk);
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
        var blockSize = Header.BlockSize;
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
                    AddStreamChunk(stream, rawStart, (uint)(currentPos - rawStart));
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
                        AddDontCareChunk(currentPos - zeroStart);
                    }
                    else
                    {
                        AddFillChunk(0, currentPos - zeroStart);
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
                    AddFillChunk(currentFillValue, currentPos - fillStart);
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
            AddStreamChunk(stream, rawStart, (uint)(streamLen - rawStart));
        }
    }

    private static bool IsZeroBlock(byte[] buffer, int length)
    {
        if (length == 0)
        {
            return true;
        }

        var span = buffer.AsSpan(0, length);
        var ulongSpan = MemoryMarshal.Cast<byte, ulong>(span);
        foreach (var v in ulongSpan)
        {
            if (v != 0)
            {
                return false;
            }
        }

        for (var i = ulongSpan.Length * 8; i < length; i++)
        {
            if (buffer[i] != 0)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsFillBlock(byte[] buffer, out uint fillValue)
    {
        fillValue = 0;
        if (buffer.Length < 4)
        {
            return false;
        }

        var pattern = BinaryPrimitives.ReadUInt32LittleEndian(buffer);
        var span = buffer.AsSpan();
        var uintSpan = MemoryMarshal.Cast<byte, uint>(span);
        foreach (var v in uintSpan)
        {
            if (v != pattern)
            {
                return false;
            }
        }

        for (var i = uintSpan.Length * 4; i < buffer.Length; i++)
        {
            if (buffer[i] != (byte)(pattern >> (i % 4 * 8)))
            {
                return false;
            }
        }

        fillValue = pattern;
        return true;
    }
}
