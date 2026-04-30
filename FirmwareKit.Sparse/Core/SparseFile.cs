namespace FirmwareKit.Sparse.Core;

/// <summary>
/// Represents a sparse file structure, providing methods to read, write, and manipulate Android sparse images.
/// </summary>
public class SparseFile : IDisposable
{
    private readonly List<SparseChunk> _chunks = new List<SparseChunk>();

    /// <summary>
    /// Peek at the sparse header of a file without reading the entire content.
    /// </summary>
    /// <param name="filePath">Path to the sparse image file to inspect (string).</param>
    /// <returns>The parsed <see cref="SparseHeader"/> read from the file.</returns>
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
    /// Gets or sets the starting block used when exporting this file as raw data.
    /// </summary>
    internal uint? RawExportStartBlock { get; set; }

    /// <summary>
    /// Gets the list of sparse chunks in the file.
    /// </summary>
    public IReadOnlyList<SparseChunk> Chunks => _chunks;

    /// <summary>
    /// Adds a chunk without sorting or overlap check. Used internally for loading or resparsing.
    /// </summary>
    internal void AddChunkRaw(SparseChunk chunk) => _chunks.Add(chunk);

    /// <summary>
    /// Removes the last chunk from the internal chunk list. Used by readers to normalize parsed files.
    /// </summary>
    internal void RemoveLastChunk()
    {
        if (_chunks.Count > 0) _chunks.RemoveAt(_chunks.Count - 1);
    }

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
            SparseChunk last = _chunks[_chunks.Count - 1];
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
    /// Initialize a new <see cref="SparseFile"/> with the provided block size and total logical size.
    /// </summary>
    /// <param name="blockSize">Size of a single block in bytes (uint).</param>
    /// <param name="totalSize">Total logical size of the image in bytes (long).</param>
    /// <param name="verbose">Enable verbose logging if true (bool).</param>
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
    /// Load a sparse file from the provided <see cref="Stream"/>.
    /// </summary>
    /// <param name="stream">Input stream containing the sparse image data.</param>
    /// <param name="validateCrc">If true, CRC validation will be performed (bool).</param>
    /// <param name="verbose">If true, enable verbose logging (bool).</param>
    /// <param name="logger">Optional logger instance to capture diagnostic messages.</param>
    /// <returns>A <see cref="SparseFile"/> instance parsed from the stream.</returns>
    public static SparseFile FromStream(Stream stream, bool validateCrc = false, bool verbose = false, ISparseLogger? logger = null)
        => SparseReader.FromStream(stream, validateCrc, verbose, logger);

    /// <summary>
    /// Load a sparse file from a byte array that contains the whole image.
    /// </summary>
    /// <param name="buffer">Byte array containing the sparse image data.</param>
    /// <param name="validateCrc">If true, CRC validation will be performed (bool).</param>
    /// <param name="verbose">If true, enable verbose logging (bool).</param>
    /// <param name="logger">Optional logger instance to capture diagnostic messages.</param>
    /// <returns>A <see cref="SparseFile"/> instance parsed from the buffer.</returns>
    public static SparseFile FromBuffer(byte[] buffer, bool validateCrc = false, bool verbose = false, ISparseLogger? logger = null)
        => SparseReader.FromBuffer(buffer, validateCrc, verbose, logger);

    /// <summary>
    /// Load a sparse file directly from an image file on disk.
    /// </summary>
    /// <param name="filePath">Path to the image file on disk (string).</param>
    /// <param name="validateCrc">If true, CRC validation will be performed (bool).</param>
    /// <param name="verbose">If true, enable verbose logging (bool).</param>
    /// <param name="logger">Optional logger instance for diagnostics.</param>
    /// <returns>A <see cref="SparseFile"/> instance parsed from the file.</returns>
    public static SparseFile FromImageFile(string filePath, bool validateCrc = false, bool verbose = false, ISparseLogger? logger = null)
        => SparseReader.FromImageFile(filePath, validateCrc, verbose, logger);

    /// <summary>
    /// Asynchronously load a sparse file from the provided stream.
    /// </summary>
    /// <param name="stream">Input stream containing the sparse image data.</param>
    /// <param name="validateCrc">If true, CRC validation will be performed (bool).</param>
    /// <param name="verbose">If true, enable verbose logging (bool).</param>
    /// <param name="logger">Optional logger instance to capture diagnostic messages.</param>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>A task that resolves to a <see cref="SparseFile"/> parsed from the stream.</returns>
    public static Task<SparseFile> FromStreamAsync(Stream stream, bool validateCrc = false, bool verbose = false, ISparseLogger? logger = null, CancellationToken cancellationToken = default)
        => SparseReader.FromStreamAsync(stream, validateCrc, verbose, logger, cancellationToken);

    /// <summary>
    /// Asynchronously load a sparse file from the provided byte array buffer.
    /// </summary>
    /// <param name="buffer">Byte array that contains the sparse image data.</param>
    /// <param name="validateCrc">If true, CRC validation will be performed (bool).</param>
    /// <param name="verbose">If true, enable verbose logging (bool).</param>
    /// <param name="logger">Optional logger instance to capture diagnostic messages.</param>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>A task that resolves to a <see cref="SparseFile"/> parsed from the buffer.</returns>
    public static Task<SparseFile> FromBufferAsync(byte[] buffer, bool validateCrc = false, bool verbose = false, ISparseLogger? logger = null, CancellationToken cancellationToken = default)
        => SparseReader.FromBufferAsync(buffer, validateCrc, verbose, logger, cancellationToken);

    /// <summary>
    /// Asynchronously load a sparse file from an image file on disk.
    /// </summary>
    /// <param name="filePath">Path to the image file on disk (string).</param>
    /// <param name="validateCrc">If true, CRC validation will be performed (bool).</param>
    /// <param name="verbose">If true, enable verbose logging (bool).</param>
    /// <param name="logger">Optional logger instance for diagnostics.</param>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>A task that resolves to a <see cref="SparseFile"/> parsed from the file.</returns>
    public static Task<SparseFile> FromImageFileAsync(string filePath, bool validateCrc = false, bool verbose = false, ISparseLogger? logger = null, CancellationToken cancellationToken = default)
        => SparseReader.FromImageFileAsync(filePath, validateCrc, verbose, logger, cancellationToken);


    /// <summary>
    /// Automatically import an image file, detecting whether it is sparse or raw.
    /// </summary>
    /// <param name="filePath">Path to the input file (string).</param>
    /// <param name="validateCrc">If true, CRC validation will be performed (bool).</param>
    /// <param name="verbose">If true, enable verbose logging (bool).</param>
    /// <param name="logger">Optional logger for diagnostic messages.</param>
    /// <returns>A <see cref="SparseFile"/> representing the imported image.</returns>
    public static SparseFile ImportAuto(string filePath, bool validateCrc = false, bool verbose = false, ISparseLogger? logger = null)
        => SparseReader.ImportAuto(filePath, validateCrc, verbose, logger);

    /// <summary>
    /// Automatically import image data from a stream, detecting whether it is sparse or raw.
    /// </summary>
    /// <param name="stream">Input stream to read image data from.</param>
    /// <param name="validateCrc">If true, CRC validation will be performed (bool).</param>
    /// <param name="verbose">If true, enable verbose logging (bool).</param>
    /// <param name="logger">Optional logger for diagnostic messages.</param>
    /// <returns>A <see cref="SparseFile"/> representing the imported image.</returns>
    public static SparseFile ImportAuto(Stream stream, bool validateCrc = false, bool verbose = false, ISparseLogger? logger = null)
        => SparseReader.ImportAuto(stream, validateCrc, verbose, logger);

    /// <summary>
    /// Create a <see cref="SparseFile"/> by importing a raw binary file and converting it to sparse representation.
    /// </summary>
    /// <param name="filePath">Path to the raw binary file (string).</param>
    /// <param name="blockSize">Block size to use for conversion, in bytes (uint).</param>
    /// <param name="verbose">Enable verbose logging if true (bool).</param>
    /// <param name="logger">Optional logger instance for diagnostics.</param>
    /// <returns>A <see cref="SparseFile"/> converted from the raw file.</returns>
    public static SparseFile FromRawFile(string filePath, uint blockSize = 4096, bool verbose = false, ISparseLogger? logger = null)
        => SparseReader.FromRawFile(filePath, blockSize, verbose, logger);

    /// <summary>
    /// Resize the sparse file's total logical size to the provided value.
    /// </summary>
    /// <param name="newSize">New total size in bytes for the sparse image (long).</param>
    public void Resize(long newSize)
    {
        var newTotalBlocks = (uint)((newSize + Header.BlockSize - 1) / Header.BlockSize);
        Header = Header with { TotalBlocks = newTotalBlocks };
    }

    /// <summary>
    /// Write the sparse file to the given stream.
    /// </summary>
    /// <param name="stream">Destination stream to write the sparse image to.</param>
    /// <param name="sparse">If true, write in sparse format; otherwise write raw data.</param>
    /// <param name="gzip">If true, gzip-compress the written output (bool).</param>
    /// <param name="includeCrc">If true, include CRC32 chunk per chunk (bool).</param>
    public void WriteToStream(Stream stream, bool sparse = true, bool gzip = false, bool includeCrc = false)
        => SparseWriter.WriteToStream(this, stream, sparse, gzip, includeCrc);

    /// <summary>
    /// Asynchronously write the sparse file to the provided stream.
    /// </summary>
    /// <param name="stream">Destination stream to write the sparse image to.</param>
    /// <param name="sparse">If true, write in sparse format; otherwise write raw data.</param>
    /// <param name="gzip">If true, gzip-compress the written output (bool).</param>
    /// <param name="includeCrc">If true, include CRC32 chunk per chunk (bool).</param>
    /// <param name="cancellationToken">Token to cancel the asynchronous write operation.</param>
    /// <returns>A task representing the asynchronous write operation.</returns>
    public Task WriteToStreamAsync(Stream stream, bool sparse = true, bool gzip = false, bool includeCrc = false, CancellationToken cancellationToken = default)
        => SparseWriter.WriteToStreamAsync(this, stream, sparse, gzip, includeCrc, cancellationToken);

    /// <summary>
    /// Write the raw (uncompressed) data represented by this sparse file to the stream.
    /// </summary>
    /// <param name="stream">Destination stream to receive raw data.</param>
    /// <param name="sparseMode">If true, preserve sparse metadata while streaming raw data (bool).</param>
    public void WriteRawToStream(Stream stream, bool sparseMode = false)
        => SparseWriter.WriteRawToStream(this, stream, sparseMode);

    /// <summary>
    /// Asynchronously write the raw (uncompressed) data represented by this sparse file to the stream.
    /// </summary>
    /// <param name="stream">Destination stream to receive raw data.</param>
    /// <param name="sparseMode">If true, preserve sparse metadata while streaming raw data (bool).</param>
    /// <param name="cancellationToken">Token to cancel the asynchronous write operation.</param>
    /// <returns>A task representing the asynchronous raw write operation.</returns>
    public Task WriteRawToStreamAsync(Stream stream, bool sparseMode = false, CancellationToken cancellationToken = default)
        => SparseWriter.WriteRawToStreamAsync(this, stream, sparseMode, cancellationToken);

    /// <summary>
    /// Delegate used as a callback when streaming or writing sparse data blocks.
    /// </summary>
    /// <param name="data">Byte array containing the block data, or <c>null</c> to indicate a gap.</param>
    /// <param name="length">Number of valid bytes in <paramref name="data"/> to process (int).</param>
    /// <returns>An integer status code; negative values typically indicate failure.</returns>
    public delegate int SparseWriteCallback(byte[]? data, int length);

    /// <summary>
    /// Write the sparse file using a custom callback for each data block instead of writing to a stream.
    /// </summary>
    /// <param name="callback">Callback invoked for each data block.</param>
    /// <param name="sparse">If true, write in sparse format; otherwise write raw blocks.</param>
    /// <param name="includeCrc">If true, include CRC32 chunks (bool).</param>
    public void WriteWithCallback(SparseWriteCallback callback, bool sparse = true, bool includeCrc = false)
        => SparseWriter.WriteWithCallback(this, callback, sparse, includeCrc);

    /// <summary>
    /// Split this sparse file into multiple smaller sparse files whose size does not exceed <paramref name="maxFileSize"/>.
    /// </summary>
    /// <param name="maxFileSize">Maximum size in bytes for each resparsed file (long).</param>
    /// <returns>A sequence of <see cref="SparseFile"/> instances representing the split images.</returns>
    public IEnumerable<SparseFile> Resparse(long maxFileSize)
        => SparseResparser.Resparse(this, maxFileSize);

    /// <summary>
    /// Split a sparse file from stream into multiple smaller sparse files using streaming parsing.
    /// Optimized for 32-bit AOT environments handling large files (up to 16GB).
    /// </summary>
    /// <param name="stream">Stream containing the sparse file data</param>
    /// <param name="maxFileSize">Maximum size in bytes for each resparsed file (long).</param>
    /// <param name="leaveOpen">Whether to leave the stream open after processing (bool).</param>
    /// <returns>A sequence of <see cref="SparseFile"/> instances representing the split images.</returns>
    public static IEnumerable<SparseFile> ResparseStreamed(Stream stream, long maxFileSize, bool leaveOpen = false)
        => SparseResparserOptimized.ResparseStreamed(stream, maxFileSize, leaveOpen);

    /// <summary>
    /// Split a sparse file from disk into multiple smaller sparse files using memory-mapped I/O.
    /// Optimized for 32-bit AOT environments handling large files (up to 16GB).
    /// </summary>
    /// <param name="filePath">Path to the sparse file (string).</param>
    /// <param name="maxFileSize">Maximum size in bytes for each resparsed file (long).</param>
    /// <returns>A sequence of <see cref="SparseFile"/> instances representing the split images.</returns>
    public static IEnumerable<SparseFile> ResparseMapped(string filePath, long maxFileSize)
        => SparseResparserOptimized.ResparseMapped(filePath, maxFileSize);

    /// <summary>
    /// Get a <see cref="Stream"/> for exporting a specific range of blocks from this sparse file.
    /// </summary>
    /// <param name="startBlock">Index of the first block to export (uint).</param>
    /// <param name="blockCount">Number of blocks to include in the exported stream (uint).</param>
    /// <param name="includeCrc">If true, include CRC32 chunks in the exported data (bool).</param>
    /// <returns>A stream that provides the requested exported data range.</returns>
    public Stream GetExportStream(uint startBlock, uint blockCount, bool includeCrc = false)
        => new SparseImageStream(this, startBlock, blockCount, includeCrc, fullRange: false);

    /// <summary>
    /// Get a collection of streams representing the resparsed (split) image files.
    /// </summary>
    /// <param name="maxFileSize">Maximum size in bytes for each split file (long).</param>
    /// <param name="includeCrc">If true, include CRC32 chunks in each stream (bool).</param>
    /// <returns>An enumerable of streams for each resparsed image part.</returns>
    public IEnumerable<Stream> GetResparsedStreams(long maxFileSize, bool includeCrc = false)
    {
        foreach (SparseFile file in Resparse(maxFileSize))
        {
            yield return new SparseImageStream(file, 0, file.Header.TotalBlocks, includeCrc, false, true);
        }
    }

    /// <summary>
    /// Calculate the length in bytes when this sparse file is written to disk.
    /// </summary>
    /// <param name="sparse">If true, calculate length for sparse format; otherwise raw format (bool).</param>
    /// <param name="includeCrc">If true, include CRC32 chunk overhead (bool).</param>
    /// <returns>The number of bytes required to write this file.</returns>
    public long GetLength(bool sparse, bool includeCrc)
    {
        if (!sparse)
        {
            return (long)Header.TotalBlocks * Header.BlockSize;
        }

        long length = Header.FileHeaderSize;
        uint totalChunkBlocks = 0;
        foreach (SparseChunk chunk in _chunks)
        {
            length += chunk.Header.TotalSize;
            totalChunkBlocks += chunk.Header.ChunkSize;
        }

        if (Header.TotalBlocks > totalChunkBlocks)
        {
            length += Header.ChunkHeaderSize;
        }

        if (includeCrc)
        {
            length += Header.ChunkHeaderSize + 4;
        }

        return length;
    }

    /// <summary>
    /// Add a RAW chunk that references data from an external file.
    /// </summary>
    /// <param name="filePath">Path to the external file containing the chunk data (string).</param>
    /// <param name="offset">Byte offset within the external file where the chunk starts (long).</param>
    /// <param name="size">Number of bytes to include in the chunk (uint).</param>
    /// <param name="blockIndex">Optional explicit starting block index; if null, appended at the current end (uint?).</param>
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
                TotalSize = (uint)(Header.ChunkHeaderSize + ((long)chunkBlocks * blockSize))
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
    /// Add a RAW chunk using data from an in-memory byte array buffer.
    /// Optimized version that minimizes memory allocations.
    /// </summary>
    /// <param name="data">Byte array with source data for the chunk.</param>
    /// <param name="blockIndex">Optional explicit starting block index; if null, appended at the current end (uint?).</param>
    public void AddRawChunk(byte[] data, uint? blockIndex = null)
    {
        var blockSize = Header.BlockSize;
        var totalBlocks = (uint)((data.Length + blockSize - 1) / blockSize);
        var currentBlockStart = GetNextBlockAndCheckOverlap(blockIndex, totalBlocks);

        // Optimized: Use a single MemoryDataProvider for the entire data
        var provider = new MemoryDataProvider(data, 0, data.Length);

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
                TotalSize = (uint)(Header.ChunkHeaderSize + ((long)chunkBlocks * blockSize))
            };

            // Use GetSubProvider instead of creating new MemoryDataProvider each time
            AddChunkSorted(new SparseChunk(chunkHeader)
            {
                StartBlock = currentBlockStart,
                DataProvider = provider.GetSubProvider(currentOffset, (long)partSize)
            });
            currentBlockStart += chunkBlocks;
            remaining -= (uint)partSize;
            currentOffset += (int)partSize;
        }
    }

    /// <summary>
    /// Add a RAW chunk using data read from a stream.
    /// </summary>
    /// <param name="stream">Source stream to read chunk data from.</param>
    /// <param name="offset">Byte offset within the stream to start reading (long).</param>
    /// <param name="size">Number of bytes to include in the chunk (uint).</param>
    /// <param name="blockIndex">Optional explicit starting block index; if null, appended at the current end (uint?).</param>
    /// <param name="leaveOpen">If true, do not close the input stream after reading (bool).</param>
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
                TotalSize = (uint)(Header.ChunkHeaderSize + ((long)chunkBlocks * blockSize))
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
    /// Add a FILL chunk that repeats a 4-byte pattern to cover the specified range.
    /// </summary>
    /// <param name="fillValue">4-byte value to repeat (uint).</param>
    /// <param name="size">Total size in bytes that the fill chunk should cover.</param>
    /// <param name="blockIndex">Optional explicit starting block index; if null, appended at the current end (uint?).</param>
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
                TotalSize = (uint)(Header.ChunkHeaderSize + 4)
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
    /// Add a DONT_CARE (skip) chunk representing an unallocated or empty region.
    /// </summary>
    /// <param name="size">Size in bytes of the unallocated region to represent.</param>
    /// <param name="blockIndex">Optional explicit starting block index; if null, appended at the current end (uint?).</param>
    public void AddDontCareChunk(long size, uint? blockIndex = null)
    {
        var totalBlocks = (uint)((size + Header.BlockSize - 1) / Header.BlockSize);
        var currentBlockStart = GetNextBlockAndCheckOverlap(blockIndex, totalBlocks);
        AddDontCareChunkInternal(size, currentBlockStart);
    }

    /// <summary>
    /// Iterate through all chunks that contain actual data (RAW or FILL) and invoke the provided action.
    /// </summary>
    /// <param name="action">Action to invoke for each data chunk. Parameters: chunk, startBlock, chunkSize.</param>
    public void ForEachChunk(Action<SparseChunk, uint, uint> action)
    {
        uint currentBlock = 0;
        foreach (SparseChunk chunk in _chunks)
        {
            if (chunk.Header.ChunkType is (ushort)ChunkType.Raw or (ushort)ChunkType.Fill)
            {
                action(chunk, currentBlock, chunk.Header.ChunkSize);
            }
            currentBlock += chunk.Header.ChunkSize;
        }
    }

    /// <summary>
    /// Iterate through every chunk in the sparse file and invoke the provided action for each.
    /// </summary>
    /// <param name="action">Action to invoke for each chunk. Parameters: chunk, startBlock, chunkSize.</param>
    public void ForEachChunkAll(Action<SparseChunk, uint, uint> action)
    {
        uint currentBlock = 0;
        foreach (SparseChunk chunk in _chunks)
        {
            action(chunk, currentBlock, chunk.Header.ChunkSize);
            currentBlock += chunk.Header.ChunkSize;
        }
    }

    private uint GetNextBlockAndCheckOverlap(uint? blockIndex, uint sizeInBlocks)
    {
        var start = blockIndex ?? CurrentBlock;
        var end = start + sizeInBlocks;

        // Optimized: Use binary search to find potential overlapping chunks
        int count = _chunks.Count;
        if (count > 0)
        {
            int left = 0, right = count - 1;

            // Find the first chunk that might overlap
            while (left <= right)
            {
                int mid = left + ((right - left) >> 1);
                uint midEnd = _chunks[mid].StartBlock + _chunks[mid].Header.ChunkSize;

                if (midEnd <= start)
                    left = mid + 1;
                else
                    right = mid - 1;
            }

            // Check a small window around the potential overlap point
            int checkStart = Math.Max(0, left - 2);
            int checkEnd = Math.Min(count, left + 2);

            for (int i = checkStart; i < checkEnd; i++)
            {
                SparseChunk chunk = _chunks[i];
                var chunkEnd = chunk.StartBlock + chunk.Header.ChunkSize;
                if (start < chunkEnd && end > chunk.StartBlock)
                {
                    throw new ArgumentException($"Block region [{start}, {end}) overlaps with existing chunk [{chunk.StartBlock}, {chunkEnd}).");
                }
            }
        }

        if (blockIndex.HasValue && blockIndex.Value > CurrentBlock)
        {
            // Internal call to avoid recursion and repeated checks
            AddDontCareChunkInternal((long)(blockIndex.Value - CurrentBlock) * Header.BlockSize, CurrentBlock);
        }

        return start;
    }

    private void AddDontCareChunkInternal(long size, uint startBlock)
    {
        var blockSize = Header.BlockSize;
        var remaining = size;
        var currentBlockStart = startBlock;
        while (remaining > 0)
        {
            var partBlocks = Math.Min((uint)((remaining + blockSize - 1) / blockSize), 0x00FFFFFFu);

            AddChunkSorted(new SparseChunk(new ChunkHeader
            {
                ChunkType = (ushort)ChunkType.DontCare,
                ChunkSize = partBlocks,
                TotalSize = (uint)Header.ChunkHeaderSize
            })
            {
                StartBlock = currentBlockStart
            });

            currentBlockStart += partBlocks;
            remaining -= (long)partBlocks * blockSize;
        }
    }

    private void AddChunkSorted(SparseChunk chunk)
    {
        // Optimized: Most use cases add chunks sequentially
        int count = _chunks.Count;
        if (count == 0 || chunk.StartBlock >= _chunks[count - 1].StartBlock)
        {
            _chunks.Add(chunk);
            return;
        }

        // Fast path for append-like operations
        if (chunk.StartBlock > _chunks[count - 1].StartBlock)
        {
            _chunks.Add(chunk);
            return;
        }

        // Binary search for insertion point
        int left = 0, right = count - 1;
        while (left <= right)
        {
            int mid = left + ((right - left) >> 1);
            uint midBlock = _chunks[mid].StartBlock;
            if (midBlock == chunk.StartBlock)
            {
                _chunks.Insert(mid, chunk);
                return;
            }
            if (midBlock < chunk.StartBlock)
                left = mid + 1;
            else
                right = mid - 1;
        }
        _chunks.Insert(left, chunk);
    }

    private class SparseChunkComparer : IComparer<SparseChunk>
    {
        public static readonly SparseChunkComparer Instance = new SparseChunkComparer();
        public int Compare(SparseChunk? x, SparseChunk? y) => x!.StartBlock.CompareTo(y!.StartBlock);
    }

    /// <summary>
    /// Releases all resources used by the <see cref="SparseFile"/> instance.
    /// </summary>
    public void Dispose()
    {
        foreach (SparseChunk chunk in _chunks) chunk.Dispose();
        _chunks.Clear();
    }
}
