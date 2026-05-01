namespace FirmwareKit.Sparse.Models;

/// <summary>
/// Result of verifying a format conversion.
/// <para>格式转换验证结果。</para>
/// </summary>
public record ConversionVerificationResult
{
    /// <summary>
    /// Gets or initializes whether the verification was successful.
    /// <para>获取或初始化验证是否成功。</para>
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Gets or initializes the error message when verification fails.
    /// <para>获取或初始化验证失败时的错误消息。</para>
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Gets or initializes the original file size in bytes.
    /// <para>获取或初始化原始文件的字节大小。</para>
    /// </summary>
    public long OriginalSize { get; init; }

    /// <summary>
    /// Gets or initializes the converted file size in bytes.
    /// <para>获取或初始化转换后文件的字节大小。</para>
    /// </summary>
    public long ConvertedSize { get; init; }

    /// <summary>
    /// Gets or initializes whether the original and converted sizes match.
    /// <para>获取或初始化原始大小与转换后大小是否匹配。</para>
    /// </summary>
    public bool SizesMatch { get; init; }
}
