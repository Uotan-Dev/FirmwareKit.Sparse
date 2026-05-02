using System.Buffers;

namespace FirmwareKit.Sparse.IO;

/// <summary>
/// Provides streaming parsing of sparse files for memory-efficient processing.
/// Optimized for 32-bit AOT environments handling large files (up to 16GB).
/// <para>提供稀疏文件的流式解析，实现内存高效处理。
/// 针对 32 位 AOT 环境处理大文件（最大 16GB）进行了优化。</para>
/// </summary>
public class SparseStreamParser : IDisposable
{
    private readonly Stream _stream;
    private readonly bool _leaveOpen;
    private readonly SparseHeader _header;
    private long _position;
    private bool _disposed;

    /// <summary>
    /// Gets the sparse header read from the stream.
    /// <para>获取从流中读取的稀疏文件头部。</para>
    /// </summary>
    public SparseHeader Header => _header;

    /// <summary>
    /// Initializes a new <see cref="SparseStreamParser"/> from the specified stream.
    /// <para>从指定流初始化新的 SparseStreamParser。</para>
    /// </summary>
    /// <param name="stream">The source stream containing sparse image data. <para>包含稀疏镜像数据的源流。</para></param>
    /// <param name="leaveOpen">If true, the stream will not be closed when this parser is disposed. <para>如果为 true，释放解析器时不关闭流。</para></param>
    public SparseStreamParser(Stream stream, bool leaveOpen = false)
    {
        _stream = stream;
        _leaveOpen = leaveOpen;
        _position = 0;

        _header = ReadHeader();
        _position = _header.FileHeaderSize;
    }

    /// <summary>
    /// Reads the sparse header from the beginning of the stream.
    /// <para>从流起始位置读取稀疏文件头部。</para>
    /// </summary>
    /// <returns>The parsed <see cref="SparseHeader"/>. <para>解析后的 SparseHeader。</para></returns>
    private SparseHeader ReadHeader()
    {
        var buffer = ArrayPool<byte>.Shared.Rent(512);
        try
        {
            _stream.Position = 0;
            int read = _stream.Read(buffer, 0, SparseFormat.SparseHeaderSize);
            if (read < SparseFormat.SparseHeaderSize)
                throw new InvalidDataException("Invalid sparse header");

            return SparseHeader.FromBytes(buffer);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Lazily enumerates all chunks in the sparse file.
    /// <para>延迟枚举稀疏文件中的所有数据块。</para>
    /// </summary>
    /// <returns>An enumerable of <see cref="SparseChunk"/> instances. <para>SparseChunk 实例的可枚举集合。</para></returns>
    public IEnumerable<SparseChunk> EnumerateChunks()
    {
        _stream.Position = _position;

        for (uint i = 0; i < _header.TotalChunks; i++)
        {
            var chunk = ReadNextChunk();
            if (chunk == null)
                break;

            yield return chunk;
        }
    }

    /// <summary>
    /// Reads the next chunk from the stream.
    /// <para>从流中读取下一个数据块。</para>
    /// </summary>
    /// <returns>The next <see cref="SparseChunk"/>, or null if the end of stream is reached. <para>下一个 SparseChunk，如果到达流末尾则返回 null。</para></returns>
    public SparseChunk? ReadNextChunk()
    {
        if (_stream.Position >= _stream.Length)
            return null;

        var headerBuffer = ArrayPool<byte>.Shared.Rent(_header.ChunkHeaderSize);
        try
        {
            int read = _stream.Read(headerBuffer, 0, _header.ChunkHeaderSize);
            if (read < _header.ChunkHeaderSize)
                return null;

            var chunkHeader = ChunkHeader.FromBytes(headerBuffer);

            if ((chunkHeader.ChunkType & 0x8000) != 0)
            {
                _stream.Seek(4, SeekOrigin.Current);
            }

            var chunk = new SparseChunk(chunkHeader);

            switch ((ChunkType)chunkHeader.ChunkType)
            {
                case ChunkType.Raw:
                    long dataSize = chunkHeader.TotalSize - _header.ChunkHeaderSize;
                    var dataBuffer = new byte[dataSize];
                    _stream.ReadExactly(dataBuffer, 0, (int)dataSize);
                    chunk.DataProvider = new MemoryDataProvider(dataBuffer, 0, (int)dataSize);
                    break;

                case ChunkType.Fill:
                    Span<byte> fillBuffer = stackalloc byte[4];
                    _stream.ReadExactly(fillBuffer);
                    chunk.FillValue = BinaryPrimitives.ReadUInt32LittleEndian(fillBuffer);
                    break;

                case ChunkType.DontCare:
                    break;
            }

            _position = _stream.Position;
            return chunk;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(headerBuffer);
        }
    }

    /// <summary>
    /// Releases the stream resources if <see cref="_leaveOpen"/> is false.
    /// <para>如果 leaveOpen 为 false，则释放流资源。</para>
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            if (!_leaveOpen)
                _stream.Dispose();
            _disposed = true;
        }
    }

}
