namespace FirmwareKit.Sparse.Models;

/// <summary>
/// Detailed Sparse image information
/// </summary>
public record SparseImageInfo
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public string? FilePath { get; init; }
    public long FileSize { get; init; }
    public long UncompressedSize { get; init; }
    public double CompressionRatio { get; init; }
    public string Version { get; init; } = "";
    public uint BlockSize { get; init; }
    public uint TotalBlocks { get; init; }
    public uint TotalChunks { get; init; }
}

/// <summary>
/// Header information for validation
/// </summary>
public record HeaderInfo
{
    public uint Magic { get; init; }
    public string Version { get; init; } = "";
    public uint BlockSize { get; init; }
    public uint TotalBlocks { get; init; }
    public uint TotalChunks { get; init; }
}

/// <summary>
/// Chunk information for validation
/// </summary>
public record ChunkInfo
{
    public uint Index { get; init; }
    public ushort ChunkType { get; init; }
    public uint ChunkSize { get; init; }
    public uint TotalSize { get; init; }
}

/// <summary>
/// Sparse image validation result
/// </summary>
public record ValidationResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public string? FilePath { get; init; }
    public HeaderInfo? Header { get; init; }
    public List<ChunkInfo>? Chunks { get; init; }
    public uint CalculatedTotalBlocks { get; init; }
}

/// <summary>
/// File basic information for comparison
/// </summary>
public record FileBasicInfo
{
    public string? Path { get; init; }
    public long Size { get; init; }
    public string? Type { get; init; }
}

/// <summary>
/// File comparison result
/// </summary>
public record FileComparisonResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public FileBasicInfo? File1Info { get; init; }
    public FileBasicInfo? File2Info { get; init; }
    public bool SizeMatches { get; init; }
    public bool TypeMatches { get; init; }
}

/// <summary>
/// File information result
/// </summary>
public record FileInfoResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public string? FilePath { get; init; }
    public long FileSize { get; init; }
    public bool IsSparseImage { get; init; }
    public SparseFileInfo? SparseInfo { get; init; }
}

/// <summary>
/// Detailed Sparse file information
/// </summary>
public record SparseFileInfo
{
    public string Version { get; init; } = "";
    public uint BlockSize { get; init; }
    public uint TotalBlocks { get; init; }
    public uint TotalChunks { get; init; }
    public long UncompressedSize { get; init; }
}

/// <summary>
/// Conversion verification result
/// </summary>
public record ConversionVerificationResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public long OriginalSize { get; init; }
    public long ConvertedSize { get; init; }
    public bool SizesMatch { get; init; }
}

/// <summary>
/// Test image creation result
/// </summary>
public record TestImageCreationResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public string? OutputPath { get; init; }
    public uint SizeInMB { get; init; }
    public uint BlockSize { get; init; }
    public int TotalChunks { get; init; }
}

/// <summary>
/// Result of data extraction.
/// </summary>
public record DataExtractionResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public string? InputPath { get; init; }
    public string? OutputPath { get; init; }
    public long PartitionOffset { get; init; }
    public uint BlockSize { get; init; }
    public uint StartBlock { get; init; }
    public long OffsetInBlock { get; init; }
    public long TotalBytesExtracted { get; init; }
    public bool DataFound { get; init; }
}

/// <summary>
/// Result of data extraction with CSV.
/// </summary>
public record DataExtractionWithCsvResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public string? InputPath { get; init; }
    public string? BinOutputPath { get; init; }
    public string? CsvOutputPath { get; init; }
    public long PartitionOffset { get; init; }
    public uint BlockSize { get; init; }
    public long StartBlockNumber { get; init; }
    public long BlockOffset { get; init; }
    public long TotalBytesExtracted { get; init; }
    public int CsvRecordCount { get; init; }
    public bool DataFound { get; init; }
}
