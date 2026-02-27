namespace FirmwareKit.Sparse.Models;

/// <summary>
/// Represents the mode for reading a sparse file.
/// </summary>
public enum SparseReadMode
{
    /// <summary>Normal mode (reads all data).</summary>
    Normal = 0,
    /// <summary>Sparse mode (parses the sparse structure).</summary>
    Sparse = 1,
    /// <summary>Hole mode (treats zeroed blocks as holes).</summary>
    Hole = 2
}
