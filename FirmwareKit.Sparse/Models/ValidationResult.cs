namespace FirmwareKit.Sparse.Models;

/// <summary>
/// Result of sparse image validation.
/// <para>稀疏镜像验证结果。</para>
/// </summary>
public record ValidationResult
{
    /// <summary>
    /// Gets or initializes whether the validation was successful.
    /// <para>获取或初始化验证是否成功。</para>
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Gets or initializes the error message when validation fails.
    /// <para>获取或初始化验证失败时的错误消息。</para>
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Gets or initializes the file path of the validated image.
    /// <para>获取或初始化已验证镜像的文件路径。</para>
    /// </summary>
    public string? FilePath { get; init; }

    /// <summary>
    /// Gets or initializes the parsed header information.
    /// <para>获取或初始化解析后的头部信息。</para>
    /// </summary>
    public HeaderInfo? Header { get; init; }

    /// <summary>
    /// Gets or initializes the list of chunk information records.
    /// <para>获取或初始化数据块信息记录列表。</para>
    /// </summary>
    public IReadOnlyList<ChunkInfo>? Chunks { get; init; }

    /// <summary>
    /// Gets or initializes the calculated total blocks from all chunks.
    /// <para>获取或初始化从所有数据块计算得出的总块数。</para>
    /// </summary>
    public uint CalculatedTotalBlocks { get; init; }
}
