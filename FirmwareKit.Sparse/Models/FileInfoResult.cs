namespace FirmwareKit.Sparse.Models;

/// <summary>
/// Result of querying file information.
/// <para>查询文件信息的结果。</para>
/// </summary>
public record FileInfoResult
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
    /// Gets or initializes the file path.
    /// <para>获取或初始化文件路径。</para>
    /// </summary>
    public string? FilePath { get; init; }

    /// <summary>
    /// Gets or initializes the file size in bytes.
    /// <para>获取或初始化文件的字节大小。</para>
    /// </summary>
    public long FileSize { get; init; }

    /// <summary>
    /// Gets or initializes whether the file is a sparse image.
    /// <para>获取或初始化文件是否为稀疏镜像。</para>
    /// </summary>
    public bool IsSparseImage { get; init; }

    /// <summary>
    /// Gets or initializes the sparse file information (null if not a sparse image).
    /// <para>获取或初始化稀疏文件信息（如果不是稀疏镜像则为 null）。</para>
    /// </summary>
    public SparseFileInfo? SparseInfo { get; init; }
}
