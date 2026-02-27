namespace FirmwareKit.Sparse.DataProviders;

/// <summary>
/// Memory-based sparse data provider.
/// </summary>
public class MemoryDataProvider : ISparseDataProvider
{
    private readonly byte[] data;
    private readonly int _offset;
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

    /// <inheritdoc/>
    public long Length => _length;

    /// <inheritdoc/>
    public void WriteTo(Stream stream)
    {
        stream.Write(data, _offset, _length);
    }

    /// <inheritdoc/>
    public Task WriteToAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        return stream.WriteAsync(data, _offset, _length, cancellationToken);
    }

    /// <inheritdoc/>
    public int Read(long offset, byte[] buffer, int bufferOffset, int count)
    {
        return Read(offset, buffer.AsSpan(bufferOffset, count));
    }

    /// <inheritdoc/>
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

    /// <inheritdoc/>
    public ISparseDataProvider GetSubProvider(long offset, long length)
    {
        return new MemoryDataProvider(data, _offset + (int)offset, (int)length);
    }

    /// <inheritdoc/>
    public void Dispose() { }
}
