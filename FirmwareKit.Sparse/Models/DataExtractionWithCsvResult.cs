namespace FirmwareKit.Sparse.Models;

/// <summary>
/// Result of extracting data from a sparse image with CSV output.
/// <para>从稀疏镜像中提取数据并输出 CSV 的结果。</para>
/// </summary>
public record DataExtractionWithCsvResult
{
    /// <summary>
    /// Gets or initializes whether the extraction was successful.
    /// <para>获取或初始化提取是否成功。</para>
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Gets or initializes the error message when extraction fails.
    /// <para>获取或初始化提取失败时的错误消息。</para>
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Gets or initializes the input sparse image path.
    /// <para>获取或初始化输入稀疏镜像路径。</para>
    /// </summary>
    public string? InputPath { get; init; }

    /// <summary>
    /// Gets or initializes the binary output file path.
    /// <para>获取或初始化二进制输出文件路径。</para>
    /// </summary>
    public string? BinOutputPath { get; init; }

    /// <summary>
    /// Gets or initializes the CSV output file path.
    /// <para>获取或初始化 CSV 输出文件路径。</para>
    /// </summary>
    public string? CsvOutputPath { get; init; }

    /// <summary>
    /// Gets or initializes the partition offset in bytes.
    /// <para>获取或初始化分区偏移（字节）。</para>
    /// </summary>
    public long PartitionOffset { get; init; }

    /// <summary>
    /// Gets or initializes the block size in bytes.
    /// <para>获取或初始化块的字节大小。</para>
    /// </summary>
    public uint BlockSize { get; init; }

    /// <summary>
    /// Gets or initializes the starting block number.
    /// <para>获取或初始化起始块号。</para>
    /// </summary>
    public long StartBlockNumber { get; init; }

    /// <summary>
    /// Gets or initializes the block offset.
    /// <para>获取或初始化块偏移。</para>
    /// </summary>
    public long BlockOffset { get; init; }

    /// <summary>
    /// Gets or initializes the total number of bytes extracted.
    /// <para>获取或初始化提取的总字节数。</para>
    /// </summary>
    public long TotalBytesExtracted { get; init; }

    /// <summary>
    /// Gets or initializes the number of records written to the CSV file.
    /// <para>获取或初始化写入 CSV 文件的记录数。</para>
    /// </summary>
    public int CsvRecordCount { get; init; }

    /// <summary>
    /// Gets or initializes whether the target data was found.
    /// <para>获取或初始化是否找到目标数据。</para>
    /// </summary>
    public bool DataFound { get; init; }
}
