namespace FirmwareKit.Sparse.Utils;

/// <summary>
/// Sparse image validator
/// </summary>
public static class SparseImageValidator
{
    /// <summary>
    /// Validates a sparse image file
    /// </summary>
    public static ValidationResult ValidateSparseImage(string filePath)
    {
        using var sparseFile = SparseFile.FromImageFile(filePath);

        var result = new ValidationResult
        {
            Success = true,
            FilePath = filePath,
            Header = new HeaderInfo
            {
                Magic = sparseFile.Header.Magic,
                Version = $"{sparseFile.Header.MajorVersion}.{sparseFile.Header.MinorVersion}",
                BlockSize = sparseFile.Header.BlockSize,
                TotalBlocks = sparseFile.Header.TotalBlocks,
                TotalChunks = sparseFile.Header.TotalChunks
            },
            Chunks = new List<ChunkInfo>()
        };
        if (!sparseFile.Header.IsValid())
        {
            throw new InvalidDataException("Invalid sparse file header");
        }
        uint totalBlocks = 0;

        for (uint i = 0; i < sparseFile.Header.TotalChunks; i++)
        {
            var chunk = sparseFile.Chunks[(int)i];

            if (!chunk.Header.IsValid())
            {
                throw new InvalidDataException($"Invalid chunk header at index {i}");
            }

            var chunkInfo = new ChunkInfo
            {
                Index = i,
                ChunkType = chunk.Header.ChunkType,
                ChunkSize = chunk.Header.ChunkSize,
                TotalSize = chunk.Header.TotalSize
            };
            result.Chunks.Add(chunkInfo);

            totalBlocks += chunk.Header.ChunkSize;
        }

        if (totalBlocks > sparseFile.Header.TotalBlocks)
        {
            throw new InvalidDataException($"Total blocks in chunks ({totalBlocks}) exceeds total blocks in header ({sparseFile.Header.TotalBlocks})");
        }

        return result with { CalculatedTotalBlocks = totalBlocks };
    }

    /// <summary>
    /// Checks if the file is a sparse image
    /// </summary>
    public static bool IsSparseImage(string filePath)
    {
        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            Span<byte> magicBytes = stackalloc byte[4];
            if (stream.Read(magicBytes) != 4)
            {
                return false;
            }

            var magic = BinaryPrimitives.ReadUInt32LittleEndian(magicBytes);
            return magic == SparseFormat.SparseHeaderMagic;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Gets detailed information about a sparse image file
    /// </summary>
    public static SparseImageInfo GetSparseImageInfo(string filePath)
    {
        if (!IsSparseImage(filePath))
        {
            throw new InvalidDataException("Not a valid sparse image file");
        }

        var header = SparseFile.PeekHeader(filePath);
        var fileInfo = new FileInfo(filePath);
        var uncompressedSize = (long)header.TotalBlocks * header.BlockSize;
        var compressionRatio = 100.0 - ((double)fileInfo.Length / uncompressedSize * 100.0);

        return new SparseImageInfo
        {
            Success = true,
            FilePath = filePath,
            FileSize = fileInfo.Length,
            UncompressedSize = uncompressedSize,
            CompressionRatio = compressionRatio,
            Version = $"{header.MajorVersion}.{header.MinorVersion}",
            BlockSize = header.BlockSize,
            TotalBlocks = header.TotalBlocks,
            TotalChunks = header.TotalChunks
        };
    }
}
