namespace FirmwareKit.Sparse.Utils;

/// <summary>
/// Sparse image utility tools
/// </summary>
public static class SparseImageUtils
{
    /// <summary>
    /// Gets detailed information about a file
    /// </summary>
    public static FileInfoResult GetFileInfo(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"File not found: {filePath}");
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
            var header = SparseFile.PeekHeader(filePath);
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
    /// Compares size and type of two files
    /// </summary>
    public static FileComparisonResult CompareFiles(string file1, string file2)
    {
        if (!File.Exists(file1))
        {
            throw new FileNotFoundException($"File not found: {file1}");
        }

        if (!File.Exists(file2))
        {
            throw new FileNotFoundException($"File not found: {file2}");
        }

        var info1 = new FileInfo(file1);
        var info2 = new FileInfo(file2);

        var isSparse1 = SparseImageValidator.IsSparseImage(file1);
        var isSparse2 = SparseImageValidator.IsSparseImage(file2);

        var result = new FileComparisonResult
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

        return result;
    }

    /// <summary>
    /// Verifies the consistency of the conversion result
    /// </summary>
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
    /// Creates a test sparse image
    /// </summary>
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

    /// <summary>
    /// Extracts valid data from a sparse image
    /// </summary>
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

            using var sparseFile = SparseFile.FromStream(inputStream);
            var blockSize = sparseFile.Header.BlockSize;
            var startBlock = (uint)(partitionOffset / blockSize);
            var offsetInBlock = partitionOffset % blockSize;

            var currentBlock = 0u;
            var dataExtracted = false;
            long totalBytesExtracted = 0;

            foreach (var chunk in sparseFile.Chunks)
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

                        case (ushort)ChunkType.DontCare:
                            break;

                        case (ushort)ChunkType.Crc32:
                            break;

                        default:
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
    /// Extracts data from a raw chunk.
    /// </summary>
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
    /// Extracts data from a fill chunk.
    /// </summary>
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
    /// CSV Format: [Index], File Offset (bytes), File Length (bytes), Device Offset (bytes), Device Length (bytes)
    /// </summary>
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
            using var sparseFile = SparseFile.FromStream(stream);
            var header = sparseFile.Header;

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

            foreach (var chunk in sparseFile.Chunks)
            {
                var chunkStartBlock = currentBlockNumber;
                var chunkEndBlock = currentBlockNumber + chunk.Header.ChunkSize;
                if (chunkEndBlock > startBlockNumber)
                {
                    switch (chunk.Header.ChunkType)
                    {
                        case (ushort)ChunkType.Raw:
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
                                    var deviceOffset = Math.Max(partitionOffset, (long)chunkStartBlock * blockSize);
                                    csvRecords.Add($"{sequenceNumber},{chunkFileOffset},{lengthToCopy},{deviceOffset},{lengthToCopy}");
                                    sequenceNumber++;
                                    foundValidData = true;
                                }
                            }
                            break;

                        case (ushort)ChunkType.Fill:
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
                                var fillDeviceOffset = Math.Max(partitionOffset, chunkStartBlock * blockSize);
                                csvRecords.Add($"{sequenceNumber},{fillFileOffset},{fillDataLength},{fillDeviceOffset},{fillDataLength}");
                                sequenceNumber++;
                                foundValidData = true;
                            }
                            break;

                        case (ushort)ChunkType.DontCare:
                            break;

                        case (ushort)ChunkType.Crc32:
                            break;

                        default:
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
    /// Writes fill data to a stream.
    /// </summary>
    private static void WriteFillData(Stream outputStream, byte[] fillPattern, long totalBytes)
    {
        var remainingBytes = totalBytes;

        while (remainingBytes > 0)
        {
            var bytesToWrite = (int)Math.Min(remainingBytes, fillPattern.Length);
            outputStream.Write(fillPattern, 0, bytesToWrite);
            remainingBytes -= bytesToWrite;
        }
    }
}
