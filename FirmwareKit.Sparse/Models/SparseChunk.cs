namespace FirmwareKit.Sparse.Models;

/// <summary>
/// Represents a chunk in a sparse file.
/// </summary>
public class SparseChunk : IDisposable
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SparseChunk"/> class with the specified chunk header.
    /// </summary>
    /// <param name="header">The chunk header.</param>
    public SparseChunk(ChunkHeader header)
    {
        Header = header;
    }

    /// <summary>
    /// Gets or sets the starting block index for this chunk (uint).
    /// </summary>
    public uint StartBlock { get; set; } = 0;

    /// <summary>
    /// Gets the <see cref="ChunkHeader"/> that describes this chunk.
    /// </summary>
    public ChunkHeader Header { get; init; }

    /// <summary>
    /// Gets or sets the <see cref="ISparseDataProvider"/> that supplies the chunk's data (may be null).
    /// </summary>
    public ISparseDataProvider? DataProvider { get; set; }

    /// <summary>
    /// Gets or sets the 4-byte fill pattern value used only for Fill chunks (uint).
    /// </summary>
    public uint FillValue { get; set; }

    /// <summary>
    /// Releases all resources used by the <see cref="SparseChunk"/>.
    /// </summary>
    public void Dispose()
    {
        DataProvider?.Dispose();
    }
}
