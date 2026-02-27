namespace FirmwareKit.Sparse.Models;

/// <summary>
/// Detailed sparse image information.
/// </summary>
public record SparseImageInfo
{
    /// <summary>Gets or initializes whether the operation was successful.</summary>
    public bool Success { get; init; }
    /// <summary>Gets or initializes the error message.</summary>
    public string? ErrorMessage { get; init; }
    /// <summary>Gets or initializes the file path.</summary>
    public string? FilePath { get; init; }
    /// <summary>Gets or initializes the file size.</summary>
    public long FileSize { get; init; }
    /// <summary>Gets or initializes the uncompressed size.</summary>
    public long UncompressedSize { get; init; }
    /// <summary>Gets or initializes the compression ratio.</summary>
    public double CompressionRatio { get; init; }
    /// <summary>Gets or initializes the version information.</summary>
    public string Version { get; init; } = "";
    /// <summary>Gets or initializes the block size.</summary>
    public uint BlockSize { get; init; }
    /// <summary>Gets or initializes the total blocks.</summary>
    public uint TotalBlocks { get; init; }
    /// <summary>Gets or initializes the total chunks.</summary>
    public uint TotalChunks { get; init; }
}

/// <summary>
/// Header information for verification.
/// </summary>
public record HeaderInfo
{
    /// <summary>Gets or initializes the magic number.</summary>
    public uint Magic { get; init; }
    /// <summary>Gets or initializes the version string.</summary>
    public string Version { get; init; } = "";
    /// <summary>Gets or initializes the block size.</summary>
    public uint BlockSize { get; init; }
    /// <summary>Gets or initializes the total blocks.</summary>
    public uint TotalBlocks { get; init; }
    /// <summary>Gets or initializes the total chunks.</summary>
    public uint TotalChunks { get; init; }
}

/// <summary>
/// Chunk information for verification.
/// </summary>
public record ChunkInfo
{
    /// <summary>Gets or initializes the index.</summary>
    public uint Index { get; init; }
    /// <summary>Gets or initializes the chunk type.</summary>
    public ushort ChunkType { get; init; }
    /// <summary>Gets or initializes the chunk size in blocks.</summary>
    public uint ChunkSize { get; init; }
    /// <summary>Gets or initializes the total size in bytes.</summary>
    public uint TotalSize { get; init; }
}

/// <summary>
/// Sparse image validation result.
/// </summary>
public record ValidationResult
{
    /// <summary>Gets or initializes whether the validation was successful.</summary>
    public bool Success { get; init; }
    /// <summary>Gets or initializes the error message.</summary>
    public string? ErrorMessage { get; init; }
    /// <summary>Gets or initializes the file path.</summary>
    public string? FilePath { get; init; }
    /// <summary>Gets or initializes the header information.</summary>
    public HeaderInfo? Header { get; init; }
    /// <summary>Gets or initializes the list of chunk information.</summary>
    public IReadOnlyList<ChunkInfo>? Chunks { get; init; }
    /// <summary>Gets or initializes the calculated total blocks.</summary>
    public uint CalculatedTotalBlocks { get; init; }
}

/// <summary>
/// Basic file information for comparison.
/// </summary>
public record FileBasicInfo
{
    /// <summary>Gets or initializes the path.</summary>
    public string? Path { get; init; }
    /// <summary>Gets or initializes the size.</summary>
    public long Size { get; init; }
    /// <summary>Gets or initializes the type.</summary>
    public string? Type { get; init; }
}

/// <summary>
/// File comparison result.
/// </summary>
public record FileComparisonResult
{
    /// <summary>Gets or initializes whether the comparison was successful.</summary>
    public bool Success { get; init; }
    /// <summary>Gets or initializes the error message.</summary>
    public string? ErrorMessage { get; init; }
    /// <summary>Gets or initializes the information for the first file.</summary>
    public FileBasicInfo? File1Info { get; init; }
    /// <summary>Gets or initializes the information for the second file.</summary>
    public FileBasicInfo? File2Info { get; init; }
    /// <summary>Gets or initializes whether the sizes match.</summary>
    public bool SizeMatches { get; init; }
    /// <summary>Gets or initializes whether the types match.</summary>
    public bool TypeMatches { get; init; }
}

/// <summary>
/// File information result.
/// </summary>
public record FileInfoResult
{
    /// <summary>Gets or initializes whether the operation was successful.</summary>
    public bool Success { get; init; }
    /// <summary>Gets or initializes the error message.</summary>
    public string? ErrorMessage { get; init; }
    /// <summary>Gets or initializes the file path.</summary>
    public string? FilePath { get; init; }
    /// <summary>Gets or initializes the file size.</summary>
    public long FileSize { get; init; }
    /// <summary>Gets or initializes whether the file is a sparse image.</summary>
    public bool IsSparseImage { get; init; }
    /// <summary>Gets or initializes the sparse file information.</summary>
    public SparseFileInfo? SparseInfo { get; init; }
}

/// <summary>
/// Detailed sparse file information.
/// </summary>
public record SparseFileInfo
{
    /// <summary>Gets or initializes the version information.</summary>
    public string Version { get; init; } = "";
    /// <summary>Gets or initializes the block size.</summary>
    public uint BlockSize { get; init; }
    /// <summary>Gets or initializes the total blocks.</summary>
    public uint TotalBlocks { get; init; }
    /// <summary>Gets or initializes the total number of chunks.</summary>
    public uint TotalChunks { get; init; }
    /// <summary>Gets or initializes the uncompressed size.</summary>
    public long UncompressedSize { get; init; }
}

/// <summary>
/// Conversion verification result.
/// </summary>
public record ConversionVerificationResult
{
    /// <summary>Gets or initializes whether the verification was successful.</summary>
    public bool Success { get; init; }
    /// <summary>Gets or initializes the error message.</summary>
    public string? ErrorMessage { get; init; }
    /// <summary>Gets or initializes the original size.</summary>
    public long OriginalSize { get; init; }
    /// <summary>Gets or initializes the converted size.</summary>
    public long ConvertedSize { get; init; }
    /// <summary>Gets or initializes whether the sizes match.</summary>
    public bool SizesMatch { get; init; }
}

/// <summary>
/// Result of test image creation.
/// </summary>
public record TestImageCreationResult
{
    /// <summary>Gets or initializes whether the creation was successful.</summary>
    public bool Success { get; init; }
    /// <summary>Gets or initializes the error message.</summary>
    public string? ErrorMessage { get; init; }
    /// <summary>Gets or initializes the output path.</summary>
    public string? OutputPath { get; init; }
    /// <summary>Gets or initializes the size in MB.</summary>
    public uint SizeInMB { get; init; }
    /// <summary>Gets or initializes the block size.</summary>
    public uint BlockSize { get; init; }
    /// <summary>Gets or initializes the total number of chunks.</summary>
    public int TotalChunks { get; init; }
}

/// <summary>
/// Result of data extraction.
/// </summary>
public record DataExtractionResult
{
    /// <summary>Gets or initializes whether the extraction was successful.</summary>
    public bool Success { get; init; }
    /// <summary>Gets or initializes the error message.</summary>
    public string? ErrorMessage { get; init; }
    /// <summary>Gets or initializes the input path.</summary>
    public string? InputPath { get; init; }
    /// <summary>Gets or initializes the output path.</summary>
    public string? OutputPath { get; init; }
    /// <summary>Gets or initializes the partition offset.</summary>
    public long PartitionOffset { get; init; }
    /// <summary>Gets or initializes the block size.</summary>
    public uint BlockSize { get; init; }
    /// <summary>Gets or initializes the starting block.</summary>
    public uint StartBlock { get; init; }
    /// <summary>Gets or initializes the offset within the block.</summary>
    public long OffsetInBlock { get; init; }
    /// <summary>Gets or initializes the total bytes extracted.</summary>
    public long TotalBytesExtracted { get; init; }
    /// <summary>Gets or initializes whether data was found.</summary>
    public bool DataFound { get; init; }
}

/// <summary>
/// Result of data extraction with CSV output.
/// </summary>
public record DataExtractionWithCsvResult
{
    /// <summary>Gets or initializes whether the extraction was successful.</summary>
    public bool Success { get; init; }
    /// <summary>Gets or initializes the error message.</summary>
    public string? ErrorMessage { get; init; }
    /// <summary>Gets or initializes the input path.</summary>
    public string? InputPath { get; init; }
    /// <summary>Gets or initializes the binary output path.</summary>
    public string? BinOutputPath { get; init; }
    /// <summary>Gets or initializes the CSV output path.</summary>
    public string? CsvOutputPath { get; init; }
    /// <summary>Gets or initializes the partition offset.</summary>
    public long PartitionOffset { get; init; }
    /// <summary>Gets or initializes the block size.</summary>
    public uint BlockSize { get; init; }
    /// <summary>Gets or initializes the starting block number.</summary>
    public long StartBlockNumber { get; init; }
    /// <summary>Gets or initializes the block offset.</summary>
    public long BlockOffset { get; init; }
    /// <summary>Gets or initializes the total bytes extracted.</summary>
    public long TotalBytesExtracted { get; init; }
    /// <summary>Gets or initializes the CSV record count.</summary>
    public int CsvRecordCount { get; init; }
    /// <summary>Gets or initializes whether data was found.</summary>
    public bool DataFound { get; init; }
}
