namespace FirmwareKit.Sparse.Models;

/// <summary>
/// Result of comparing two sparse image files.
/// <para>比较两个稀疏镜像文件的结果。</para>
/// </summary>
public record FileComparisonResult
{
    /// <summary>
    /// Gets or initializes whether the comparison was successful.
    /// <para>获取或初始化比较是否成功。</para>
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Gets or initializes the error message when comparison fails.
    /// <para>获取或初始化比较失败时的错误消息。</para>
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Gets or initializes the information for the first file.
    /// <para>获取或初始化第一个文件的信息。</para>
    /// </summary>
    public FileBasicInfo? File1Info { get; init; }

    /// <summary>
    /// Gets or initializes the information for the second file.
    /// <para>获取或初始化第二个文件的信息。</para>
    /// </summary>
    public FileBasicInfo? File2Info { get; init; }

    /// <summary>
    /// Gets or initializes whether the file sizes match.
    /// <para>获取或初始化文件大小是否匹配。</para>
    /// </summary>
    public bool SizeMatches { get; init; }

    /// <summary>
    /// Gets or initializes whether the file types match.
    /// <para>获取或初始化文件类型是否匹配。</para>
    /// </summary>
    public bool TypeMatches { get; init; }
}
