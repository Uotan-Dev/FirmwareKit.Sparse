namespace FirmwareKit.Sparse.DataProviders;

/// <summary>
/// Represents a sparse data provider.
/// </summary>
public interface ISparseDataProvider : IDisposable
{
    /// <summary>
    /// Gets the total length of the data.
    /// </summary>
    long Length { get; }

    /// <summary>
    /// Writes the data to the specified stream.
    /// </summary>
    /// <param name="stream">The target stream.</param>
    void WriteTo(Stream stream);

    /// <summary>
    /// Writes the data to the specified stream asynchronously.
    /// </summary>
    /// <param name="stream">The target stream.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous write operation.</returns>
    Task WriteToAsync(Stream stream, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads data from the specified offset into a buffer.
    /// </summary>
    /// <param name="offset">The starting offset to read from.</param>
    /// <param name="buffer">The target buffer.</param>
    /// <param name="bufferOffset">The starting index in the buffer.</param>
    /// <param name="count">The number of bytes to read.</param>
    /// <returns>The number of bytes read.</returns>
    int Read(long offset, byte[] buffer, int bufferOffset, int count);

    /// <summary>
    /// Reads data from the specified offset into a buffer span.
    /// </summary>
    /// <param name="offset">The starting offset to read from.</param>
    /// <param name="buffer">The target buffer span.</param>
    /// <returns>The number of bytes read.</returns>
    int Read(long offset, Span<byte> buffer);

    /// <summary>
    /// Gets a sub-data provider for the specified range.
    /// </summary>
    /// <param name="offset">The starting offset of the sub-data.</param>
    /// <param name="length">The length of the sub-data.</param>
    /// <returns>A new <see cref="ISparseDataProvider"/> instance.</returns>
    ISparseDataProvider GetSubProvider(long offset, long length);
}
