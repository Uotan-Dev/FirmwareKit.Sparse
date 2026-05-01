namespace FirmwareKit.Sparse.Models;

/// <summary>
/// Basic file information used for comparison.
/// <para>用于比较的基本文件信息。</para>
/// </summary>
public record FileBasicInfo
{
    /// <summary>
    /// Gets or initializes the file path.
    /// <para>获取或初始化文件路径。</para>
    /// </summary>
    public string? Path { get; init; }

    /// <summary>
    /// Gets or initializes the file size in bytes.
    /// <para>获取或初始化文件的字节大小。</para>
    /// </summary>
    public long Size { get; init; }

    /// <summary>
    /// Gets or initializes the file type description.
    /// <para>获取或初始化文件类型描述。</para>
    /// </summary>
    public string? Type { get; init; }
}
