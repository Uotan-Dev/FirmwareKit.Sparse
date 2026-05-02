namespace FirmwareKit.Sparse.Core;

/// <summary>
/// Represents a sparse file structure, providing methods to read, write, and manipulate Android sparse images.
/// <para>表示稀疏文件结构，提供读取、写入和操作 Android 稀疏镜像的方法。</para>
/// </summary>
public partial class SparseFile : IDisposable
{
    /// <summary>
    /// Peeks at the sparse header of a file without reading the entire content.
    /// <para>查看文件的稀疏头部，无需读取整个内容。</para>
    /// </summary>
    /// <param name="filePath">Path to the sparse image file to inspect. <para>要检查的稀疏镜像文件路径。</para></param>
    /// <returns>The parsed <see cref="SparseHeader"/> read from the file. <para>从文件解析的 SparseHeader。</para></returns>
    public static SparseHeader PeekHeader(string filePath) => SparseReader.PeekHeader(filePath);

    /// <summary>
    /// Loads a sparse file from the provided <see cref="Stream"/>.
    /// <para>从提供的 Stream 加载稀疏文件。</para>
    /// </summary>
    /// <param name="stream">Input stream containing the sparse image data. <para>包含稀疏镜像数据的输入流。</para></param>
    /// <param name="validateCrc">If true, CRC validation will be performed. <para>如果为 true，将执行 CRC 验证。</para></param>
    /// <param name="verbose">If true, enable verbose logging. <para>如果为 true，启用详细日志。</para></param>
    /// <param name="logger">Optional logger instance to capture diagnostic messages. <para>可选的日志记录器实例。</para></param>
    /// <returns>A <see cref="SparseFile"/> instance parsed from the stream. <para>从流解析的 SparseFile 实例。</para></returns>
    public static SparseFile FromStream(Stream stream, bool validateCrc = false, bool verbose = false, ISparseLogger? logger = null)
        => SparseReader.FromStream(stream, validateCrc, verbose, logger);

    /// <summary>
    /// Loads a sparse file from a byte array that contains the whole image.
    /// <para>从包含整个镜像的字节数组加载稀疏文件。</para>
    /// </summary>
    /// <param name="buffer">Byte array containing the sparse image data. <para>包含稀疏镜像数据的字节数组。</para></param>
    /// <param name="validateCrc">If true, CRC validation will be performed. <para>如果为 true，将执行 CRC 验证。</para></param>
    /// <param name="verbose">If true, enable verbose logging. <para>如果为 true，启用详细日志。</para></param>
    /// <param name="logger">Optional logger instance to capture diagnostic messages. <para>可选的日志记录器实例。</para></param>
    /// <returns>A <see cref="SparseFile"/> instance parsed from the buffer. <para>从缓冲区解析的 SparseFile 实例。</para></returns>
    public static SparseFile FromBuffer(byte[] buffer, bool validateCrc = false, bool verbose = false, ISparseLogger? logger = null)
        => SparseReader.FromBuffer(buffer, validateCrc, verbose, logger);

    /// <summary>
    /// Loads a sparse file directly from an image file on disk.
    /// <para>从磁盘上的镜像文件直接加载稀疏文件。</para>
    /// </summary>
    /// <param name="filePath">Path to the image file on disk. <para>磁盘上镜像文件的路径。</para></param>
    /// <param name="validateCrc">If true, CRC validation will be performed. <para>如果为 true，将执行 CRC 验证。</para></param>
    /// <param name="verbose">If true, enable verbose logging. <para>如果为 true，启用详细日志。</para></param>
    /// <param name="logger">Optional logger instance for diagnostics. <para>可选的日志记录器实例。</para></param>
    /// <returns>A <see cref="SparseFile"/> instance parsed from the file. <para>从文件解析的 SparseFile 实例。</para></returns>
    public static SparseFile FromImageFile(string filePath, bool validateCrc = false, bool verbose = false, ISparseLogger? logger = null)
        => SparseReader.FromImageFile(filePath, validateCrc, verbose, logger);

    /// <summary>
    /// Asynchronously loads a sparse file from the provided stream.
    /// <para>异步从提供的流加载稀疏文件。</para>
    /// </summary>
    /// <param name="stream">Input stream containing the sparse image data. <para>包含稀疏镜像数据的输入流。</para></param>
    /// <param name="validateCrc">If true, CRC validation will be performed. <para>如果为 true，将执行 CRC 验证。</para></param>
    /// <param name="verbose">If true, enable verbose logging. <para>如果为 true，启用详细日志。</para></param>
    /// <param name="logger">Optional logger instance to capture diagnostic messages. <para>可选的日志记录器实例。</para></param>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation. <para>取消异步操作的令牌。</para></param>
    /// <returns>A task that resolves to a <see cref="SparseFile"/> parsed from the stream. <para>解析为从流解析的 SparseFile 的任务。</para></returns>
    public static Task<SparseFile> FromStreamAsync(Stream stream, bool validateCrc = false, bool verbose = false, ISparseLogger? logger = null, CancellationToken cancellationToken = default)
        => SparseReader.FromStreamAsync(stream, validateCrc, verbose, logger, cancellationToken);

    /// <summary>
    /// Asynchronously loads a sparse file from the provided byte array buffer.
    /// <para>异步从提供的字节数组缓冲区加载稀疏文件。</para>
    /// </summary>
    /// <param name="buffer">Byte array that contains the sparse image data. <para>包含稀疏镜像数据的字节数组。</para></param>
    /// <param name="validateCrc">If true, CRC validation will be performed. <para>如果为 true，将执行 CRC 验证。</para></param>
    /// <param name="verbose">If true, enable verbose logging. <para>如果为 true，启用详细日志。</para></param>
    /// <param name="logger">Optional logger instance to capture diagnostic messages. <para>可选的日志记录器实例。</para></param>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation. <para>取消异步操作的令牌。</para></param>
    /// <returns>A task that resolves to a <see cref="SparseFile"/> parsed from the buffer. <para>解析为从缓冲区解析的 SparseFile 的任务。</para></returns>
    public static Task<SparseFile> FromBufferAsync(byte[] buffer, bool validateCrc = false, bool verbose = false, ISparseLogger? logger = null, CancellationToken cancellationToken = default)
        => SparseReader.FromBufferAsync(buffer, validateCrc, verbose, logger, cancellationToken);

    /// <summary>
    /// Asynchronously loads a sparse file from an image file on disk.
    /// <para>异步从磁盘上的镜像文件加载稀疏文件。</para>
    /// </summary>
    /// <param name="filePath">Path to the image file on disk. <para>磁盘上镜像文件的路径。</para></param>
    /// <param name="validateCrc">If true, CRC validation will be performed. <para>如果为 true，将执行 CRC 验证。</para></param>
    /// <param name="verbose">If true, enable verbose logging. <para>如果为 true，启用详细日志。</para></param>
    /// <param name="logger">Optional logger instance for diagnostics. <para>可选的日志记录器实例。</para></param>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation. <para>取消异步操作的令牌。</para></param>
    /// <returns>A task that resolves to a <see cref="SparseFile"/> parsed from the file. <para>解析为从文件解析的 SparseFile 的任务。</para></returns>
    public static Task<SparseFile> FromImageFileAsync(string filePath, bool validateCrc = false, bool verbose = false, ISparseLogger? logger = null, CancellationToken cancellationToken = default)
        => SparseReader.FromImageFileAsync(filePath, validateCrc, verbose, logger, cancellationToken);

    /// <summary>
    /// Automatically imports an image file, detecting whether it is sparse or raw.
    /// <para>自动导入镜像文件，检测其为稀疏格式还是原始格式。</para>
    /// </summary>
    /// <param name="filePath">Path to the input file. <para>输入文件路径。</para></param>
    /// <param name="validateCrc">If true, CRC validation will be performed. <para>如果为 true，将执行 CRC 验证。</para></param>
    /// <param name="verbose">If true, enable verbose logging. <para>如果为 true，启用详细日志。</para></param>
    /// <param name="logger">Optional logger for diagnostic messages. <para>可选的日志记录器。</para></param>
    /// <returns>A <see cref="SparseFile"/> representing the imported image. <para>表示导入镜像的 SparseFile。</para></returns>
    public static SparseFile ImportAuto(string filePath, bool validateCrc = false, bool verbose = false, ISparseLogger? logger = null)
        => SparseReader.ImportAuto(filePath, validateCrc, verbose, logger);

    /// <summary>
    /// Automatically imports image data from a stream, detecting whether it is sparse or raw.
    /// <para>自动从流导入镜像数据，检测其为稀疏格式还是原始格式。</para>
    /// </summary>
    /// <param name="stream">Input stream to read image data from. <para>读取镜像数据的输入流。</para></param>
    /// <param name="validateCrc">If true, CRC validation will be performed. <para>如果为 true，将执行 CRC 验证。</para></param>
    /// <param name="verbose">If true, enable verbose logging. <para>如果为 true，启用详细日志。</para></param>
    /// <param name="logger">Optional logger for diagnostic messages. <para>可选的日志记录器。</para></param>
    /// <returns>A <see cref="SparseFile"/> representing the imported image. <para>表示导入镜像的 SparseFile。</para></returns>
    public static SparseFile ImportAuto(Stream stream, bool validateCrc = false, bool verbose = false, ISparseLogger? logger = null)
        => SparseReader.ImportAuto(stream, validateCrc, verbose, logger);

    /// <summary>
    /// Creates a <see cref="SparseFile"/> by importing a raw binary file and converting it to sparse representation.
    /// <para>通过导入原始二进制文件并将其转换为稀疏表示来创建 SparseFile。</para>
    /// </summary>
    /// <param name="filePath">Path to the raw binary file. <para>原始二进制文件的路径。</para></param>
    /// <param name="blockSize">Block size to use for conversion, in bytes. <para>转换使用的块大小（字节）。</para></param>
    /// <param name="verbose">Enable verbose logging if true. <para>如果为 true，启用详细日志。</para></param>
    /// <param name="logger">Optional logger instance for diagnostics. <para>可选的日志记录器实例。</para></param>
    /// <returns>A <see cref="SparseFile"/> converted from the raw file. <para>从原始文件转换的 SparseFile。</para></returns>
    public static SparseFile FromRawFile(string filePath, uint blockSize = 4096, bool verbose = false, ISparseLogger? logger = null)
        => SparseReader.FromRawFile(filePath, blockSize, verbose, logger);

    /// <summary>
    /// Splits a sparse file from stream into multiple smaller sparse files using streaming parsing.
    /// Optimized for 32-bit AOT environments handling large files (up to 16GB).
    /// <para>使用流式解析将流中的稀疏文件拆分为多个较小的稀疏文件。针对处理大文件（最大 16GB）的 32 位 AOT 环境优化。</para>
    /// </summary>
    /// <param name="stream">Stream containing the sparse file data. <para>包含稀疏文件数据的流。</para></param>
    /// <param name="maxFileSize">Maximum size in bytes for each resparsed file. <para>每个拆分文件的最大字节数。</para></param>
    /// <param name="leaveOpen">Whether to leave the stream open after processing. <para>处理后是否保持流打开。</para></param>
    /// <returns>A sequence of <see cref="SparseFile"/> instances representing the split images. <para>表示拆分镜像的 SparseFile 实例序列。</para></returns>
    public static IEnumerable<SparseFile> ResparseStreamed(Stream stream, long maxFileSize, bool leaveOpen = false)
        => SparseResparser.ResparseStreamed(stream, maxFileSize, leaveOpen);

    /// <summary>
    /// Splits a sparse file from disk into multiple smaller sparse files using memory-mapped I/O.
    /// Optimized for 32-bit AOT environments handling large files (up to 16GB).
    /// <para>使用内存映射 I/O 将磁盘上的稀疏文件拆分为多个较小的稀疏文件。针对处理大文件（最大 16GB）的 32 位 AOT 环境优化。</para>
    /// </summary>
    /// <param name="filePath">Path to the sparse file. <para>稀疏文件的路径。</para></param>
    /// <param name="maxFileSize">Maximum size in bytes for each resparsed file. <para>每个拆分文件的最大字节数。</para></param>
    /// <returns>A sequence of <see cref="SparseFile"/> instances representing the split images. <para>表示拆分镜像的 SparseFile 实例序列。</para></returns>
    public static IEnumerable<SparseFile> ResparseMapped(string filePath, long maxFileSize)
        => SparseResparser.ResparseMapped(filePath, maxFileSize);

    private readonly List<SparseChunk> _chunks = new List<SparseChunk>();

    /// <summary>
    /// Initializes a new instance of the <see cref="SparseFile"/> class with default settings.
    /// <para>使用默认设置初始化 SparseFile 类的新实例。</para>
    /// </summary>
    public SparseFile()
    {
        Header = SparseHeader.CreateDefault(4096, 0);
    }

    /// <summary>
    /// Initializes a new <see cref="SparseFile"/> with the provided block size and total logical size.
    /// <para>使用提供的块大小和总逻辑大小初始化 SparseFile。</para>
    /// </summary>
    /// <param name="blockSize">Size of a single block in bytes. <para>单个块的字节大小。</para></param>
    /// <param name="totalSize">Total logical size of the image in bytes. <para>镜像的总逻辑大小（字节）。</para></param>
    /// <param name="verbose">Enable verbose logging if true. <para>如果为 true，启用详细日志。</para></param>
    public SparseFile(uint blockSize, long totalSize, bool verbose = false)
    {
        Verbose = verbose;
        var totalBlocks = (uint)((totalSize + blockSize - 1) / blockSize);
        Header = SparseHeader.CreateDefault(blockSize, totalBlocks);
    }

    /// <summary>
    /// Gets or sets the sparse header.
    /// <para>获取或设置稀疏文件头部。</para>
    /// </summary>
    public SparseHeader Header { get; set; }

    /// <summary>
    /// Gets or sets the logger for this instance. If null, <see cref="SparseLogger.Instance"/> is used.
    /// <para>获取或设置此实例的日志记录器。如果为 null，则使用 SparseLogger.Instance。</para>
    /// </summary>
    public ISparseLogger? Logger { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether verbose logging is enabled.
    /// <para>获取或设置是否启用详细日志记录。</para>
    /// </summary>
    public bool Verbose { get; set; } = false;

    /// <summary>
    /// Gets the list of sparse chunks in the file.
    /// <para>获取文件中的稀疏数据块列表。</para>
    /// </summary>
    public IReadOnlyList<SparseChunk> Chunks => _chunks;

    /// <summary>
    /// Gets the total number of blocks added (representing the current maximum logical extent).
    /// <para>获取已添加的总块数（表示当前最大逻辑范围）。</para>
    /// </summary>
    public uint CurrentBlock
    {
        get
        {
            if (_chunks.Count == 0)
            {
                return 0;
            }
            SparseChunk last = _chunks[_chunks.Count - 1];
            return last.StartBlock + last.Header.ChunkSize;
        }
    }

    /// <summary>
    /// Resizes the sparse file's total logical size to the provided value.
    /// <para>将稀疏文件的总逻辑大小调整为提供的值。</para>
    /// </summary>
    /// <param name="newSize">New total size in bytes for the sparse image. <para>稀疏镜像的新总大小（字节）。</para></param>
    public void Resize(long newSize)
    {
        var newTotalBlocks = (uint)((newSize + Header.BlockSize - 1) / Header.BlockSize);
        Header = Header with { TotalBlocks = newTotalBlocks };
    }

    /// <summary>
    /// Writes the sparse file to the given stream.
    /// <para>将稀疏文件写入给定的流。</para>
    /// </summary>
    /// <param name="stream">Destination stream to write the sparse image to. <para>写入稀疏镜像的目标流。</para></param>
    /// <param name="sparse">If true, write in sparse format; otherwise write raw data. <para>如果为 true，以稀疏格式写入；否则写入原始数据。</para></param>
    /// <param name="gzip">If true, gzip-compress the written output. <para>如果为 true，使用 gzip 压缩输出。</para></param>
    /// <param name="includeCrc">If true, include CRC32 chunk per chunk. <para>如果为 true，每个数据块包含 CRC32。</para></param>
    public void WriteToStream(Stream stream, bool sparse = true, bool gzip = false, bool includeCrc = false)
        => SparseWriter.WriteToStream(this, stream, sparse, gzip, includeCrc);

    /// <summary>
    /// Asynchronously writes the sparse file to the provided stream.
    /// <para>异步将稀疏文件写入提供的流。</para>
    /// </summary>
    /// <param name="stream">Destination stream to write the sparse image to. <para>写入稀疏镜像的目标流。</para></param>
    /// <param name="sparse">If true, write in sparse format; otherwise write raw data. <para>如果为 true，以稀疏格式写入；否则写入原始数据。</para></param>
    /// <param name="gzip">If true, gzip-compress the written output. <para>如果为 true，使用 gzip 压缩输出。</para></param>
    /// <param name="includeCrc">If true, include CRC32 chunk per chunk. <para>如果为 true，每个数据块包含 CRC32。</para></param>
    /// <param name="cancellationToken">Token to cancel the asynchronous write operation. <para>取消异步写入操作的令牌。</para></param>
    /// <returns>A task representing the asynchronous write operation. <para>表示异步写入操作的任务。</para></returns>
    public Task WriteToStreamAsync(Stream stream, bool sparse = true, bool gzip = false, bool includeCrc = false, CancellationToken cancellationToken = default)
        => SparseWriter.WriteToStreamAsync(this, stream, sparse, gzip, includeCrc, cancellationToken);

    /// <summary>
    /// Writes the raw (uncompressed) data represented by this sparse file to the stream.
    /// <para>将此稀疏文件表示的原始（未压缩）数据写入流。</para>
    /// </summary>
    /// <param name="stream">Destination stream to receive raw data. <para>接收原始数据的目标流。</para></param>
    /// <param name="sparseMode">If true, preserve sparse metadata while streaming raw data. <para>如果为 true，在流式传输原始数据时保留稀疏元数据。</para></param>
    public void WriteRawToStream(Stream stream, bool sparseMode = false)
        => SparseWriter.WriteRawToStream(this, stream, sparseMode);

    /// <summary>
    /// Asynchronously writes the raw (uncompressed) data represented by this sparse file to the stream.
    /// <para>异步将此稀疏文件表示的原始（未压缩）数据写入流。</para>
    /// </summary>
    /// <param name="stream">Destination stream to receive raw data. <para>接收原始数据的目标流。</para></param>
    /// <param name="sparseMode">If true, preserve sparse metadata while streaming raw data. <para>如果为 true，在流式传输原始数据时保留稀疏元数据。</para></param>
    /// <param name="cancellationToken">Token to cancel the asynchronous write operation. <para>取消异步写入操作的令牌。</para></param>
    /// <returns>A task representing the asynchronous raw write operation. <para>表示异步原始写入操作的任务。</para></returns>
    public Task WriteRawToStreamAsync(Stream stream, bool sparseMode = false, CancellationToken cancellationToken = default)
        => SparseWriter.WriteRawToStreamAsync(this, stream, sparseMode, cancellationToken);

    /// <summary>
    /// Writes the sparse file using a custom callback for each data block instead of writing to a stream.
    /// <para>使用自定义回调为每个数据块写入稀疏文件，而非写入流。</para>
    /// </summary>
    /// <param name="callback">Callback invoked for each data block. <para>每个数据块调用的回调。</para></param>
    /// <param name="sparse">If true, write in sparse format; otherwise write raw blocks. <para>如果为 true，以稀疏格式写入；否则写入原始块。</para></param>
    /// <param name="includeCrc">If true, include CRC32 chunks. <para>如果为 true，包含 CRC32 数据块。</para></param>
    public void WriteWithCallback(SparseWriteCallback callback, bool sparse = true, bool includeCrc = false)
        => SparseWriter.WriteWithCallback(this, callback, sparse, includeCrc);

    /// <summary>
    /// Splits this sparse file into multiple smaller sparse files whose size does not exceed <paramref name="maxFileSize"/>.
    /// <para>将此稀疏文件拆分为多个不超过 maxFileSize 的较小稀疏文件。</para>
    /// </summary>
    /// <param name="maxFileSize">Maximum size in bytes for each resparsed file. <para>每个拆分文件的最大字节数。</para></param>
    /// <returns>A sequence of <see cref="SparseFile"/> instances representing the split images. <para>表示拆分镜像的 SparseFile 实例序列。</para></returns>
    public IEnumerable<SparseFile> Resparse(long maxFileSize)
        => SparseResparser.Resparse(this, maxFileSize);

    /// <summary>
    /// Gets a <see cref="Stream"/> for exporting a specific range of blocks from this sparse file.
    /// <para>获取用于导出此稀疏文件中指定块范围的 Stream。</para>
    /// </summary>
    /// <param name="startBlock">Index of the first block to export. <para>要导出的第一个块的索引。</para></param>
    /// <param name="blockCount">Number of blocks to include in the exported stream. <para>导出流中包含的块数。</para></param>
    /// <param name="includeCrc">If true, include CRC32 chunks in the exported data. <para>如果为 true，在导出数据中包含 CRC32 数据块。</para></param>
    /// <returns>A stream that provides the requested exported data range. <para>提供请求导出数据范围的流。</para></returns>
    public Stream GetExportStream(uint startBlock, uint blockCount, bool includeCrc = false)
        => new SparseImageStream(this, startBlock, blockCount, includeCrc, fullRange: false);

    /// <summary>
    /// Gets a collection of streams representing the resparsed (split) image files.
    /// <para>获取表示重新拆分镜像文件的流集合。</para>
    /// </summary>
    /// <param name="maxFileSize">Maximum size in bytes for each split file. <para>每个拆分文件的最大字节数。</para></param>
    /// <param name="includeCrc">If true, include CRC32 chunks in each stream. <para>如果为 true，在每个流中包含 CRC32 数据块。</para></param>
    /// <returns>An enumerable of streams for each resparsed image part. <para>每个拆分镜像部分的流的可枚举集合。</para></returns>
    public IEnumerable<Stream> GetResparsedStreams(long maxFileSize, bool includeCrc = false)
    {
        foreach (SparseFile file in Resparse(maxFileSize))
        {
            yield return new SparseImageStream(file, 0, file.Header.TotalBlocks, includeCrc, false, true);
        }
    }

    /// <summary>
    /// Calculates the length in bytes when this sparse file is written to disk.
    /// <para>计算此稀疏文件写入磁盘时的字节长度。</para>
    /// </summary>
    /// <param name="sparse">If true, calculate length for sparse format; otherwise raw format. <para>如果为 true，计算稀疏格式的长度；否则计算原始格式。</para></param>
    /// <param name="includeCrc">If true, include CRC32 chunk overhead. <para>如果为 true，包含 CRC32 数据块开销。</para></param>
    /// <returns>The number of bytes required to write this file. <para>写入此文件所需的字节数。</para></returns>
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
    /// Releases all resources used by the <see cref="SparseFile"/> instance.
    /// <para>释放 SparseFile 实例使用的所有资源。</para>
    /// </summary>
    public void Dispose()
    {
        foreach (SparseChunk chunk in _chunks) chunk.Dispose();
        _chunks.Clear();
    }

    /// <summary>
    /// Gets or sets the starting block used when exporting this file as raw data.
    /// <para>获取或设置将此文件导出为原始数据时使用的起始块。</para>
    /// </summary>
    internal uint? RawExportStartBlock { get; set; }

    /// <summary>
    /// Gets or sets the total block count used for raw export. When set, WriteRawToStream
    /// uses this value instead of Header.TotalBlocks to determine the raw output length.
    /// <para>获取或设置用于原始导出的总块数。设置后，WriteRawToStream 使用此值
    /// 而非 Header.TotalBlocks 来确定原始输出长度。</para>
    /// </summary>
    internal uint? RawExportTotalBlocks { get; set; }

    /// <summary>
    /// Adds a chunk without sorting or overlap check. Used internally for loading or resparsing.
    /// <para>添加数据块而不进行排序或重叠检查。用于内部加载或重新拆分。</para>
    /// </summary>
    /// <param name="chunk">The chunk to add. <para>要添加的数据块。</para></param>
    internal void AddChunkRaw(SparseChunk chunk) => _chunks.Add(chunk);

    /// <summary>
    /// Removes the last chunk from the internal chunk list. Used by readers to normalize parsed files.
    /// <para>从内部数据块列表中移除最后一个数据块。由读取器用于规范化解析后的文件。</para>
    /// </summary>
    internal void RemoveLastChunk()
    {
        if (_chunks.Count > 0) _chunks.RemoveAt(_chunks.Count - 1);
    }

    /// <summary>
    /// Delegate used as a callback when streaming or writing sparse data blocks.
    /// <para>在流式传输或写入稀疏数据块时用作回调的委托。</para>
    /// </summary>
    /// <param name="data">Byte array containing the block data, or <c>null</c> to indicate a gap. <para>包含块数据的字节数组，或 null 表示间隙。</para></param>
    /// <param name="length">Number of valid bytes in <paramref name="data"/> to process. <para>data 中要处理的有效字节数。</para></param>
    /// <returns>An integer status code; negative values typically indicate failure. <para>整数状态码；负值通常表示失败。</para></returns>
    public delegate int SparseWriteCallback(byte[]? data, int length);
}
