using Microsoft.Win32.SafeHandles;

namespace FirmwareKit.Sparse.DataProviders;

/// <summary>
/// Provides access to a region of a file as an <see cref="ISparseDataProvider"/>.
/// This implementation reads from a file on disk starting at a given offset and
/// exposes a fixed-length view over that data.
/// </summary>
public class FileDataProvider : ISparseDataProvider
{
    /// <summary>Path to the source file (string).</summary>
    private readonly string filePath;
    /// <summary>Byte offset within the file where the provider view begins (long).</summary>
    private readonly long offset;
    /// <summary>Length in bytes of the provider view (long).</summary>
    private readonly long length;

    /// <summary>
    /// Initialize a new <see cref="FileDataProvider"/> for the given file segment.
    /// </summary>
    /// <param name="filePath">Path to the source file on disk (string).</param>
    /// <param name="offset">Byte offset within the file where the segment starts (long).</param>
    /// <param name="length">Number of bytes exposed by this provider (long).</param>
    public FileDataProvider(string filePath, long offset, long length)
    {
        this.filePath = filePath;
        this.offset = offset;
        this.length = length;
    }

    /// <summary>
    /// Gets the total length, in bytes, of the data exposed by this provider.
    /// </summary>
    public long Length => length;

    /// <summary>
    /// Synchronously write the provider's data range to the specified stream.
    /// </summary>
    /// <param name="stream">Destination stream to write the data to.</param>
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

    /// <summary>
    /// Asynchronously write the provider's data range to the specified stream.
    /// </summary>
    /// <param name="stream">Destination stream to write the data to.</param>
    /// <param name="cancellationToken">Cancellation token for the asynchronous operation.</param>
    /// <returns>A task that completes when the write finishes.</returns>
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

    /// <summary>
    /// Read data from the provider into a byte array.
    /// </summary>
    /// <param name="inOffset">Byte offset relative to the provider's start to read from (long).</param>
    /// <param name="buffer">Destination buffer to receive bytes.</param>
    /// <param name="bufferOffset">Offset in the destination buffer to start writing (int).</param>
    /// <param name="count">Maximum number of bytes to read (int).</param>
    /// <returns>The number of bytes actually read.</returns>
    public int Read(long inOffset, byte[] buffer, int bufferOffset, int count)
    {
        return Read(inOffset, buffer.AsSpan(bufferOffset, count));
    }

    /// <summary>
    /// Read data from the provider into a <see cref="Span{Byte}"/>.
    /// </summary>
    /// <param name="inOffset">Byte offset relative to the provider's start to read from (long).</param>
    /// <param name="buffer">Span that receives the data.</param>
    /// <returns>The number of bytes actually read.</returns>
    public int Read(long inOffset, Span<byte> buffer)
    {
        if (inOffset >= length)
        {
            return 0;
        }

        var toRead = (int)Math.Min(buffer.Length, length - inOffset);
#if NET6_0_OR_GREATER
        using SafeFileHandle handle = File.OpenHandle(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.RandomAccess);
        System.IO.RandomAccess.Read(handle, buffer.Slice(0, toRead), offset + inOffset);
        return toRead;
#else
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.RandomAccess);
        fs.Seek(offset + inOffset, SeekOrigin.Begin);
        return fs.Read(buffer.Slice(0, toRead));
#endif
    }

    /// <summary>
    /// Create a sub-provider that represents a sub-range of this provider.
    /// </summary>
    /// <param name="subOffset">Byte offset relative to this provider's start for the sub-range (long).</param>
    /// <param name="subLength">Length in bytes of the sub-range (long).</param>
    /// <returns>An <see cref="ISparseDataProvider"/> for the requested sub-range.</returns>
    public ISparseDataProvider GetSubProvider(long subOffset, long subLength)
    {
        return new FileDataProvider(filePath, offset + subOffset, subLength);
    }

    /// <summary>
    /// Release any resources held by the provider. This provider does not hold persistent
    /// resources and Dispose is a no-op.
    /// </summary>
    public void Dispose() { }
}
