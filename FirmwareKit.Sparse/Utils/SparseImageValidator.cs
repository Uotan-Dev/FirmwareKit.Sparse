namespace FirmwareKit.Sparse.Utils;

/// <summary>
/// Provides methods for validating and retrieving information from sparse image files.
/// <para>提供验证和获取稀疏镜像文件信息的方法。</para>
/// </summary>
public static class SparseImageValidator
{
    /// <summary>
    /// Validates a sparse image file by checking headers and chunk consistency.
    /// <para>通过检查头部和数据块一致性来验证稀疏镜像文件。</para>
    /// </summary>
    /// <param name="filePath">The absolute path to the sparse image file. <para>稀疏镜像文件的绝对路径。</para></param>
    /// <returns>A <see cref="ValidationResult"/> containing the detailed validation information. <para>包含详细验证信息的 ValidationResult。</para></returns>
    public static ValidationResult ValidateSparseImage(string filePath)
    {
        try
        {
            using var sparseFile = SparseFile.FromImageFile(filePath, validateCrc: true);

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

            for (var i = 0; i < sparseFile.Chunks.Count; i++)
            {
                SparseChunk chunk = sparseFile.Chunks[i];

                var chunkInfo = new ChunkInfo
                {
                    Index = (uint)i,
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
    /// <para>根据魔数判断指定文件是否为有效的稀疏镜像。</para>
    /// </summary>
    /// <param name="filePath">The path to the file to check. <para>要检查的文件路径。</para></param>
    /// <returns><c>true</c> if the file is a valid sparse image; otherwise, <c>false</c>. <para>如果文件是有效的稀疏镜像则为 true；否则为 false。</para></returns>
    public static bool IsSparseImage(string filePath)
    {
        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            Span<byte> headerBytes = stackalloc byte[SparseFormat.SparseHeaderSize];
            if (stream.Read(headerBytes) != SparseFormat.SparseHeaderSize)
            {
                return false;
            }

            var header = SparseHeader.FromBytes(headerBytes);
            return header.IsValid();
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Retrieves detailed metadata and compression information for a sparse image file.
    /// <para>获取稀疏镜像文件的详细元数据和压缩信息。</para>
    /// </summary>
    /// <param name="filePath">The path to the sparse image file. <para>稀疏镜像文件的路径。</para></param>
    /// <returns>A <see cref="SparseImageInfo"/> containing metadata such as uncompressed size and compression ratio. <para>包含未压缩大小和压缩比等元数据的 SparseImageInfo。</para></returns>
    public static SparseImageInfo GetSparseImageInfo(string filePath)
    {
        try
        {
            if (!IsSparseImage(filePath))
            {
                return new SparseImageInfo { Success = false, ErrorMessage = "Not a valid sparse image file", FilePath = filePath };
            }

            SparseHeader header = SparseFile.PeekHeader(filePath);
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
