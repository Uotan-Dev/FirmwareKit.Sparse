namespace FirmwareKit.Sparse.DataProviders;

/// <summary>
/// Provides access to a region of a <see cref="Stream"/> as an <see cref="ISparseDataProvider"/>.
/// Supports synchronous and asynchronous reads and can optionally leave the underlying
/// stream open when disposed.
/// </summary>
public class StreamDataProvider : ISparseDataProvider
{
    /// <summary>The underlying source stream.</summary>
    private readonly Stream stream;
    /// <summary>Byte offset within the source stream where this provider begins (long).</summary>
    private readonly long offset;
    /// <summary>Length in bytes exposed by this provider (long).</summary>
    private readonly long length;
    /// <summary>If true, the underlying stream will not be closed when this provider is disposed.</summary>
    private readonly bool leaveOpen;

    /// <summary>
    /// Initialize a new <see cref="StreamDataProvider"/> for a section of a stream.
    /// </summary>
    /// <param name="stream">The source stream to read from.</param>
    /// <param name="offset">Byte offset within the stream where the provider view starts (long).</param>
    /// <param name="length">Number of bytes exposed by this provider (long).</param>
    /// <param name="leaveOpen">If true, do not close the source stream when disposing (bool).</param>
    public StreamDataProvider(Stream stream, long offset, long length, bool leaveOpen = true)
    {
        this.stream = stream;
        this.offset = offset;
        this.length = length;
        this.leaveOpen = leaveOpen;
    }

    /// <summary>
    /// Gets the total length, in bytes, of the data exposed by this provider.
    /// </summary>
    public long Length => length;

    /// <summary>
    /// Synchronously write the provider's data to the specified output stream.
    /// </summary>
    /// <param name="outStream">Destination stream to receive the bytes.</param>
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

    /// <summary>
    /// Asynchronously write the provider's data to the specified output stream.
    /// </summary>
    /// <param name="outStream">Destination stream to receive the bytes.</param>
    /// <param name="cancellationToken">Cancellation token for the asynchronous operation.</param>
    /// <returns>A task that completes when the write finishes.</returns>
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

    /// <summary>
    /// Read bytes from this provider into a byte array buffer.
    /// </summary>
    /// <param name="inOffset">Byte offset relative to the provider's start to read from (long).</param>
    /// <param name="buffer">Destination buffer to receive bytes.</param>
    /// <param name="bufferOffset">Offset in the destination buffer to begin writing (int).</param>
    /// <param name="count">Maximum number of bytes to read (int).</param>
    /// <returns>The number of bytes actually read.</returns>
    public int Read(long inOffset, byte[] buffer, int bufferOffset, int count)
    {
        return Read(inOffset, buffer.AsSpan(bufferOffset, count));
    }

    /// <summary>
    /// Read bytes from this provider into a <see cref="Span{Byte}"/>.
    /// </summary>
    /// <param name="inOffset">Byte offset relative to the provider's start to read from (long).</param>
    /// <param name="buffer">Span to receive the data.</param>
    /// <returns>The number of bytes actually read.</returns>
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

    /// <summary>
    /// Create a sub-provider that represents a sub-range of this provider.
    /// </summary>
    /// <param name="subOffset">Byte offset relative to this provider's start for the sub-range (long).</param>
    /// <param name="subLength">Length in bytes of the sub-range (long).</param>
    /// <returns>An <see cref="ISparseDataProvider"/> for the requested sub-range.</returns>
    public ISparseDataProvider GetSubProvider(long subOffset, long subLength)
    {
        return new StreamDataProvider(stream, offset + subOffset, subLength, true);
    }

    /// <summary>
    /// Dispose the provider and optionally close the underlying stream.
    /// </summary>
    public void Dispose()
    {
        if (!leaveOpen)
        {
            stream.Dispose();
        }
    }
}
