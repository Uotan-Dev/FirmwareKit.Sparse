namespace FirmwareKit.Sparse.Utils;

/// <summary>
/// Provides methods for validating and retrieving information from sparse image files.
/// </summary>
public static class SparseImageValidator
{
    /// <summary>
    /// Validates a sparse image file by checking headers and chunk consistency.
    /// </summary>
    /// <param name="filePath">The absolute path to the sparse image file.</param>
    /// <returns>A <see cref="ValidationResult"/> containing the detailed validation information.</returns>
    public static ValidationResult ValidateSparseImage(string filePath)
    {
        try
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

            uint totalBlocks = 0;

            for (uint i = 0; i < sparseFile.Header.TotalChunks; i++)
            {
                var chunk = sparseFile.Chunks[(int)i];

                var chunkInfo = new ChunkInfo
                {
                    Index = i,
                    ChunkType = chunk.Header.ChunkType,
                    ChunkSize = chunk.Header.ChunkSize,
                    TotalSize = chunk.Header.TotalSize
                };
                ((List<ChunkInfo>)result.Chunks).Add(chunkInfo);

                totalBlocks += chunk.Header.ChunkSize;
            }

            if (totalBlocks > sparseFile.Header.TotalBlocks)
            {
                return result with { Success = false, ErrorMessage = $"Total blocks in chunks ({totalBlocks}) exceeds total blocks in header ({sparseFile.Header.TotalBlocks})" };
            }

            return result with { CalculatedTotalBlocks = totalBlocks };
        }
        catch (Exception ex)
        {
            return new ValidationResult { Success = false, ErrorMessage = ex.Message, FilePath = filePath };
        }
    }

    /// <summary>
    /// Determines whether the specified file is a valid sparse image based on its magic number.
    /// </summary>
    /// <param name="filePath">The path to the file to check.</param>
    /// <returns><c>true</c> if the file is a valid sparse image; otherwise, <c>false</c>.</returns>
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
    /// Retrieves detailed metadata and compression information for a sparse image file.
    /// </summary>
    /// <param name="filePath">The path to the sparse image file.</param>
    /// <returns>A <see cref="SparseImageInfo"/> containing metadata such as uncompressed size and compression ratio.</returns>
    public static SparseImageInfo GetSparseImageInfo(string filePath)
    {
        try
        {
            if (!IsSparseImage(filePath))
            {
                return new SparseImageInfo { Success = false, ErrorMessage = "Not a valid sparse image file", FilePath = filePath };
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
        catch (Exception ex)
        {
            return new SparseImageInfo { Success = false, ErrorMessage = ex.Message, FilePath = filePath };
        }
    }
}
