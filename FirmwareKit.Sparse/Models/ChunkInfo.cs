namespace FirmwareKit.Sparse.Models;

/// <summary>
/// Chunk information extracted during validation.
/// <para>验证期间提取的数据块信息。</para>
/// </summary>
public record ChunkInfo
{
    /// <summary>
    /// Gets or initializes the zero-based index of the chunk.
    /// <para>获取或初始化数据块的从零开始的索引。</para>
    /// </summary>
    public uint Index { get; init; }

    /// <summary>
    /// Gets or initializes the chunk type code.
    /// <para>获取或初始化数据块类型代码。</para>
    /// </summary>
    public ushort ChunkType { get; init; }

    /// <summary>
    /// Gets or initializes the chunk size in blocks.
    /// <para>获取或初始化数据块的块数量。</para>
    /// </summary>
    public uint ChunkSize { get; init; }

    /// <summary>
    /// Gets or initializes the total size in bytes (including header).
    /// <para>获取或初始化总字节大小（包括头部）。</para>
    /// </summary>
    public uint TotalSize { get; init; }
}
