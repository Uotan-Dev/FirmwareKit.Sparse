namespace FirmwareKit.Sparse.DataProviders;

/// <summary>
/// Stream-based sparse data provider.
/// </summary>
public class StreamDataProvider : ISparseDataProvider
{
    private readonly Stream stream;
    private readonly long offset;
    private readonly long length;
    private readonly bool leaveOpen;

    /// <summary>
    /// Initializes a new instance of the <see cref="StreamDataProvider"/> class.
    /// </summary>
    /// <param name="stream">The data stream.</param>
    /// <param name="offset">The starting offset in the stream.</param>
    /// <param name="length">The data length.</param>
    /// <param name="leaveOpen">Whether to leave the stream open when the provider is disposed.</param>
    public StreamDataProvider(Stream stream, long offset, long length, bool leaveOpen = true)
    {
        this.stream = stream;
        this.offset = offset;
        this.length = length;
        this.leaveOpen = leaveOpen;
    }

    /// <inheritdoc/>
    public long Length => length;

    /// <inheritdoc/>
    public void WriteTo(Stream outStream)
    {
        if (stream.CanSeek)
        {
            stream.Seek(offset, SeekOrigin.Begin);
        }
        var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(1024 * 1024);
        try
        {
            var remaining = length;
            while (remaining > 0)
            {
                var toRead = (int)Math.Min(buffer.Length, remaining);
                var read = stream.Read(buffer, 0, toRead);
                if (read == 0)
                {
                    break;
                }

                outStream.Write(buffer, 0, read);
                remaining -= read;
            }
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <inheritdoc/>
    public async Task WriteToAsync(Stream outStream, CancellationToken cancellationToken = default)
    {
        if (stream.CanSeek)
        {
            stream.Seek(offset, SeekOrigin.Begin);
        }
        var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(1024 * 1024);
        try
        {
            var remaining = length;
            while (remaining > 0)
            {
                var toRead = (int)Math.Min(buffer.Length, remaining);
                var read = await stream.ReadAsync(buffer, 0, toRead, cancellationToken);
                if (read == 0)
                {
                    break;
                }

                await outStream.WriteAsync(buffer, 0, read, cancellationToken);
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

        var toRead = (int)Math.Min(buffer.Length, (int)(length - inOffset));
        if (stream.CanSeek)
        {
            stream.Seek(offset + inOffset, SeekOrigin.Begin);
        }
        return stream.Read(buffer.Slice(0, toRead));
    }

    /// <inheritdoc/>
    public ISparseDataProvider GetSubProvider(long subOffset, long subLength)
    {
        return new StreamDataProvider(stream, offset + subOffset, subLength, true);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!leaveOpen)
        {
            stream.Dispose();
        }
    }
}
