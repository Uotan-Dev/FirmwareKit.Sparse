namespace FirmwareKit.Sparse.Utils;

/// <summary>
/// Sparse image utility tools providing file info, comparison, conversion verification,
/// test image creation, and data extraction capabilities.
/// <para>稀疏镜像实用工具，提供文件信息、比较、转换验证、测试镜像创建和数据提取功能。</para>
/// </summary>
public static partial class SparseImageUtils
{
    /// <summary>
    /// Gets detailed information about a file, including sparse metadata if applicable.
    /// <para>获取文件的详细信息，包括适用的稀疏元数据。</para>
    /// </summary>
    /// <param name="filePath">The file path. <para>文件路径。</para></param>
    /// <returns>A <see cref="FileInfoResult"/> containing file details. <para>包含文件详细信息的 FileInfoResult。</para></returns>
    public static FileInfoResult GetFileInfo(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return new FileInfoResult
            {
                Success = false,
                ErrorMessage = $"File not found: {filePath}"
            };
        }

        var fileInfo = new FileInfo(filePath);
        var result = new FileInfoResult
        {
            Success = true,
            FilePath = filePath,
            FileSize = fileInfo.Length,
            IsSparseImage = SparseImageValidator.IsSparseImage(filePath)
        };

        if (result.IsSparseImage)
        {
            SparseHeader header = SparseFile.PeekHeader(filePath);
            result = result with
            {
                SparseInfo = new SparseFileInfo
                {
                    Version = $"{header.MajorVersion}.{header.MinorVersion}",
                    BlockSize = header.BlockSize,
                    TotalBlocks = header.TotalBlocks,
                    TotalChunks = header.TotalChunks,
                    UncompressedSize = (long)header.TotalBlocks * header.BlockSize
                }
            };
        }

        return result;
    }

    /// <summary>
    /// Compares the size and type of two files.
    /// <para>比较两个文件的大小和类型。</para>
    /// </summary>
    /// <param name="file1">The path to the first file. <para>第一个文件的路径。</para></param>
    /// <param name="file2">The path to the second file. <para>第二个文件的路径。</para></param>
    /// <returns>A <see cref="FileComparisonResult"/> containing the comparison results. <para>包含比较结果的 FileComparisonResult。</para></returns>
    public static FileComparisonResult CompareFiles(string file1, string file2)
    {
        if (!File.Exists(file1))
        {
            return new FileComparisonResult
            {
                Success = false,
                ErrorMessage = $"File not found: {file1}"
            };
        }

        if (!File.Exists(file2))
        {
            return new FileComparisonResult
            {
                Success = false,
                ErrorMessage = $"File not found: {file2}"
            };
        }

        var info1 = new FileInfo(file1);
        var info2 = new FileInfo(file2);

        var isSparse1 = SparseImageValidator.IsSparseImage(file1);
        var isSparse2 = SparseImageValidator.IsSparseImage(file2);

        return new FileComparisonResult
        {
            Success = true,
            File1Info = new FileBasicInfo
            {
                Path = file1,
                Size = info1.Length,
                Type = isSparse1 ? "Sparse" : "Raw"
            },
            File2Info = new FileBasicInfo
            {
                Path = file2,
                Size = info2.Length,
                Type = isSparse2 ? "Sparse" : "Raw"
            },
            SizeMatches = info1.Length == info2.Length,
            TypeMatches = isSparse1 == isSparse2
        };
    }

    /// <summary>
    /// Verifies the consistency of files before and after conversion.
    /// <para>验证转换前后文件的一致性。</para>
    /// </summary>
    /// <param name="originalFile">The path to the original file. <para>原始文件的路径。</para></param>
    /// <param name="convertedFile">The path to the converted file. <para>转换后文件的路径。</para></param>
    /// <returns>A <see cref="ConversionVerificationResult"/> containing the verification results. <para>包含验证结果的 ConversionVerificationResult。</para></returns>
    public static ConversionVerificationResult VerifyConversion(string originalFile, string convertedFile)
    {
        try
        {
            var original = new FileInfo(originalFile);
            var converted = new FileInfo(convertedFile);

            return new ConversionVerificationResult
            {
                Success = true,
                OriginalSize = original.Length,
                ConvertedSize = converted.Length,
                SizesMatch = original.Length == converted.Length
            };
        }
        catch (Exception ex)
        {
            return new ConversionVerificationResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// Creates a test sparse image with sample data chunks.
    /// <para>创建包含示例数据块的测试稀疏镜像。</para>
    /// </summary>
    /// <param name="outputPath">The output path. <para>输出路径。</para></param>
    /// <param name="sizeInMB">The size in megabytes (MB). <para>大小（兆字节）。</para></param>
    /// <param name="blockSize">The block size. <para>块大小。</para></param>
    /// <returns>A <see cref="TestImageCreationResult"/> containing the result of the test image creation. <para>包含测试镜像创建结果的 TestImageCreationResult。</para></returns>
    public static TestImageCreationResult CreateTestSparseImage(string outputPath, uint sizeInMB = 100, uint blockSize = 4096)
    {
        try
        {
            var totalSize = (long)sizeInMB * 1024 * 1024;
            using var sparseFile = new SparseFile(blockSize, totalSize);
            var testData = Enumerable.Range(0, (int)blockSize).Select(i => (byte)(i % 256)).ToArray();
            for (uint i = 0; i < 10; i++)
            {
                sparseFile.AddRawChunk(testData);
            }
            sparseFile.AddFillChunk(0xDEADBEEF, blockSize * 50);
            sparseFile.AddDontCareChunk(blockSize * 100);

            using var outputStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
            sparseFile.WriteToStream(outputStream);

            return new TestImageCreationResult
            {
                Success = true,
                OutputPath = outputPath,
                SizeInMB = sizeInMB,
                BlockSize = blockSize,
                TotalChunks = sparseFile.Chunks.Count
            };
        }
        catch (Exception ex)
        {
            return new TestImageCreationResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

}
