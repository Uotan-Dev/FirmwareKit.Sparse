namespace FirmwareKit.Sparse.Models;

/// <summary>
/// Detailed sparse file format information.
/// <para>详细的稀疏文件格式信息。</para>
/// </summary>
public record SparseFileInfo
{
    /// <summary>
    /// Gets or initializes the version string.
    /// <para>获取或初始化版本字符串。</para>
    /// </summary>
    public string Version { get; init; } = "";

    /// <summary>
    /// Gets or initializes the block size in bytes.
    /// <para>获取或初始化块的字节大小。</para>
    /// </summary>
    public uint BlockSize { get; init; }

    /// <summary>
    /// Gets or initializes the total number of blocks.
    /// <para>获取或初始化总块数。</para>
    /// </summary>
    public uint TotalBlocks { get; init; }

    /// <summary>
    /// Gets or initializes the total number of chunks.
    /// <para>获取或初始化总数据块数。</para>
    /// </summary>
    public uint TotalChunks { get; init; }

    /// <summary>
    /// Gets or initializes the uncompressed size in bytes.
    /// <para>获取或初始化未压缩的字节大小。</para>
    /// </summary>
    public long UncompressedSize { get; init; }
}
