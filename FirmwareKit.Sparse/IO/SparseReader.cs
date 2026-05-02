namespace FirmwareKit.Sparse.IO;

using FirmwareKit.Sparse.Core;
using FirmwareKit.Sparse.DataProviders;
using FirmwareKit.Sparse.Models;
using FirmwareKit.Sparse.Utils;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;

/// <summary>
/// Provides methods for reading and importing sparse image data.
/// <para>提供读取和导入稀疏镜像数据的方法。</para>
/// </summary>
public static partial class SparseReader
{
    /// <summary>
    /// Peeks at the sparse header of a file without reading the entire content.
    /// <para>预览文件的稀疏头部，不读取全部内容。</para>
    /// </summary>
    /// <param name="filePath">The path to the sparse image file. <para>稀疏镜像文件的路径。</para></param>
    /// <returns>A <see cref="SparseHeader"/> containing the metadata of the sparse image. <para>包含稀疏镜像元数据的 SparseHeader。</para></returns>
    public static SparseHeader PeekHeader(string filePath)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        Span<byte> headerData = stackalloc byte[SparseFormat.SparseHeaderSize];
        stream.ReadExactly(headerData);
        return SparseHeader.FromBytes(headerData);
    }

    /// <summary>
    /// Loads a <see cref="SparseFile"/> from the provided <see cref="Stream"/>.
    /// <para>从提供的 Stream 加载 SparseFile。</para>
    /// </summary>
    /// <param name="stream">Input stream containing the sparse image data. <para>包含稀疏镜像数据的输入流。</para></param>
    /// <param name="validateCrc">If true, validate CRC checksums while parsing. <para>如果为 true，解析时验证 CRC 校验和。</para></param>
    /// <param name="verbose">If true, enable verbose logging. <para>如果为 true，启用详细日志。</para></param>
    /// <param name="logger">Optional logger instance for diagnostic messages. <para>可选的日志记录器实例。</para></param>
    /// <returns>A <see cref="SparseFile"/> parsed from the stream. <para>从流解析的 SparseFile。</para></returns>
    public static SparseFile FromStream(Stream stream, bool validateCrc = false, bool verbose = false, ISparseLogger? logger = null)
    {
        return FromStreamInternal(stream, null, validateCrc, verbose, logger);
    }

    /// <summary>
    /// Loads a <see cref="SparseFile"/> from a byte array that contains the sparse image.
    /// <para>从包含稀疏镜像数据的字节数组加载 SparseFile。</para>
    /// </summary>
    /// <param name="buffer">Byte array containing the sparse image data. <para>包含稀疏镜像数据的字节数组。</para></param>
    /// <param name="validateCrc">If true, validate CRC checksums while parsing. <para>如果为 true，解析时验证 CRC 校验和。</para></param>
    /// <param name="verbose">If true, enable verbose logging. <para>如果为 true，启用详细日志。</para></param>
    /// <param name="logger">Optional logger instance for diagnostic messages. <para>可选的日志记录器实例。</para></param>
    /// <returns>A <see cref="SparseFile"/> parsed from the buffer. <para>从缓冲区解析的 SparseFile。</para></returns>
    public static SparseFile FromBuffer(byte[] buffer, bool validateCrc = false, bool verbose = false, ISparseLogger? logger = null)
    {
        using var ms = new MemoryStream(buffer);
        return FromStream(ms, validateCrc, verbose, logger);
    }

    /// <summary>
    /// Loads a <see cref="SparseFile"/> directly from an image file on disk.
    /// <para>直接从磁盘上的镜像文件加载 SparseFile。</para>
    /// </summary>
    /// <param name="filePath">Path to the image file on disk. <para>磁盘上镜像文件的路径。</para></param>
    /// <param name="validateCrc">If true, validate CRC checksums while parsing. <para>如果为 true，解析时验证 CRC 校验和。</para></param>
    /// <param name="verbose">If true, enable verbose logging. <para>如果为 true，启用详细日志。</para></param>
    /// <param name="logger">Optional logger instance for diagnostic messages. <para>可选的日志记录器实例。</para></param>
    /// <returns>A <see cref="SparseFile"/> parsed from the file. <para>从文件解析的 SparseFile。</para></returns>
    public static SparseFile FromImageFile(string filePath, bool validateCrc = false, bool verbose = false, ISparseLogger? logger = null)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);
        return FromStreamInternal(stream, filePath, validateCrc, verbose, logger);
    }

    /// <summary>
    /// Reads a sparse image from a stream with full parsing and optional CRC validation.
    /// <para>从流中读取稀疏镜像，执行完整解析和可选的 CRC 验证。</para>
    /// </summary>
    /// <param name="stream">The source stream. <para>源流。</para></param>
    /// <param name="filePath">Optional file path for reference. <para>可选的文件路径，用于引用。</para></param>
    /// <param name="validateCrc">If true, validate CRC checksums. <para>如果为 true，验证 CRC 校验和。</para></param>
    /// <param name="verbose">Enable verbose logging. <para>启用详细日志。</para></param>
    /// <param name="logger">Optional logger instance. <para>可选的日志记录器实例。</para></param>
    /// <returns>A parsed <see cref="SparseFile"/>. <para>解析后的 SparseFile。</para></returns>
    internal static SparseFile FromStreamInternal(Stream stream, string? filePath, bool validateCrc, bool verbose, ISparseLogger? logger)
    {
        var sparseFile = new SparseFile { Verbose = verbose, Logger = logger };
        ISparseLogger activeLogger = logger ?? SparseLogger.Instance;

        Span<byte> headerData = stackalloc byte[SparseFormat.SparseHeaderSize];
        stream.ReadExactly(headerData);

        sparseFile.Header = SparseHeader.FromBytes(headerData);

        if (verbose)
        {
            activeLogger.LogInformation($"Parsing Sparse image header: BlockSize={sparseFile.Header.BlockSize}, TotalBlocks={sparseFile.Header.TotalBlocks}, TotalChunks={sparseFile.Header.TotalChunks}");
        }

        if (!sparseFile.Header.IsValid())
        {
            throw new InvalidDataException("Invalid sparse header");
        }

        if (sparseFile.Header.FileHeaderSize > SparseFormat.SparseHeaderSize)
        {
            if (!stream.CanSeek)
            {
                throw new InvalidDataException("Stream must be seekable when sparse file header is extended.");
            }

            stream.Seek(sparseFile.Header.FileHeaderSize - SparseFormat.SparseHeaderSize, SeekOrigin.Current);
        }

        uint? checksum = validateCrc ? Crc32.Begin() : null;
        byte[]? buffer = validateCrc ? ArrayPool<byte>.Shared.Rent(1024 * 1024) : null;
        try
        {
            uint currentBlock = 0;

            Span<byte> chunkHeaderData = stackalloc byte[SparseFormat.ChunkHeaderSize];
            Span<byte> buffer4 = stackalloc byte[4];
            for (uint i = 0; i < sparseFile.Header.TotalChunks; i++)
            {
                stream.ReadExactly(chunkHeaderData);

                var chunkHeader = ChunkHeader.FromBytes(chunkHeaderData);

                if (verbose)
                {
                    activeLogger.LogInformation($"Chunk #{i}: Type=0x{chunkHeader.ChunkType:X4}, Size={chunkHeader.ChunkSize} blocks, Total Size={chunkHeader.TotalSize}");
                }

                if (sparseFile.Header.ChunkHeaderSize > SparseFormat.ChunkHeaderSize)
                {
                    if (!stream.CanSeek)
                    {
                        throw new InvalidDataException("Stream must be seekable when chunk headers are extended.");
                    }

                    stream.Seek(sparseFile.Header.ChunkHeaderSize - SparseFormat.ChunkHeaderSize, SeekOrigin.Current);
                }

                var chunk = new SparseChunk(chunkHeader) { StartBlock = currentBlock };

                if (!chunkHeader.IsValid(sparseFile.Header.ChunkHeaderSize, sparseFile.Header.BlockSize))
                {
                    throw new InvalidDataException($"Invalid chunk header for chunk {i}: Type 0x{chunkHeader.ChunkType:X4}");
                }

                if (chunkHeader.TotalSize < sparseFile.Header.ChunkHeaderSize)
                {
                    throw new InvalidDataException($"Total size ({chunkHeader.TotalSize}) for chunk {i} is smaller than chunk header size ({sparseFile.Header.ChunkHeaderSize})");
                }

                var dataSize = (long)chunkHeader.TotalSize - sparseFile.Header.ChunkHeaderSize;
                var expectedRawSize = (long)chunkHeader.ChunkSize * sparseFile.Header.BlockSize;

                switch (chunkHeader.ChunkType)
                {
                    case (ushort)ChunkType.Raw:
                        if (dataSize != expectedRawSize)
                        {
                            throw new InvalidDataException($"Total size ({chunkHeader.TotalSize}) for RAW chunk {i} does not match expected data size ({expectedRawSize})");
                        }

                        if (filePath != null)
                        {
                            if (validateCrc && buffer != null && checksum.HasValue)
                            {
                                var dataOffset = stream.Position;
                                var remaining = dataSize;
                                while (remaining > 0)
                                {
                                    var toRead = (int)Math.Min(buffer.Length, remaining);
                                    stream.ReadExactly(buffer.AsSpan(0, toRead));
                                    checksum = Crc32.Update(checksum.Value, buffer.AsSpan(0, toRead));
                                    remaining -= toRead;
                                }
                                chunk.DataProvider = new FileDataProvider(filePath, dataOffset, dataSize);
                            }
                            else
                            {
                                chunk.DataProvider = new FileDataProvider(filePath, stream.Position, dataSize);
                                stream.Seek(dataSize, SeekOrigin.Current);
                            }
                        }
                        else
                        {
                            if (dataSize > int.MaxValue)
                            {
                                throw new NotSupportedException($"Raw data for chunk {i} is too large ({dataSize} bytes), exceeding memory buffer limits.");
                            }
                            var rawData = new byte[dataSize];
                            stream.ReadExactly(rawData);
                            if (validateCrc && checksum.HasValue)
                            {
                                checksum = Crc32.Update(checksum.Value, rawData);
                            }
                            chunk.DataProvider = new MemoryDataProvider(rawData);
                        }
                        break;

                    case (ushort)ChunkType.Fill:
                        if (dataSize != 4)
                        {
                            throw new InvalidDataException($"Data size ({dataSize}) for FILL chunk {i} must be 4");
                        }

                        stream.ReadExactly(buffer4);

                        chunk.FillValue = BinaryPrimitives.ReadUInt32LittleEndian(buffer4);

                        if (validateCrc && checksum.HasValue)
                        {
                            checksum = Crc32.UpdateRepeated(checksum.Value, chunk.FillValue, expectedRawSize);
                        }

                        break;

                    case (ushort)ChunkType.DontCare:
                        if (dataSize != 0)
                        {
                            throw new InvalidDataException($"Data size ({dataSize}) for DONT_CARE chunk {i} must be 0");
                        }
                        if (validateCrc && checksum.HasValue)
                        {
                            checksum = Crc32.UpdateZero(checksum.Value, expectedRawSize);
                        }
                        break;

                    case (ushort)ChunkType.Crc32:
                        if (dataSize != 4)
                        {
                            throw new InvalidDataException($"Data size ({dataSize}) for CRC32 chunk {i} must be 4");
                        }
                        stream.ReadExactly(buffer4);
                        var fileCrc = BinaryPrimitives.ReadUInt32LittleEndian(buffer4);
                        if (validateCrc && checksum.HasValue && fileCrc != Crc32.Finish(checksum.Value))
                        {
                            throw new InvalidDataException($"CRC32 checksum mismatch: file has 0x{fileCrc:X8}, computed 0x{Crc32.Finish(checksum.Value):X8}");
                        }
                        break;

                    default:
                        throw new InvalidDataException($"Unknown chunk type for chunk {i}: 0x{chunkHeader.ChunkType:X4}");
                }

                if (chunkHeader.ChunkType != (ushort)ChunkType.Crc32)
                {
                    sparseFile.AddChunkRaw(chunk);
                    currentBlock += chunkHeader.ChunkSize;
                }
            }

            if (sparseFile.Chunks.Count > 0)
            {
                var last = sparseFile.Chunks[sparseFile.Chunks.Count - 1];
                if (last.Header.ChunkType == (ushort)ChunkType.DontCare && (last.StartBlock + last.Header.ChunkSize) == currentBlock && currentBlock == sparseFile.Header.TotalBlocks)
                {
                    sparseFile.RemoveLastChunk();
                    sparseFile.Header = sparseFile.Header with { TotalChunks = sparseFile.Header.TotalChunks - 1 };
                }
            }

            if (verbose)
            {
                activeLogger.LogInformation($"Sparse image parsing completed: {sparseFile.Chunks.Count} chunks, {currentBlock} blocks total");
            }

            if (sparseFile.Header.TotalBlocks != currentBlock)
            {
                throw new InvalidDataException($"Block count mismatch: Sparse header expects {sparseFile.Header.TotalBlocks} blocks, but parsed {currentBlock}");
            }

            return sparseFile;
        }
        finally
        {
            if (buffer != null)
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }

}
