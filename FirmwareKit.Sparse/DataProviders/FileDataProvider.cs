namespace FirmwareKit.Sparse.DataProviders;

/// <summary>
/// Provides access to a region of a file as an <see cref="ISparseDataProvider"/>.
/// Reads from a file on disk starting at a given offset and exposes a fixed-length view.
/// <para>提供对文件区域的访问，实现 ISparseDataProvider 接口。
/// 从磁盘上的文件读取数据，从给定偏移开始，暴露固定长度的视图。</para>
/// </summary>
public class FileDataProvider : ISparseDataProvider
{
    private readonly string _filePath;
    private readonly long _offset;
    private readonly long _length;

    /// <summary>
    /// Initializes a new <see cref="FileDataProvider"/> for the given file segment.
    /// <para>为给定的文件段初始化新的 FileDataProvider。</para>
    /// </summary>
    /// <param name="filePath">Path to the source file on disk. <para>磁盘上源文件的路径。</para></param>
    /// <param name="offset">Byte offset within the file where the segment starts. <para>文件中段起始的字节偏移。</para></param>
    /// <param name="length">Number of bytes exposed by this provider. <para>此提供程序暴露的字节数。</para></param>
    public FileDataProvider(string filePath, long offset, long length)
    {
        _filePath = filePath;
        _offset = offset;
        _length = length;
    }

    /// <summary>
    /// Gets the total length, in bytes, of the data exposed by this provider.
    /// <para>获取此提供程序暴露的数据总字节长度。</para>
    /// </summary>
    public long Length => _length;

    /// <summary>
    /// Synchronously writes the provider's data range to the specified stream.
    /// <para>同步将提供程序的数据范围写入指定流。</para>
    /// </summary>
    /// <param name="stream">Destination stream to write the data to. <para>要写入数据的目标流。</para></param>
    public void WriteTo(Stream stream)
    {
        using var fs = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);
        var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(1024 * 1024);
        try
        {
            var remaining = _length;
            fs.Seek(_offset, SeekOrigin.Begin);

            while (remaining > 0)
            {
                var toRead = (int)Math.Min(buffer.Length, remaining);
                var read = fs.Read(buffer, 0, toRead);
                if (read == 0)
                {
                    break;
                }

                stream.Write(buffer, 0, read);
                remaining -= read;
            }
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Asynchronously writes the provider's data range to the specified stream.
    /// <para>异步将提供程序的数据范围写入指定流。</para>
    /// </summary>
    /// <param name="stream">Destination stream to write the data to. <para>要写入数据的目标流。</para></param>
    /// <param name="cancellationToken">Cancellation token for the asynchronous operation. <para>异步操作的取消令牌。</para></param>
    /// <returns>A task that completes when the write finishes. <para>写入完成时结束的任务。</para></returns>
    public async Task WriteToAsync(Stream stream, CancellationToken cancellationToken = default)
    {
#if NET6_0_OR_GREATER
        await using var fs = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
#else
        using var fs = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
#endif
        var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(1024 * 1024);
        try
        {
            var remaining = _length;
            fs.Seek(_offset, SeekOrigin.Begin);

            while (remaining > 0)
            {
                var toRead = (int)Math.Min(buffer.Length, remaining);
                var read = await fs.ReadAsync(buffer, 0, toRead, cancellationToken);
                if (read == 0)
                {
                    break;
                }

                await stream.WriteAsync(buffer, 0, read, cancellationToken);
                remaining -= read;
            }
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Reads data from the provider into a byte array buffer.
    /// <para>从提供程序读取数据到字节数组缓冲区。</para>
    /// </summary>
    /// <param name="inOffset">Byte offset relative to the provider's start. <para>相对于提供程序起始的字节偏移。</para></param>
    /// <param name="buffer">Destination buffer to receive bytes. <para>接收字节的目标缓冲区。</para></param>
    /// <param name="bufferOffset">Offset in the destination buffer to start writing. <para>目标缓冲区中的起始写入偏移。</para></param>
    /// <param name="count">Maximum number of bytes to read. <para>最大读取字节数。</para></param>
    /// <returns>The number of bytes actually read. <para>实际读取的字节数。</para></returns>
    public int Read(long inOffset, byte[] buffer, int bufferOffset, int count)
    {
        return Read(inOffset, buffer.AsSpan(bufferOffset, count));
    }

    /// <summary>
    /// Reads data from the provider into a <see cref="Span{Byte}"/>.
    /// <para>从提供程序读取数据到 Span{Byte}。</para>
    /// </summary>
    /// <param name="inOffset">Byte offset relative to the provider's start. <para>相对于提供程序起始的字节偏移。</para></param>
    /// <param name="buffer">Span that receives the data. <para>接收数据的 Span。</para></param>
    /// <returns>The number of bytes actually read. <para>实际读取的字节数。</para></returns>
    public int Read(long inOffset, Span<byte> buffer)
    {
        if (inOffset >= _length)
        {
            return 0;
        }

        var toRead = (int)Math.Min(buffer.Length, _length - inOffset);
#if NET6_0_OR_GREATER
        using Microsoft.Win32.SafeHandles.SafeFileHandle handle = File.OpenHandle(_filePath, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.RandomAccess);
        System.IO.RandomAccess.Read(handle, buffer.Slice(0, toRead), _offset + inOffset);
        return toRead;
#else
        using var fs = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.RandomAccess);
        fs.Seek(_offset + inOffset, SeekOrigin.Begin);
        return fs.Read(buffer.Slice(0, toRead));
#endif
    }

    /// <summary>
    /// Creates a sub-provider that represents a sub-range of this provider.
    /// <para>创建表示此提供程序子范围的子提供程序。</para>
    /// </summary>
    /// <param name="subOffset">Byte offset relative to this provider's start. <para>相对于此提供程序起始的字节偏移。</para></param>
    /// <param name="subLength">Length in bytes of the sub-range. <para>子范围的字节长度。</para></param>
    /// <returns>An <see cref="ISparseDataProvider"/> for the requested sub-range. <para>请求子范围的 ISparseDataProvider。</para></returns>
    public ISparseDataProvider GetSubProvider(long subOffset, long subLength)
    {
        return new FileDataProvider(_filePath, _offset + subOffset, subLength);
    }

    /// <summary>
    /// Releases any resources held by the provider. This provider does not hold persistent resources.
    /// <para>释放提供程序持有的所有资源。此提供程序不持有持久资源。</para>
    /// </summary>
    public void Dispose() { }
}
