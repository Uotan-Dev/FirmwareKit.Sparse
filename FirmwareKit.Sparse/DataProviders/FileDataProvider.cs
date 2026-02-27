namespace FirmwareKit.Sparse.DataProviders;

/// <summary>
/// File-based sparse data provider.
/// </summary>
public class FileDataProvider : ISparseDataProvider
{
    private readonly string filePath;
    private readonly long offset;
    private readonly long length;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileDataProvider"/> class.
    /// </summary>
    /// <param name="filePath">The file path.</param>
    /// <param name="offset">The starting offset in the file.</param>
    /// <param name="length">The data length.</param>
    public FileDataProvider(string filePath, long offset, long length)
    {
        this.filePath = filePath;
        this.offset = offset;
        this.length = length;
    }

    /// <inheritdoc/>
    public long Length => length;

    /// <inheritdoc/>
    public void WriteTo(Stream stream)
    {
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);
        var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(1024 * 1024);
        try
        {
            var remaining = length;
            fs.Seek(offset, SeekOrigin.Begin);

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

    /// <inheritdoc/>
    public async Task WriteToAsync(Stream stream, CancellationToken cancellationToken = default)
    {
#if NET6_0_OR_GREATER
        await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
#else
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
#endif
        var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(1024 * 1024);
        try
        {
            var remaining = length;
            fs.Seek(offset, SeekOrigin.Begin);

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

    /// <inheritdoc/>
    public int Read(long inOffset, byte[] buffer, int bufferOffset, int count)
    {
        return Read(inOffset, buffer.AsSpan(bufferOffset, count));
    }

    /// <inheritdoc/>
    public int Read(long inOffset, Span<byte> buffer)
    {
        if (inOffset >= length)
        {
            return 0;
        }

        var toRead = (int)Math.Min(buffer.Length, length - inOffset);
#if NET6_0_OR_GREATER
        using var handle = File.OpenHandle(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.RandomAccess);
        System.IO.RandomAccess.Read(handle, buffer.Slice(0, toRead), offset + inOffset);
        return toRead;
#else
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.RandomAccess);
        fs.Seek(offset + inOffset, SeekOrigin.Begin);
        return fs.Read(buffer.Slice(0, toRead));
#endif
    }

    /// <inheritdoc/>
    public ISparseDataProvider GetSubProvider(long subOffset, long subLength)
    {
        return new FileDataProvider(filePath, offset + subOffset, subLength);
    }

    /// <inheritdoc/>
    public void Dispose() { }
}
