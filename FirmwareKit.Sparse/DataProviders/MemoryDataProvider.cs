namespace FirmwareKit.Sparse.DataProviders;

/// <summary>
/// Provides an in-memory view over a byte array as an <see cref="ISparseDataProvider"/>.
/// Useful for small chunks of data already loaded into memory.
/// </summary>
public class MemoryDataProvider : ISparseDataProvider
{
    /// <summary>Underlying byte array containing the data.</summary>
    private readonly byte[] data;
    /// <summary>Start offset into <see cref="data"/> for this provider (int).</summary>
    private readonly int _offset;
    /// <summary>Length in bytes exposed by this provider (int).</summary>
    private readonly int _length;

    /// <summary>
    /// Initializes a new instance of the <see cref="MemoryDataProvider"/> class.
    /// </summary>
    /// <param name="data">The data byte array.</param>
    /// <param name="offset">The starting offset in the array.</param>
    /// <param name="length">The data length. If -1, all data from the offset to the end of the array is used.</param>
    public MemoryDataProvider(byte[] data, int offset = 0, int length = -1)
    {
        this.data = data;
        _offset = offset;
        _length = length < 0 ? data.Length - offset : length;
    }

    /// <summary>
    /// Gets the number of bytes available from this provider.
    /// </summary>
    public long Length => _length;

    /// <summary>
    /// Synchronously write the provider's data to the destination stream.
    /// </summary>
    /// <param name="stream">Target stream to receive the bytes.</param>
    public void WriteTo(Stream stream)
    {
        stream.Write(data, _offset, _length);
    }

    /// <summary>
    /// Asynchronously write the provider's data to the destination stream.
    /// </summary>
    /// <param name="stream">Target stream to receive the bytes.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A task that completes when writing finishes.</returns>
    public Task WriteToAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        return stream.WriteAsync(data, _offset, _length, cancellationToken);
    }

    /// <summary>
    /// Read bytes from this provider into a byte array buffer.
    /// </summary>
    /// <param name="offset">Byte offset relative to this provider's start to read from (long).</param>
    /// <param name="buffer">Destination buffer to receive bytes.</param>
    /// <param name="bufferOffset">Offset in the destination buffer to begin writing (int).</param>
    /// <param name="count">Maximum number of bytes to read (int).</param>
    /// <returns>The number of bytes actually read.</returns>
    public int Read(long offset, byte[] buffer, int bufferOffset, int count)
    {
        return Read(offset, buffer.AsSpan(bufferOffset, count));
    }

    /// <summary>
    /// Read bytes from this provider into a <see cref="Span{Byte}"/>.
    /// </summary>
    /// <param name="offset">Byte offset relative to this provider's start to read from (long).</param>
    /// <param name="buffer">Span that will receive the data.</param>
    /// <returns>The number of bytes actually read.</returns>
    public int Read(long offset, Span<byte> buffer)
    {
        var available = (int)Math.Max(0, _length - offset);
        var toCopy = Math.Min(buffer.Length, available);
        if (toCopy <= 0)
        {
            return 0;
        }

        data.AsSpan(_offset + (int)offset, toCopy).CopyTo(buffer);
        return toCopy;
    }

    /// <summary>
    /// Create a sub-provider representing a slice of the current in-memory data.
    /// </summary>
    /// <param name="offset">Byte offset relative to this provider's start for the sub-range (long).</param>
    /// <param name="length">Length in bytes of the sub-range (long).</param>
    /// <returns>An <see cref="ISparseDataProvider"/> representing the requested sub-range.</returns>
    public ISparseDataProvider GetSubProvider(long offset, long length)
    {
        return new MemoryDataProvider(data, _offset + (int)offset, (int)length);
    }

    /// <summary>
    /// Release any resources held by the provider. No resources are held, so this is a no-op.
    /// </summary>
    public void Dispose() { }
}
