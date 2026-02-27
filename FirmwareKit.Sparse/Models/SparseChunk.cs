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

    /// <summary>Gets or sets the starting block index.</summary>
    public uint StartBlock { get; set; } = 0;
    /// <summary>Gets the chunk header.</summary>
    public ChunkHeader Header { get; init; }
    /// <summary>Gets or sets the data provider.</summary>
    public ISparseDataProvider? DataProvider { get; set; }
    /// <summary>Gets or sets the fill value (used only for Fill chunks).</summary>
    public uint FillValue { get; set; }

    /// <summary>
    /// Releases all resources used by the <see cref="SparseChunk"/>.
    /// </summary>
    public void Dispose()
    {
        DataProvider?.Dispose();
    }
}
