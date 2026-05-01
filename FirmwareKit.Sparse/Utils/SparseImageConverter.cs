namespace FirmwareKit.Sparse.Utils;

/// <summary>
/// Utility class for sparse image format conversion operations.
/// <para>稀疏镜像格式转换操作的实用工具类。</para>
/// </summary>
public static class SparseImageConverter
{
    /// <summary>
    /// Converts sparse images back to a raw image asynchronously.
    /// <para>异步将稀疏镜像转换回原始镜像。</para>
    /// </summary>
    /// <param name="inputFiles">A collection of input sparse image file paths. <para>输入稀疏镜像文件路径的集合。</para></param>
    /// <param name="outputFile">The path to the output raw image file. <para>输出原始镜像文件的路径。</para></param>
    /// <param name="cancellationToken">The cancellation token. <para>取消令牌。</para></param>
    /// <returns>A task representing the asynchronous conversion operation. <para>表示异步转换操作的任务。</para></returns>
    public static async Task ConvertSparseToRawAsync(IEnumerable<string> inputFiles, string outputFile, CancellationToken cancellationToken = default)
    {
        using var outputStream = new FileStream(outputFile, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.SequentialScan);
        long maxFileSize = 0;

        foreach (var inputFile in inputFiles)
        {
            SparseHeader header = SparseFile.PeekHeader(inputFile);
            var fileSize = (long)header.TotalBlocks * header.BlockSize;
            if (fileSize > maxFileSize) maxFileSize = fileSize;
        }

        if (maxFileSize > 0)
        {
            outputStream.SetLength(maxFileSize);
        }

        foreach (var inputFile in inputFiles)
        {
            using SparseFile sparseFile = await SparseFile.FromImageFileAsync(inputFile, true, false, null, cancellationToken);
            await sparseFile.WriteRawToStreamAsync(outputStream, true, cancellationToken);
        }

        await outputStream.FlushAsync(cancellationToken);
    }

    /// <summary>
    /// Converts a raw image to a sparse image asynchronously.
    /// <para>异步将原始镜像转换为稀疏镜像。</para>
    /// </summary>
    /// <param name="inputFile">The path to the input raw image file. <para>输入原始镜像文件的路径。</para></param>
    /// <param name="outputFile">The path to the output sparse image file. <para>输出稀疏镜像文件的路径。</para></param>
    /// <param name="blockSize">The block size in bytes (default is 4096). <para>块大小（字节），默认为 4096。</para></param>
    /// <param name="cancellationToken">The cancellation token. <para>取消令牌。</para></param>
    /// <returns>A task representing the asynchronous conversion operation. <para>表示异步转换操作的任务。</para></returns>
    public static async Task ConvertRawToSparseAsync(string inputFile, string outputFile, uint blockSize = 4096, CancellationToken cancellationToken = default)
    {
        using var outputStream = new FileStream(outputFile, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.SequentialScan);
        using SparseFile sparseFile = await SparseReader.FromRawFileAsync(inputFile, blockSize, false, null, cancellationToken);
        await sparseFile.WriteToStreamAsync(outputStream, true, false, false, cancellationToken);
    }

    /// <summary>
    /// Splits a large sparse image into multiple sparse images of a specified maximum size.
    /// <para>将大型稀疏镜像拆分为指定最大大小的多个稀疏镜像。</para>
    /// </summary>
    /// <param name="inputFile">The path to the input sparse image file. <para>输入稀疏镜像文件的路径。</para></param>
    /// <param name="outputPattern">The pattern for output file paths; can include {0} for the index placeholder. <para>输出文件路径的模式；可包含 {0} 作为索引占位符。</para></param>
    /// <param name="maxFileSize">The maximum size in bytes for each split file. <para>每个拆分文件的最大字节数。</para></param>
    public static void ResparseImage(string inputFile, string outputPattern, long maxFileSize)
    {
        using var stream = new FileStream(inputFile, FileMode.Open, FileAccess.Read);
        using var sparseFile = SparseFile.FromStream(stream, validateCrc: true);

        var i = 0;
        foreach (SparseFile file in sparseFile.Resparse(maxFileSize))
        {
            using (file)
            {
                var outPath = outputPattern.Contains("{0}")
                    ? string.Format(outputPattern, i)
                    : $"{outputPattern}.{i:D2}";

                using var outStream = new FileStream(outPath, FileMode.Create, FileAccess.Write);
                file.WriteToStream(outStream, true);
                i++;
            }
        }
    }
}
