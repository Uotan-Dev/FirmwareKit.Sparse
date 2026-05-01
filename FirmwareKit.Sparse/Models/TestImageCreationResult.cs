namespace FirmwareKit.Sparse.Models;

/// <summary>
/// Result of creating a test sparse image.
/// <para>创建测试稀疏镜像的结果。</para>
/// </summary>
public record TestImageCreationResult
{
    /// <summary>
    /// Gets or initializes whether the creation was successful.
    /// <para>获取或初始化创建是否成功。</para>
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Gets or initializes the error message when creation fails.
    /// <para>获取或初始化创建失败时的错误消息。</para>
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Gets or initializes the output file path.
    /// <para>获取或初始化输出文件路径。</para>
    /// </summary>
    public string? OutputPath { get; init; }

    /// <summary>
    /// Gets or initializes the image size in megabytes.
    /// <para>获取或初始化镜像大小的兆字节数。</para>
    /// </summary>
    public uint SizeInMB { get; init; }

    /// <summary>
    /// Gets or initializes the block size in bytes.
    /// <para>获取或初始化块的字节大小。</para>
    /// </summary>
    public uint BlockSize { get; init; }

    /// <summary>
    /// Gets or initializes the total number of chunks in the created image.
    /// <para>获取或初始化创建镜像中的总数据块数。</para>
    /// </summary>
    public int TotalChunks { get; init; }
}
