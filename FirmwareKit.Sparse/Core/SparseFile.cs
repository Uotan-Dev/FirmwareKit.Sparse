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
    public static SparseHeader PeekHeader(string filePath) => SparseReader.PeekHeader(filePath);

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
    /// Adds a chunk without sorting or overlap check. Used internally for loading or resparsing.
    /// </summary>
    internal void AddChunkRaw(SparseChunk chunk) => _chunks.Add(chunk);

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
            var last = _chunks[_chunks.Count - 1];
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
    public static SparseFile FromStream(Stream stream, bool validateCrc = false, bool verbose = false, ISparseLogger? logger = null)
        => SparseReader.FromStream(stream, validateCrc, verbose, logger);

    /// <summary>
    /// Loads a sparse file from a byte array buffer.
    /// </summary>
    public static SparseFile FromBuffer(byte[] buffer, bool validateCrc = false, bool verbose = false, ISparseLogger? logger = null)
        => SparseReader.FromBuffer(buffer, validateCrc, verbose, logger);

    /// <summary>
    /// Loads a sparse file from the specified image file.
    /// </summary>
    public static SparseFile FromImageFile(string filePath, bool validateCrc = false, bool verbose = false, ISparseLogger? logger = null)
        => SparseReader.FromImageFile(filePath, validateCrc, verbose, logger);

    /// <summary>
    /// Loads a sparse file from the specified stream asynchronously.
    /// </summary>
    public static Task<SparseFile> FromStreamAsync(Stream stream, bool validateCrc = false, bool verbose = false, ISparseLogger? logger = null, CancellationToken cancellationToken = default)
        => SparseReader.FromStreamAsync(stream, validateCrc, verbose, logger, cancellationToken);

    /// <summary>
    /// Loads a sparse file from a byte array buffer asynchronously.
    /// </summary>
    public static Task<SparseFile> FromBufferAsync(byte[] buffer, bool validateCrc = false, bool verbose = false, ISparseLogger? logger = null, CancellationToken cancellationToken = default)
        => SparseReader.FromBufferAsync(buffer, validateCrc, verbose, logger, cancellationToken);

    /// <summary>
    /// Loads a sparse file from the specified image file asynchronously.
    /// </summary>
    public static Task<SparseFile> FromImageFileAsync(string filePath, bool validateCrc = false, bool verbose = false, ISparseLogger? logger = null, CancellationToken cancellationToken = default)
        => SparseReader.FromImageFileAsync(filePath, validateCrc, verbose, logger, cancellationToken);


    /// <summary>
    /// Automatically imports a file, detecting whether it is in sparse or raw format.
    /// </summary>
    public static SparseFile ImportAuto(string filePath, bool validateCrc = false, bool verbose = false, ISparseLogger? logger = null)
        => SparseReader.ImportAuto(filePath, validateCrc, verbose, logger);

    /// <summary>
    /// Automatically imports data from a stream, detecting whether it is in sparse or raw format.
    /// </summary>
    public static SparseFile ImportAuto(Stream stream, bool validateCrc = false, bool verbose = false, ISparseLogger? logger = null)
        => SparseReader.ImportAuto(stream, validateCrc, verbose, logger);

    /// <summary>
    /// Creates a <see cref="SparseFile"/> by importing a raw binary file.
    /// </summary>
    public static SparseFile FromRawFile(string filePath, uint blockSize = 4096, bool verbose = false, ISparseLogger? logger = null)
        => SparseReader.FromRawFile(filePath, blockSize, verbose, logger);

    /// <summary>
    /// Resizes the sparse file total logical size.
    /// </summary>
    public void Resize(long newSize)
    {
        var newTotalBlocks = (uint)((newSize + Header.BlockSize - 1) / Header.BlockSize);
        Header = Header with { TotalBlocks = newTotalBlocks };
    }

    /// <summary>
    /// Writes the sparse file content to the specified stream.
    /// </summary>
    public void WriteToStream(Stream stream, bool sparse = true, bool gzip = false, bool includeCrc = false)
        => SparseWriter.WriteToStream(this, stream, sparse, gzip, includeCrc);

    /// <summary>
    /// Writes the sparse file content to the specified stream asynchronously.
    /// </summary>
    public Task WriteToStreamAsync(Stream stream, bool sparse = true, bool gzip = false, bool includeCrc = false, CancellationToken cancellationToken = default)
        => SparseWriter.WriteToStreamAsync(this, stream, sparse, gzip, includeCrc, cancellationToken);

    /// <summary>
    /// Writes the raw (uncompressed) data of the sparse file to the specified stream.
    /// </summary>
    public void WriteRawToStream(Stream stream, bool sparseMode = false)
        => SparseWriter.WriteRawToStream(this, stream, sparseMode);

    /// <summary>
    /// Writes the raw (uncompressed) data of the sparse file to the specified stream asynchronously.
    /// </summary>
    public Task WriteRawToStreamAsync(Stream stream, bool sparseMode = false, CancellationToken cancellationToken = default)
        => SparseWriter.WriteRawToStreamAsync(this, stream, sparseMode, cancellationToken);

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
    public void WriteWithCallback(SparseWriteCallback callback, bool sparse = true, bool includeCrc = false)
        => SparseWriter.WriteWithCallback(this, callback, sparse, includeCrc);

    /// <summary>
    /// Splits the current sparse file into multiple sparse files, each not exceeding the specified maximum size.
    /// </summary>
    public List<SparseFile> Resparse(long maxFileSize)
        => SparseResparser.Resparse(this, maxFileSize);

    /// <summary>
    /// Gets a stream for exporting a specific range of blocks from the sparse file.
    /// </summary>
    public Stream GetExportStream(uint startBlock, uint blockCount, bool includeCrc = false)
        => new SparseImageStream(this, startBlock, blockCount, includeCrc);

    /// <summary>
    /// Gets a collection of streams representing the resparsed (split) image files.
    /// </summary>
    public IEnumerable<Stream> GetResparsedStreams(long maxFileSize, bool includeCrc = false)
    {
        foreach (var file in Resparse(maxFileSize))
        {
            yield return new SparseImageStream(file, 0, file.Header.TotalBlocks, includeCrc, false, true);
        }
    }

    /// <summary>
    /// Gets the length when written to disk.
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
    /// Adds a RAW chunk that references data from an external file.
    /// </summary>
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
                if (partSize == 0) partSize = remaining;
            }

            var chunkBlocks = (uint)((partSize + blockSize - 1) / blockSize);
            var chunkHeader = new ChunkHeader
            {
                ChunkType = (ushort)ChunkType.Raw,
                ChunkSize = chunkBlocks,
                TotalSize = (uint)(SparseFormat.ChunkHeaderSize + ((long)chunkBlocks * blockSize))
            };

            AddChunkSorted(new SparseChunk(chunkHeader)
            {
                StartBlock = currentBlockStart,
                DataProvider = new FileDataProvider(filePath, currentOffset, partSize)
            });
            currentBlockStart += chunkBlocks;
            remaining -= (uint)partSize;
            currentOffset += partSize;
        }
    }

    /// <summary>
    /// Adds a RAW chunk using data from a byte array buffer.
    /// </summary>
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
                if (partSize == 0) partSize = remaining;
            }

            var chunkBlocks = (uint)((partSize + blockSize - 1) / blockSize);
            var chunkHeader = new ChunkHeader
            {
                ChunkType = (ushort)ChunkType.Raw,
                ChunkSize = chunkBlocks,
                TotalSize = (uint)(SparseFormat.ChunkHeaderSize + ((long)chunkBlocks * blockSize))
            };

            AddChunkSorted(new SparseChunk(chunkHeader)
            {
                StartBlock = currentBlockStart,
                DataProvider = new MemoryDataProvider(data, currentOffset, (int)partSize)
            });
            currentBlockStart += chunkBlocks;
            remaining -= (uint)partSize;
            currentOffset += (int)partSize;
        }
    }

    /// <summary>
    /// Adds a RAW chunk using data from a stream.
    /// </summary>
    public void AddStreamChunk(Stream stream, long offset, uint size, uint? blockIndex = null, bool leaveOpen = true)
    {
        var blockSize = Header.BlockSize;
        var totalBlocks = (size + blockSize - 1) / blockSize;
        var currentBlockStart = GetNextBlockAndCheckOverlap(blockIndex, totalBlocks);

        var remaining = size;
        var currentOffset = offset;

        while (remaining > 0)
        {
            var partSize = Math.Min(remaining, (uint)SparseFormat.MaxChunkDataSize);
            if (partSize < remaining && partSize % blockSize != 0)
            {
                partSize = partSize / blockSize * blockSize;
                if (partSize == 0) partSize = remaining;
            }

            var chunkBlocks = (uint)((partSize + blockSize - 1) / blockSize);
            var chunkHeader = new ChunkHeader
            {
                ChunkType = (ushort)ChunkType.Raw,
                ChunkSize = chunkBlocks,
                TotalSize = (uint)(SparseFormat.ChunkHeaderSize + ((long)chunkBlocks * blockSize))
            };

            AddChunkSorted(new SparseChunk(chunkHeader)
            {
                StartBlock = currentBlockStart,
                DataProvider = new StreamDataProvider(stream, currentOffset, partSize, leaveOpen)
            });
            currentBlockStart += chunkBlocks;
            remaining -= (uint)partSize;
            currentOffset += partSize;
        }
    }

    /// <summary>
    /// Adds a FILL chunk that repeats a 4-byte value.
    /// </summary>
    public void AddFillChunk(uint fillValue, long size, uint? blockIndex = null)
    {
        var blockSize = Header.BlockSize;
        var totalBlocks = (uint)((size + blockSize - 1) / blockSize);
        var currentBlockStart = GetNextBlockAndCheckOverlap(blockIndex, totalBlocks);

        var remaining = size;

        while (remaining > 0)
        {
            var partSize = Math.Min(remaining, (long)0x00FFFFFF * blockSize);
            var partBlocks = Math.Min((uint)((partSize + blockSize - 1) / blockSize), 0x00FFFFFFu);
            var actualPartSize = (long)partBlocks * blockSize;

            AddChunkSorted(new SparseChunk(new ChunkHeader
            {
                ChunkType = (ushort)ChunkType.Fill,
                ChunkSize = partBlocks,
                TotalSize = SparseFormat.ChunkHeaderSize + 4
            })
            {
                StartBlock = currentBlockStart,
                FillValue = fillValue
            });

            currentBlockStart += partBlocks;
            remaining -= actualPartSize;
        }
    }

    /// <summary>
    /// Adds a DONT_CARE (skip) chunk representing an unallocated or empty region.
    /// </summary>
    public void AddDontCareChunk(long size, uint? blockIndex = null)
    {
        var blockSize = Header.BlockSize;
        var totalBlocks = (uint)((size + blockSize - 1) / blockSize);
        var currentBlockStart = GetNextBlockAndCheckOverlap(blockIndex, totalBlocks);

        var remaining = size;
        while (remaining > 0)
        {
            var partBlocks = Math.Min((uint)((remaining + blockSize - 1) / blockSize), 0x00FFFFFFu);
            var actualPartSize = (long)partBlocks * blockSize;

            AddChunkSorted(new SparseChunk(new ChunkHeader
            {
                ChunkType = (ushort)ChunkType.DontCare,
                ChunkSize = partBlocks,
                TotalSize = SparseFormat.ChunkHeaderSize
            })
            {
                StartBlock = currentBlockStart
            });

            currentBlockStart += partBlocks;
            remaining -= actualPartSize;
        }
    }

    /// <summary>
    /// Iterates through all chunks that contain actual data (RAW or FILL).
    /// </summary>
    public void ForEachChunk(Action<SparseChunk, uint, uint> action)
    {
        uint currentBlock = 0;
        foreach (var chunk in _chunks)
        {
            if (chunk.Header.ChunkType is (ushort)ChunkType.Raw or (ushort)ChunkType.Fill)
            {
                action(chunk, currentBlock, chunk.Header.ChunkSize);
            }
            currentBlock += chunk.Header.ChunkSize;
        }
    }

    /// <summary>
    /// Iterates through all chunks in the file.
    /// </summary>
    public void ForEachChunkAll(Action<SparseChunk, uint, uint> action)
    {
        uint currentBlock = 0;
        foreach (var chunk in _chunks)
        {
            action(chunk, currentBlock, chunk.Header.ChunkSize);
            currentBlock += chunk.Header.ChunkSize;
        }
    }

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
            AddDontCareChunk((long)(blockIndex.Value - CurrentBlock) * Header.BlockSize, CurrentBlock);
        }

        return start;
    }

    private void AddChunkSorted(SparseChunk chunk)
    {
        var index = _chunks.FindIndex(c => c.StartBlock > chunk.StartBlock);
        if (index == -1) _chunks.Add(chunk);
        else _chunks.Insert(index, chunk);
    }

    /// <summary>
    /// Releases all resources used by the <see cref="SparseFile"/> instance.
    /// </summary>
    public void Dispose()
    {
        foreach (var chunk in _chunks) chunk.Dispose();
        _chunks.Clear();
    }
}
