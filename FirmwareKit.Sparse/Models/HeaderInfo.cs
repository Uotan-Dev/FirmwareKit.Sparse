namespace FirmwareKit.Sparse.Models;

/// <summary>
/// Header information extracted during validation.
/// <para>验证期间提取的头部信息。</para>
/// </summary>
public record HeaderInfo
{
    /// <summary>
    /// Gets or initializes the magic number from the header.
    /// <para>获取或初始化头部中的魔术数字。</para>
    /// </summary>
    public uint Magic { get; init; }

    /// <summary>
    /// Gets or initializes the version string (Major.Minor).
    /// <para>获取或初始化版本字符串（主版本.次版本）。</para>
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
}
