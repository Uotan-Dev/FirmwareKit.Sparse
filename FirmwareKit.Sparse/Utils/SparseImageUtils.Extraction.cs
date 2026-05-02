namespace FirmwareKit.Sparse.Utils;

using FirmwareKit.Sparse.Core;
using FirmwareKit.Sparse.Models;
using System.Buffers.Binary;

/// <summary>
/// Data extraction methods for <see cref="SparseImageUtils"/>.
/// <para>SparseImageUtils 的数据提取方法。</para>
/// </summary>
public static partial class SparseImageUtils
{
    /// <summary>
    /// Extracts valid data from a sparse image starting at the specified partition offset.
    /// <para>从稀疏镜像中提取从指定分区偏移开始的有效数据。</para>
    /// </summary>
    /// <param name="inputPath">The input sparse image path. <para>输入稀疏镜像路径。</para></param>
    /// <param name="outputPath">The output binary data path. <para>输出二进制数据路径。</para></param>
    /// <param name="partitionOffset">The partition offset in bytes. <para>分区偏移（字节）。</para></param>
    /// <returns>A <see cref="DataExtractionResult"/> containing the result of the data extraction. <para>包含数据提取结果的 DataExtractionResult。</para></returns>
    public static DataExtractionResult ExtractValidData(string inputPath, string outputPath, long partitionOffset)
    {
        try
        {
            if (!SparseImageValidator.IsSparseImage(inputPath))
            {
                return new DataExtractionResult
                {
                    Success = false,
                    ErrorMessage = "Input file is not a valid sparse image"
                };
            }

            using var inputStream = new FileStream(inputPath, FileMode.Open, FileAccess.Read);
            using var outputStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write);

            using var sparseFile = SparseFile.FromStream(inputStream, validateCrc: true);
            var blockSize = sparseFile.Header.BlockSize;
            var startBlock = (uint)(partitionOffset / blockSize);
            var offsetInBlock = partitionOffset % blockSize;

            var currentBlock = 0u;
            var dataExtracted = false;
            long totalBytesExtracted = 0;

            foreach (SparseChunk chunk in sparseFile.Chunks)
            {
                var chunkEndBlock = currentBlock + chunk.Header.ChunkSize;
                if (chunkEndBlock > startBlock)
                {
                    switch (chunk.Header.ChunkType)
                    {
                        case (ushort)ChunkType.Raw:
                            if (chunk.DataProvider != null)
                            {
                                var bytesExtracted = ExtractRawChunkData(chunk, currentBlock, startBlock, offsetInBlock, blockSize, outputStream);
                                totalBytesExtracted += bytesExtracted;
                                if (bytesExtracted > 0)
                                {
                                    dataExtracted = true;
                                }
                            }
                            break;

                        case (ushort)ChunkType.Fill:
                            var fillBytesExtracted = ExtractFillChunkData(chunk, currentBlock, startBlock, offsetInBlock, blockSize, outputStream);
                            totalBytesExtracted += fillBytesExtracted;
                            if (fillBytesExtracted > 0)
                            {
                                dataExtracted = true;
                            }
                            break;
                    }
                }

                currentBlock = chunkEndBlock;
            }

            return new DataExtractionResult
            {
                Success = true,
                InputPath = inputPath,
                OutputPath = outputPath,
                PartitionOffset = partitionOffset,
                BlockSize = blockSize,
                StartBlock = startBlock,
                OffsetInBlock = offsetInBlock,
                TotalBytesExtracted = totalBytesExtracted,
                DataFound = dataExtracted
            };
        }
        catch (Exception ex)
        {
            return new DataExtractionResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// Extracts data from a RAW chunk and writes it to the provided output stream.
    /// <para>从 RAW 数据块中提取数据并写入提供的输出流。</para>
    /// </summary>
    /// <param name="chunk">The <see cref="SparseChunk"/> to extract data from. <para>要提取数据的 SparseChunk。</para></param>
    /// <param name="currentBlock">The block index where this chunk starts. <para>此数据块开始的块索引。</para></param>
    /// <param name="startBlock">The first block index to include from the partition offset. <para>从分区偏移开始包含的第一个块索引。</para></param>
    /// <param name="offsetInBlock">Byte offset within the start block. <para>起始块内的字节偏移。</para></param>
    /// <param name="blockSize">Size of a block in bytes. <para>块的字节大小。</para></param>
    /// <param name="outputStream">Stream to write extracted data into. <para>写入提取数据的流。</para></param>
    /// <returns>The number of bytes written to <paramref name="outputStream"/>. <para>写入输出流的字节数。</para></returns>
    private static long ExtractRawChunkData(SparseChunk chunk, uint currentBlock, uint startBlock, long offsetInBlock, uint blockSize, Stream outputStream)
    {
        if (chunk.DataProvider == null)
        {
            return 0;
        }

        long startOffsetInChunk = 0;
        if (currentBlock <= startBlock && startBlock < currentBlock + chunk.Header.ChunkSize)
        {
            var blocksToSkip = startBlock - currentBlock;
            startOffsetInChunk = (blocksToSkip * blockSize) + offsetInBlock;
        }
        else if (currentBlock < startBlock)
        {
            return 0;
        }

        var length = chunk.DataProvider.Length - startOffsetInChunk;
        if (length <= 0)
        {
            return 0;
        }

        var buffer = new byte[1024 * 1024];
        long totalRead = 0;
        while (totalRead < length)
        {
            var toRead = (int)Math.Min(buffer.Length, length - totalRead);
            var read = chunk.DataProvider.Read(startOffsetInChunk + totalRead, buffer, 0, toRead);
            if (read <= 0)
            {
                break;
            }

            outputStream.Write(buffer, 0, read);
            totalRead += read;
        }
        return totalRead;
    }

    /// <summary>
    /// Extracts data from a FILL chunk, writing the repeated fill pattern to the output stream.
    /// <para>从 FILL 数据块中提取数据，将重复的填充模式写入输出流。</para>
    /// </summary>
    /// <param name="chunk">The <see cref="SparseChunk"/> representing the fill region. <para>表示填充区域的 SparseChunk。</para></param>
    /// <param name="currentBlock">The block index where this chunk starts. <para>此数据块开始的块索引。</para></param>
    /// <param name="startBlock">The first block index to include from the partition offset. <para>从分区偏移开始包含的第一个块索引。</para></param>
    /// <param name="offsetInBlock">Byte offset within the start block. <para>起始块内的字节偏移。</para></param>
    /// <param name="blockSize">Size of a block in bytes. <para>块的字节大小。</para></param>
    /// <param name="outputStream">Stream to write the generated fill bytes into. <para>写入生成的填充字节的流。</para></param>
    /// <returns>The number of bytes written to <paramref name="outputStream"/>. <para>写入输出流的字节数。</para></returns>
    private static long ExtractFillChunkData(SparseChunk chunk, uint currentBlock, uint startBlock, long offsetInBlock, uint blockSize, Stream outputStream)
    {
        var fillBytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(fillBytes, chunk.FillValue);
        var totalSize = (long)chunk.Header.ChunkSize * blockSize;

        if (currentBlock <= startBlock && startBlock < currentBlock + chunk.Header.ChunkSize)
        {
            var blocksToSkip = startBlock - currentBlock;
            var bytesToSkip = (blocksToSkip * blockSize) + offsetInBlock;

            if (bytesToSkip < totalSize)
            {
                var remainingBytes = totalSize - bytesToSkip;
                WriteFillData(outputStream, fillBytes, remainingBytes);
                return remainingBytes;
            }
        }
        else if (currentBlock >= startBlock)
        {
            WriteFillData(outputStream, fillBytes, totalSize);
            return totalSize;
        }

        return 0;
    }

    /// <summary>
    /// Extracts valid data and generates a corresponding CSV map.
    /// CSV format: [Index], File Offset (bytes), File Length (bytes), Device Offset (bytes), Device Length (bytes)
    /// <para>提取有效数据并生成对应的 CSV 映射。CSV 格式：[索引], 文件偏移(字节), 文件长度(字节), 设备偏移(字节), 设备长度(字节)</para>
    /// </summary>
    /// <param name="sparseImagePath">The path to the sparse image. <para>稀疏镜像的路径。</para></param>
    /// <param name="binOutputPath">The binary output path. <para>二进制输出路径。</para></param>
    /// <param name="csvOutputPath">The CSV output path. <para>CSV 输出路径。</para></param>
    /// <param name="partitionOffset">The partition offset. <para>分区偏移。</para></param>
    /// <returns>A <see cref="DataExtractionWithCsvResult"/> containing the result of the data extraction with CSV output. <para>包含带 CSV 输出的数据提取结果的 DataExtractionWithCsvResult。</para></returns>
    public static DataExtractionWithCsvResult ExtractValidDataWithCsv(string sparseImagePath, string binOutputPath, string csvOutputPath, long partitionOffset)
    {
        try
        {
            if (!SparseImageValidator.IsSparseImage(sparseImagePath))
            {
                return new DataExtractionWithCsvResult
                {
                    Success = false,
                    ErrorMessage = "Not a valid sparse image file"
                };
            }

            using var stream = new FileStream(sparseImagePath, FileMode.Open, FileAccess.Read);
            using var sparseFile = SparseFile.FromStream(stream, validateCrc: true);
            SparseHeader header = sparseFile.Header;

            var blockSize = header.BlockSize;
            var startBlockNumber = partitionOffset / blockSize;
            var blockOffset = partitionOffset % blockSize;

            var csvRecords = new List<string>
            {
                "Index,File Offset(b),File Length(b),Device Offset(b),Device Length(b)"
            };

            using var outputStream = new FileStream(binOutputPath, FileMode.Create, FileAccess.Write);

            var currentBlockNumber = 0u;
            var sequenceNumber = 1;
            var fileOffset = 0L;
            var foundValidData = false;

            foreach (SparseChunk chunk in sparseFile.Chunks)
            {
                var chunkStartBlock = currentBlockNumber;
                var chunkEndBlock = currentBlockNumber + chunk.Header.ChunkSize;
                if (chunkEndBlock > startBlockNumber)
                {
                    switch (chunk.Header.ChunkType)
                    {
                        case (ushort)ChunkType.Raw:
                            ProcessRawChunkForCsv(chunk, chunkStartBlock, chunkEndBlock,
                                startBlockNumber, blockOffset, blockSize, outputStream,
                                ref sequenceNumber, ref fileOffset, csvRecords, ref foundValidData);
                            break;

                        case (ushort)ChunkType.Fill:
                            ProcessFillChunkForCsv(chunk, chunkStartBlock, chunkEndBlock,
                                startBlockNumber, blockOffset, blockSize, outputStream,
                                ref sequenceNumber, ref fileOffset, csvRecords, ref foundValidData);
                            break;
                    }
                }

                currentBlockNumber += chunk.Header.ChunkSize;
            }
            File.WriteAllLines(csvOutputPath, csvRecords);

            return new DataExtractionWithCsvResult
            {
                Success = true,
                InputPath = sparseImagePath,
                BinOutputPath = binOutputPath,
                CsvOutputPath = csvOutputPath,
                PartitionOffset = partitionOffset,
                BlockSize = blockSize,
                StartBlockNumber = startBlockNumber,
                BlockOffset = blockOffset,
                TotalBytesExtracted = fileOffset,
                CsvRecordCount = csvRecords.Count - 1,
                DataFound = foundValidData
            };
        }
        catch (Exception ex)
        {
            return new DataExtractionWithCsvResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// Processes a RAW chunk for CSV extraction, writing data and recording CSV entries.
    /// <para>处理 RAW 数据块的 CSV 提取，写入数据并记录 CSV 条目。</para>
    /// </summary>
    private static void ProcessRawChunkForCsv(SparseChunk chunk, uint chunkStartBlock, uint chunkEndBlock,
        long startBlockNumber, long blockOffset, uint blockSize, Stream outputStream,
        ref int sequenceNumber, ref long fileOffset, List<string> csvRecords, ref bool foundValidData)
    {
        var skipBytes = 0L;
        var dataLength = (long)(chunk.Header.ChunkSize * blockSize);
        if (startBlockNumber >= chunkStartBlock && startBlockNumber < chunkEndBlock)
        {
            skipBytes = ((startBlockNumber - chunkStartBlock) * blockSize) + blockOffset;
            dataLength -= skipBytes;
        }
        else if (startBlockNumber < chunkStartBlock)
        {
            skipBytes = 0;
        }

        if (dataLength > 0 && chunk.DataProvider != null)
        {
            var chunkFileOffset = fileOffset;
            var sourceOffset = skipBytes;
            var lengthToCopy = Math.Min(dataLength, chunk.DataProvider.Length - sourceOffset);

            if (lengthToCopy > 0)
            {
                var buffer = new byte[1024 * 1024];
                long chunkRead = 0;
                while (chunkRead < lengthToCopy)
                {
                    var toRead = (int)Math.Min(buffer.Length, lengthToCopy - chunkRead);
                    var read = chunk.DataProvider.Read(sourceOffset + chunkRead, buffer, 0, toRead);
                    if (read <= 0)
                    {
                        break;
                    }

                    outputStream.Write(buffer, 0, read);
                    chunkRead += read;
                }
                fileOffset += lengthToCopy;
                long deviceOffset;
                if (startBlockNumber >= chunkStartBlock && startBlockNumber < chunkEndBlock)
                {
                    deviceOffset = (startBlockNumber * blockSize) + blockOffset;
                }
                else
                {
                    deviceOffset = (long)chunkStartBlock * blockSize;
                }
                csvRecords.Add($"{sequenceNumber},{chunkFileOffset},{lengthToCopy},{deviceOffset},{lengthToCopy}");
                sequenceNumber++;
                foundValidData = true;
            }
        }
    }

    /// <summary>
    /// Processes a FILL chunk for CSV extraction, writing fill data and recording CSV entries.
    /// <para>处理 FILL 数据块的 CSV 提取，写入填充数据并记录 CSV 条目。</para>
    /// </summary>
    private static void ProcessFillChunkForCsv(SparseChunk chunk, uint chunkStartBlock, uint chunkEndBlock,
        long startBlockNumber, long blockOffset, uint blockSize, Stream outputStream,
        ref int sequenceNumber, ref long fileOffset, List<string> csvRecords, ref bool foundValidData)
    {
        var fillBytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(fillBytes, chunk.FillValue);
        var fillDataLength = (long)(chunk.Header.ChunkSize * blockSize);
        var fillSkipBytes = 0L;
        if (startBlockNumber >= chunkStartBlock && startBlockNumber < chunkEndBlock)
        {
            fillSkipBytes = ((startBlockNumber - chunkStartBlock) * blockSize) + blockOffset;
            fillDataLength -= fillSkipBytes;
        }
        else if (startBlockNumber < chunkStartBlock)
        {
            fillSkipBytes = 0;
        }

        if (fillDataLength > 0)
        {
            var fillFileOffset = fileOffset;
            WriteFillData(outputStream, fillBytes, fillDataLength);
            fileOffset += fillDataLength;
            long fillDeviceOffset;
            if (startBlockNumber >= chunkStartBlock && startBlockNumber < chunkEndBlock)
            {
                fillDeviceOffset = (startBlockNumber * blockSize) + blockOffset;
            }
            else
            {
                fillDeviceOffset = (long)chunkStartBlock * blockSize;
            }
            csvRecords.Add($"{sequenceNumber},{fillFileOffset},{fillDataLength},{fillDeviceOffset},{fillDataLength}");
            sequenceNumber++;
            foundValidData = true;
        }
    }

    /// <summary>
    /// Writes a repeated fill pattern to the output stream for the requested number of bytes.
    /// <para>将重复的填充模式写入输出流，写入请求的字节数。</para>
    /// </summary>
    /// <param name="outputStream">Stream to receive the fill bytes. <para>接收填充字节的流。</para></param>
    /// <param name="fillPattern">Byte pattern (typically 4 bytes) that is repeated. <para>重复的字节模式（通常为 4 字节）。</para></param>
    /// <param name="totalBytes">Total number of bytes to write from the repeated pattern. <para>从重复模式写入的总字节数。</para></param>
    private static void WriteFillData(Stream outputStream, byte[] fillPattern, long totalBytes)
    {
        if (totalBytes <= 0 || fillPattern.Length == 0) return;

        const int bufferSize = 64 * 1024;
        var buffer = new byte[bufferSize];
        var patternLen = fillPattern.Length;

        for (int i = 0; i < bufferSize; i += patternLen)
        {
            var copyLen = Math.Min(patternLen, bufferSize - i);
            Array.Copy(fillPattern, 0, buffer, i, copyLen);
        }

        var remaining = totalBytes;
        while (remaining > 0)
        {
            var toWrite = (int)Math.Min(remaining, bufferSize);
            outputStream.Write(buffer, 0, toWrite);
            remaining -= toWrite;
        }
    }
}
