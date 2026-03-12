// using FirmwareKit.Lp;

namespace FirmwareKit.Sparse.Utils;

/// <summary>
/// Utility class for sparse image conversion.
/// </summary>
public static class SparseImageConverter
{
    /// <summary>
    /// Converts sparse images back to a raw image.
    /// </summary>
    /// <param name="inputFiles">A collection of input sparse image files.</param>
    /// <param name="outputFile">The path to the output raw image file.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public static async Task ConvertSparseToRawAsync(IEnumerable<string> inputFiles, string outputFile, CancellationToken cancellationToken = default)
    {
        // Using FileOptions.WriteThrough and SequentialScan for better I/O performance
        using var outputStream = new FileStream(outputFile, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.SequentialScan);
        long maxFileSize = 0;

        // Peek headers to determine total output size first to minimize disk allocation overhead
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
    /// Converts a raw image to a sparse image.
    /// </summary>
    /// <param name="inputFile">The path to the input raw image file.</param>
    /// <param name="outputFile">The path to the output sparse image file.</param>
    /// <param name="blockSize">The block size (default is 4096).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public static async Task ConvertRawToSparseAsync(string inputFile, string outputFile, uint blockSize = 4096, CancellationToken cancellationToken = default)
    {
        using var outputStream = new FileStream(outputFile, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.SequentialScan);
        using SparseFile sparseFile = await SparseReader.FromRawFileAsync(inputFile, blockSize, false, null, cancellationToken);
        await sparseFile.WriteToStreamAsync(outputStream, true, false, false, cancellationToken);
    }

    /// <summary>
    /// Splits a large sparse image into multiple sparse images of a specified maximum size.
    /// </summary>
    /// <param name="inputFile">The path to the input sparse image file.</param>
    /// <param name="outputPattern">The pattern for output file paths; can include {0} for the index placeholder.</param>
    /// <param name="maxFileSize">The maximum size in bytes for each split file.</param>
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
