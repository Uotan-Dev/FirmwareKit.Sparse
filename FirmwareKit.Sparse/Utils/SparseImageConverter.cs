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
    public static void ConvertSparseToRaw(IEnumerable<string> inputFiles, string outputFile)
    {
        using var outputStream = new FileStream(outputFile, FileMode.Create, FileAccess.Write);
        long maxFileSize = 0;
        foreach (var inputFile in inputFiles)
        {
            var header = SparseFile.PeekHeader(inputFile);
            var fileSize = (long)header.TotalBlocks * header.BlockSize;
            maxFileSize = Math.Max(maxFileSize, fileSize);
        }
        outputStream.SetLength(maxFileSize);
        foreach (var inputFile in inputFiles)
        {
            using var sparseFile = SparseFile.FromImageFile(inputFile);
            sparseFile.WriteRawToStream(outputStream, true);
        }
    }

    /// <summary>
    /// Converts a raw image to a sparse image.
    /// </summary>
    /// <param name="inputFile">The path to the input raw image file.</param>
    /// <param name="outputFile">The path to the output sparse image file.</param>
    /// <param name="blockSize">The block size (default is 4096).</param>
    public static void ConvertRawToSparse(string inputFile, string outputFile, uint blockSize = 4096)
    {
        using var outputStream = new FileStream(outputFile, FileMode.Create, FileAccess.Write);
        using var sparseFile = SparseFile.FromRawFile(inputFile, blockSize);
        sparseFile.WriteToStream(outputStream);
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
        using var sparseFile = SparseFile.FromStream(stream);
        var files = sparseFile.Resparse(maxFileSize);

        try
        {
            for (var i = 0; i < files.Count; i++)
            {
                var outPath = outputPattern.Contains("{0}")
                    ? string.Format(outputPattern, i)
                    : $"{outputPattern}.{i:D2}";

                using var outStream = new FileStream(outPath, FileMode.Create, FileAccess.Write);
                files[i].WriteToStream(outStream, true);
            }
        }
        finally
        {
            foreach (var f in files)
            {
                f.Dispose();
            }
        }
    }
}
