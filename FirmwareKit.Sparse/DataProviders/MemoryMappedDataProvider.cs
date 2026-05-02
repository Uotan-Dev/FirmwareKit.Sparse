using System.Buffers;
using System.IO.MemoryMappedFiles;

namespace FirmwareKit.Sparse.DataProviders;

/// <summary>
/// Provides access to a region of a file using memory-mapped I/O.
/// Optimized for 32-bit AOT environments handling large files (up to 16GB).
/// <para>使用内存映射 I/O 提供对文件区域的访问。
/// 针对 32 位 AOT 环境处理大文件（最大 16GB）进行了优化。</para>
/// </summary>
public class MemoryMappedDataProvider : ISparseDataProvider, IDisposable
{
    private readonly MemoryMappedFile _mmf;
    private readonly MemoryMappedViewAccessor _accessor;
    private readonly long _offset;
    private readonly long _length;

    /// <summary>
    /// Initializes a new <see cref="MemoryMappedDataProvider"/> from a file path.
    /// <para>从文件路径初始化新的 MemoryMappedDataProvider。</para>
    /// </summary>
    /// <param name="filePath">Path to the source file. <para>源文件的路径。</para></param>
    /// <param name="offset">Byte offset within the file. <para>文件内的字节偏移。</para></param>
    /// <param name="length">Length of the data segment. <para>数据段的长度。</para></param>
    public MemoryMappedDataProvider(string filePath, long offset, long length)
    {
        _offset = offset;
        _length = length;

        _mmf = MemoryMappedFile.CreateFromFile(
            filePath,
            FileMode.Open,
            null,
            offset + length,
            MemoryMappedFileAccess.Read);

        _accessor = _mmf.CreateViewAccessor(offset, length, MemoryMappedFileAccess.Read);
    }

    /// <summary>
    /// Initializes a new <see cref="MemoryMappedDataProvider"/> from an existing <see cref="MemoryMappedFile"/>.
    /// <para>从现有的 MemoryMappedFile 初始化新的 MemoryMappedDataProvider。</para>
    /// </summary>
    /// <param name="mmf">The memory-mapped file to read from. <para>要读取的内存映射文件。</para></param>
    /// <param name="offset">Byte offset within the file. <para>文件内的字节偏移。</para></param>
    /// <param name="length">Length of the data segment. <para>数据段的长度。</para></param>
    private MemoryMappedDataProvider(MemoryMappedFile mmf, long offset, long length)
    {
        _mmf = mmf;
        _offset = offset;
        _length = length;
        _accessor = mmf.CreateViewAccessor(offset, length, MemoryMappedFileAccess.Read);
    }

    /// <summary>
    /// Gets the length of the data segment in bytes.
    /// <para>获取数据段的字节长度。</para>
    /// </summary>
    public long Length => _length;

    /// <summary>
    /// Synchronously writes the data to the specified stream.
    /// <para>同步将数据写入指定流。</para>
    /// </summary>
    /// <param name="stream">Destination stream to write the data to. <para>要写入数据的目标流。</para></param>
    public void WriteTo(Stream stream)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            long remaining = _length;
            long pos = 0;

            while (remaining > 0)
            {
                int toRead = (int)Math.Min(buffer.Length, remaining);
                _accessor.ReadArray(pos, buffer, 0, toRead);
                stream.Write(buffer, 0, toRead);
                pos += toRead;
                remaining -= toRead;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Asynchronously writes the data to the specified stream.
    /// <para>异步将数据写入指定流。</para>
    /// </summary>
    /// <param name="stream">Destination stream to write the data to. <para>要写入数据的目标流。</para></param>
    /// <param name="cancellationToken">Cancellation token for the operation. <para>操作的取消令牌。</para></param>
    /// <returns>A task that completes when the write finishes. <para>写入完成时结束的任务。</para></returns>
    public async Task WriteToAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            long remaining = _length;
            long pos = 0;

            while (remaining > 0 && !cancellationToken.IsCancellationRequested)
            {
                int toRead = (int)Math.Min(buffer.Length, remaining);
                _accessor.ReadArray(pos, buffer, 0, toRead);
                await stream.WriteAsync(buffer, 0, toRead, cancellationToken).ConfigureAwait(false);
                pos += toRead;
                remaining -= toRead;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Reads data into a byte array buffer.
    /// <para>读取数据到字节数组缓冲区。</para>
    /// </summary>
    /// <param name="inOffset">Byte offset relative to this provider's start. <para>相对于此提供程序起始的字节偏移。</para></param>
    /// <param name="buffer">Destination buffer to receive bytes. <para>接收字节的目标缓冲区。</para></param>
    /// <param name="bufferOffset">Offset in the destination buffer. <para>目标缓冲区中的偏移。</para></param>
    /// <param name="count">Maximum number of bytes to read. <para>最大读取字节数。</para></param>
    /// <returns>The number of bytes actually read. <para>实际读取的字节数。</para></returns>
    public int Read(long inOffset, byte[] buffer, int bufferOffset, int count)
    {
        if (inOffset >= _length)
            return 0;

        int toRead = (int)Math.Min(count, _length - inOffset);
        _accessor.ReadArray(inOffset, buffer, bufferOffset, toRead);
        return toRead;
    }

    /// <summary>
    /// Reads data into a <see cref="Span{Byte}"/>.
    /// <para>读取数据到 Span{Byte}。</para>
    /// </summary>
    /// <param name="inOffset">Byte offset relative to this provider's start. <para>相对于此提供程序起始的字节偏移。</para></param>
    /// <param name="buffer">Span that receives the data. <para>接收数据的 Span。</para></param>
    /// <returns>The number of bytes actually read. <para>实际读取的字节数。</para></returns>
    public int Read(long inOffset, Span<byte> buffer)
    {
        if (inOffset >= _length)
            return 0;

        int toRead = (int)Math.Min(buffer.Length, _length - inOffset);
        byte[] tempBuffer = ArrayPool<byte>.Shared.Rent(toRead);
        try
        {
            _accessor.SafeMemoryMappedViewHandle.ReadArray<byte>((ulong)inOffset, tempBuffer, 0, toRead);
            tempBuffer.AsSpan(0, toRead).CopyTo(buffer);
            return toRead;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(tempBuffer);
        }
    }

    /// <summary>
    /// Creates a sub-provider for a sub-range of this provider's data.
    /// <para>创建此提供程序数据子范围的子提供程序。</para>
    /// </summary>
    /// <param name="subOffset">Byte offset relative to this provider's start. <para>相对于此提供程序起始的字节偏移。</para></param>
    /// <param name="subLength">Length in bytes of the sub-range. <para>子范围的字节长度。</para></param>
    /// <returns>An <see cref="ISparseDataProvider"/> for the requested sub-range. <para>请求子范围的 ISparseDataProvider。</para></returns>
    public ISparseDataProvider GetSubProvider(long subOffset, long subLength)
    {
        return new MemoryMappedDataProvider(_mmf, _offset + subOffset, subLength);
    }

    /// <summary>
    /// Releases the memory-mapped view accessor and file resources.
    /// <para>释放内存映射视图访问器和文件资源。</para>
    /// </summary>
    public void Dispose()
    {
        _accessor.Dispose();
        _mmf.Dispose();
    }
}
