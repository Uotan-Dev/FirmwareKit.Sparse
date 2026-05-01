namespace FirmwareKit.Sparse.Models;

/// <summary>
/// Detailed sparse image information including validation and format details.
/// <para>详细的稀疏镜像信息，包括验证和格式详情。</para>
/// </summary>
public record SparseImageInfo
{
    /// <summary>
    /// Gets or initializes whether the operation was successful.
    /// <para>获取或初始化操作是否成功。</para>
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Gets or initializes the error message when the operation fails.
    /// <para>获取或初始化操作失败时的错误消息。</para>
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Gets or initializes the file path of the sparse image.
    /// <para>获取或初始化稀疏镜像的文件路径。</para>
    /// </summary>
    public string? FilePath { get; init; }

    /// <summary>
    /// Gets or initializes the on-disk file size in bytes.
    /// <para>获取或初始化磁盘上的文件字节大小。</para>
    /// </summary>
    public long FileSize { get; init; }

    /// <summary>
    /// Gets or initializes the uncompressed (original) size in bytes.
    /// <para>获取或初始化未压缩（原始）的字节大小。</para>
    /// </summary>
    public long UncompressedSize { get; init; }

    /// <summary>
    /// Gets or initializes the compression ratio (FileSize / UncompressedSize).
    /// <para>获取或初始化压缩比率（FileSize / UncompressedSize）。</para>
    /// </summary>
    public double CompressionRatio { get; init; }

    /// <summary>
    /// Gets or initializes the sparse format version string.
    /// <para>获取或初始化稀疏格式版本字符串。</para>
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
