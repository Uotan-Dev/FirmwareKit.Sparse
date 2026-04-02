namespace FirmwareKit.Sparse.IO;

using FirmwareKit.Sparse.Core;
using FirmwareKit.Sparse.DataProviders;
using FirmwareKit.Sparse.Models;
using FirmwareKit.Sparse.Utils;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Provides methods for reading and importing sparse image data.
/// </summary>
public static class SparseReader
{
    /// <summary>
    /// Peeks at the sparse header of a file without reading the entire content.
    /// </summary>
    /// <param name="filePath">The path to the sparse image file.</param>
    /// <returns>A <see cref="SparseHeader"/> containing the metadata of the sparse image.</returns>
    public static SparseHeader PeekHeader(string filePath)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        Span<byte> headerData = stackalloc byte[SparseFormat.SparseHeaderSize];
        stream.ReadExactly(headerData);
        return SparseHeader.FromBytes(headerData);
    }

    /// <summary>
    /// Load a <see cref="SparseFile"/> from the provided <see cref="Stream"/>.
    /// </summary>
    /// <param name="stream">Input stream containing the sparse image data.</param>
    /// <param name="validateCrc">If true, validate CRC checksums while parsing (bool).</param>
    /// <param name="verbose">If true, enable verbose logging (bool).</param>
    /// <param name="logger">Optional logger instance for diagnostic messages.</param>
    /// <returns>A <see cref="SparseFile"/> parsed from the stream.</returns>
    public static SparseFile FromStream(Stream stream, bool validateCrc = false, bool verbose = false, ISparseLogger? logger = null)
    {
        return FromStreamInternal(stream, null, validateCrc, verbose, logger);
    }

    /// <summary>
    /// Load a <see cref="SparseFile"/> from a byte array that contains the sparse image.
    /// </summary>
    /// <param name="buffer">Byte array containing the sparse image data.</param>
    /// <param name="validateCrc">If true, validate CRC checksums while parsing (bool).</param>
    /// <param name="verbose">If true, enable verbose logging (bool).</param>
    /// <param name="logger">Optional logger instance for diagnostic messages.</param>
    /// <returns>A <see cref="SparseFile"/> parsed from the buffer.</returns>
    public static SparseFile FromBuffer(byte[] buffer, bool validateCrc = false, bool verbose = false, ISparseLogger? logger = null)
    {
        using var ms = new MemoryStream(buffer);
        return FromStream(ms, validateCrc, verbose, logger);
    }

    /// <summary>
    /// Load a <see cref="SparseFile"/> directly from an image file on disk.
    /// </summary>
    /// <param name="filePath">Path to the image file on disk (string).</param>
    /// <param name="validateCrc">If true, validate CRC checksums while parsing (bool).</param>
    /// <param name="verbose">If true, enable verbose logging (bool).</param>
    /// <param name="logger">Optional logger instance for diagnostic messages.</param>
    /// <returns>A <see cref="SparseFile"/> parsed from the file.</returns>
    public static SparseFile FromImageFile(string filePath, bool validateCrc = false, bool verbose = false, ISparseLogger? logger = null)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);
        return FromStreamInternal(stream, filePath, validateCrc, verbose, logger);
    }

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

                if (!chunkHeader.IsValid())
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
                        // Use ReadExactly to ensure we read all 4 bytes (handles partial reads)
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

            // Trim trailing DontCare chunk that only exists to pad to header.TotalBlocks.
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

    /// <summary>
    /// Asynchronously load a <see cref="SparseFile"/> from the provided stream.
    /// </summary>
    /// <param name="stream">Input stream containing the sparse image data.</param>
    /// <param name="validateCrc">If true, validate CRC checksums while parsing (bool).</param>
    /// <param name="verbose">If true, enable verbose logging (bool).</param>
    /// <param name="logger">Optional logger instance for diagnostic messages.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the asynchronous operation.</param>
    /// <returns>A task that resolves to a parsed <see cref="SparseFile"/>.</returns>
    public static Task<SparseFile> FromStreamAsync(Stream stream, bool validateCrc = false, bool verbose = false, ISparseLogger? logger = null, CancellationToken cancellationToken = default)
    {
        return FromStreamInternalAsync(stream, null, validateCrc, verbose, logger, cancellationToken);
    }

    /// <summary>
    /// Asynchronously load a <see cref="SparseFile"/> from a byte array that contains the sparse image.
    /// </summary>
    /// <param name="buffer">Byte array containing the sparse image data.</param>
    /// <param name="validateCrc">If true, validate CRC checksums while parsing (bool).</param>
    /// <param name="verbose">If true, enable verbose logging (bool).</param>
    /// <param name="logger">Optional logger instance for diagnostic messages.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the asynchronous operation.</param>
    /// <returns>A task that resolves to a parsed <see cref="SparseFile"/>.</returns>
    public static async Task<SparseFile> FromBufferAsync(byte[] buffer, bool validateCrc = false, bool verbose = false, ISparseLogger? logger = null, CancellationToken cancellationToken = default)
    {
        using var ms = new MemoryStream(buffer);
        return await FromStreamAsync(ms, validateCrc, verbose, logger, cancellationToken);
    }

    /// <summary>
    /// Asynchronously load a <see cref="SparseFile"/> from an image file on disk.
    /// </summary>
    /// <param name="filePath">Path to the image file on disk (string).</param>
    /// <param name="validateCrc">If true, validate CRC checksums while parsing (bool).</param>
    /// <param name="verbose">If true, enable verbose logging (bool).</param>
    /// <param name="logger">Optional logger instance for diagnostic messages.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the asynchronous operation.</param>
    /// <returns>A task that resolves to a parsed <see cref="SparseFile"/>.</returns>
    public static async Task<SparseFile> FromImageFileAsync(string filePath, bool validateCrc = false, bool verbose = false, ISparseLogger? logger = null, CancellationToken cancellationToken = default)
    {
#if NET6_0_OR_GREATER
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
#else
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
#endif
        return await FromStreamInternalAsync(stream, filePath, validateCrc, verbose, logger, cancellationToken);
    }

    internal static async Task<SparseFile> FromStreamInternalAsync(Stream stream, string? filePath, bool validateCrc, bool verbose, ISparseLogger? logger, CancellationToken cancellationToken)
    {
        var sparseFile = new SparseFile { Verbose = verbose, Logger = logger };
        ISparseLogger activeLogger = logger ?? SparseLogger.Instance;

        var headerData = new byte[SparseFormat.SparseHeaderSize];
        await ReadExactlyAsync(stream, headerData, 0, SparseFormat.SparseHeaderSize, cancellationToken);

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

        var checksum = validateCrc ? Crc32.Begin() : (uint?)null;
        var buffer = validateCrc ? ArrayPool<byte>.Shared.Rent(1024 * 1024) : null;

        try
        {
            uint currentBlock = 0;

            var chunkHeaderData = new byte[SparseFormat.ChunkHeaderSize];
            var buffer4 = new byte[4];
            for (uint i = 0; i < sparseFile.Header.TotalChunks; i++)
            {
                await ReadExactlyAsync(stream, chunkHeaderData, 0, SparseFormat.ChunkHeaderSize, cancellationToken);

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

                if (!chunkHeader.IsValid())
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
                                    await ReadExactlyAsync(stream, buffer, 0, toRead, cancellationToken);
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
                            await ReadExactlyAsync(stream, rawData, 0, (int)dataSize, cancellationToken);
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

                        await ReadExactlyAsync(stream, buffer4, 0, 4, cancellationToken);

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
                        var crcFileData = new byte[4];
                        await ReadExactlyAsync(stream, crcFileData, 0, 4, cancellationToken);
                        var fileCrc = BinaryPrimitives.ReadUInt32LittleEndian(crcFileData);
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

    private static async Task ReadExactlyAsync(Stream stream, byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
#if NET7_0_OR_GREATER
        await stream.ReadExactlyAsync(buffer.AsMemory(offset, count), cancellationToken);
#else
        var totalRead = 0;
        while (totalRead < count)
        {
            var read = await stream.ReadAsync(buffer, offset + totalRead, count - totalRead, cancellationToken);
            if (read == 0) throw new EndOfStreamException();
            totalRead += read;
        }
#endif
    }

    /// <summary>
    /// Detect whether the given file is a sparse image or a raw image and import accordingly.
    /// </summary>
    /// <param name="filePath">Path to the input file on disk (string).</param>
    /// <param name="validateCrc">If true, validate CRC checksums while parsing (bool).</param>
    /// <param name="verbose">If true, enable verbose logging (bool).</param>
    /// <param name="logger">Optional logger instance for diagnostic messages.</param>
    /// <returns>A <see cref="SparseFile"/> representing the imported image.</returns>
    public static SparseFile ImportAuto(string filePath, bool validateCrc = false, bool verbose = false, ISparseLogger? logger = null)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return ImportAuto(stream, validateCrc, verbose, logger, filePath);
    }

    /// <summary>
    /// Automatically import image data from a stream, detecting whether it is sparse or raw.
    /// </summary>
    /// <param name="stream">Input stream to inspect and import.</param>
    /// <param name="validateCrc">If true, validate CRC checksums while parsing (bool).</param>
    /// <param name="verbose">If true, enable verbose logging (bool).</param>
    /// <param name="logger">Optional logger instance for diagnostic messages.</param>
    /// <param name="filePath">Optional original file path (used when creating file-backed providers).</param>
    /// <returns>A <see cref="SparseFile"/> representing the imported image.</returns>
    public static SparseFile ImportAuto(Stream stream, bool validateCrc = false, bool verbose = false, ISparseLogger? logger = null, string? filePath = null)
    {
        var magicData = new byte[4];
        var pos = stream.CanSeek ? stream.Position : 0;
        var read = stream.Read(magicData, 0, 4);
        if (read == 4)
        {
            var magic = BinaryPrimitives.ReadUInt32LittleEndian(magicData);
            if (stream.CanSeek)
            {
                stream.Seek(pos, SeekOrigin.Begin);
            }

            if (magic == SparseFormat.SparseHeaderMagic)
            {
                var inputStream = stream.CanSeek ? stream : new PrefixReadStream(stream, magicData, read);
                return FromStreamInternal(inputStream, filePath, validateCrc, verbose, logger);
            }
        }

        if (filePath != null)
        {
            return FromRawFile(filePath, 4096, verbose, logger);
        }

        // Treat as raw stream
        Stream rawInput;
        if (stream.CanSeek)
        {
            stream.Seek(pos, SeekOrigin.Begin);
            rawInput = stream;
        }
        else
        {
            rawInput = new PrefixReadStream(stream, magicData, read);
        }

        long rawLength;
        if (rawInput.CanSeek)
        {
            rawLength = rawInput.Length - rawInput.Position;
        }
        else
        {
            using var temp = new MemoryStream();
            rawInput.CopyTo(temp);
            var rawBytes = temp.ToArray();
            var rawFromBytes = new SparseFile(4096, rawBytes.Length, verbose) { Logger = logger };
            rawFromBytes.AddRawChunk(rawBytes);
            return rawFromBytes;
        }

        var rawFile = new SparseFile(4096, rawLength, verbose) { Logger = logger };
        ReadFromStream(rawFile, rawInput, SparseReadMode.Normal);
        return rawFile;
    }

    private sealed class PrefixReadStream : Stream
    {
        private readonly Stream _inner;
        private readonly byte[] _prefix;
        private int _prefixOffset;

        public PrefixReadStream(Stream inner, byte[] prefix, int prefixLength)
        {
            _inner = inner;
            _prefix = new byte[prefixLength];
            Array.Copy(prefix, _prefix, prefixLength);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_prefixOffset < _prefix.Length)
            {
                var toCopy = Math.Min(count, _prefix.Length - _prefixOffset);
                Array.Copy(_prefix, _prefixOffset, buffer, offset, toCopy);
                _prefixOffset += toCopy;
                return toCopy;
            }

            return _inner.Read(buffer, offset, count);
        }

#if NET6_0_OR_GREATER || NETSTANDARD2_1_OR_GREATER
        public override int Read(Span<byte> buffer)
        {
            if (_prefixOffset < _prefix.Length)
            {
                var toCopy = Math.Min(buffer.Length, _prefix.Length - _prefixOffset);
                _prefix.AsSpan(_prefixOffset, toCopy).CopyTo(buffer);
                _prefixOffset += toCopy;
                return toCopy;
            }

            return _inner.Read(buffer);
        }
#endif

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>
    /// Create a <see cref="SparseFile"/> by importing a raw binary file (no sparse metadata).
    /// </summary>
    /// <param name="filePath">Path to the raw binary file (string).</param>
    /// <param name="blockSize">Block size to use for conversion, in bytes (uint).</param>
    /// <param name="verbose">If true, enable verbose logging (bool).</param>
    /// <param name="logger">Optional logger instance for diagnostic messages.</param>
    /// <returns>A <see cref="SparseFile"/> constructed from the raw file.</returns>
    public static SparseFile FromRawFile(string filePath, uint blockSize = 4096, bool verbose = false, ISparseLogger? logger = null)
    {
        var fi = new FileInfo(filePath);
        var sparseFile = new SparseFile(blockSize, (long)fi.Length, verbose) { Logger = logger };
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);
        var chunkHeader = new ChunkHeader
        {
            ChunkType = (ushort)ChunkType.Raw,
            ChunkSize = (uint)((fi.Length + blockSize - 1) / blockSize),
            TotalSize = (uint)(sparseFile.Header.ChunkHeaderSize + (((fi.Length + blockSize - 1) / blockSize) * blockSize))
        };
        sparseFile.AddChunkRaw(new SparseChunk(chunkHeader)
        {
            DataProvider = new FileDataProvider(filePath, 0, fi.Length)
        });
        return sparseFile;
    }

    /// <summary>
    /// Asynchronously create a <see cref="SparseFile"/> by importing a raw binary file.
    /// </summary>
    /// <param name="filePath">Path to the raw binary file (string).</param>
    /// <param name="blockSize">Block size to use for conversion, in bytes (uint).</param>
    /// <param name="verbose">If true, enable verbose logging (bool).</param>
    /// <param name="logger">Optional logger instance for diagnostic messages.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>A task that resolves to a <see cref="SparseFile"/> constructed from the raw file.</returns>
    public static Task<SparseFile> FromRawFileAsync(string filePath, uint blockSize = 4096, bool verbose = false, ISparseLogger? logger = null, CancellationToken cancellationToken = default)
    {
        var fi = new FileInfo(filePath);
        var sparseFile = new SparseFile(blockSize, (long)fi.Length, verbose) { Logger = logger };
        var chunkHeader = new ChunkHeader
        {
            ChunkType = (ushort)ChunkType.Raw,
            ChunkSize = (uint)((fi.Length + blockSize - 1) / blockSize),
            TotalSize = (uint)(sparseFile.Header.ChunkHeaderSize + (((fi.Length + blockSize - 1) / blockSize) * blockSize))
        };
        sparseFile.AddChunkRaw(new SparseChunk(chunkHeader)
        {
            DataProvider = new FileDataProvider(filePath, 0, fi.Length)
        });
        return Task.FromResult(sparseFile);
    }

    /// <summary>
    /// Read raw data from a stream and incorporate it into the provided <paramref name="sparseFile"/>
    /// by converting consecutive blocks into RAW, FILL or DONT_CARE chunks according to <paramref name="mode"/>.
    /// </summary>
    /// <param name="sparseFile">Target <see cref="SparseFile"/> to populate.</param>
    /// <param name="stream">Input stream to read raw data from.</param>
    /// <param name="mode">The sparsification mode to apply (<see cref="SparseReadMode"/>).</param>
    /// <param name="validateCrc">If true, update CRC while reading (bool).</param>
    public static void ReadFromStream(SparseFile sparseFile, Stream stream, SparseReadMode mode, bool validateCrc = false)
    {
        if (mode == SparseReadMode.Sparse)
        {
            var headerData = new byte[SparseFormat.SparseHeaderSize];
            if (stream.Read(headerData, 0, headerData.Length) != headerData.Length)
            {
                throw new InvalidDataException("Failed to read sparse header");
            }

            var importedHeader = SparseHeader.FromBytes(headerData);
            if (!importedHeader.IsValid())
            {
                throw new InvalidDataException("Invalid sparse header");
            }

            if (sparseFile.Header.BlockSize != importedHeader.BlockSize)
            {
                throw new ArgumentException("Imported sparse file block size does not match the current file");
            }

            if (sparseFile.Verbose)
            {
                SparseLogger.LogInformation($"ReadFromStream (Sparse mode): BlockSize={importedHeader.BlockSize}, TotalBlocks={importedHeader.TotalBlocks}, TotalChunks={importedHeader.TotalChunks}");
            }

            stream.Seek(importedHeader.FileHeaderSize - SparseFormat.SparseHeaderSize, SeekOrigin.Current);

            var checksum = Crc32.Begin();
            var currentBlockStart = sparseFile.CurrentBlock;

            for (uint i = 0; i < importedHeader.TotalChunks; i++)
            {
                var chunkHeaderData = new byte[SparseFormat.ChunkHeaderSize];
                stream.ReadExactly(chunkHeaderData, 0, chunkHeaderData.Length);
                var chunkHeader = ChunkHeader.FromBytes(chunkHeaderData);

                if (sparseFile.Verbose)
                {
                    SparseLogger.LogInformation($"Imported Chunk #{i}: Type=0x{chunkHeader.ChunkType:X4}, Size={chunkHeader.ChunkSize} blocks");
                }

                stream.Seek(importedHeader.ChunkHeaderSize - SparseFormat.ChunkHeaderSize, SeekOrigin.Current);

                var dataSize = (long)chunkHeader.TotalSize - importedHeader.ChunkHeaderSize;
                var expectedRawSize = (long)chunkHeader.ChunkSize * sparseFile.Header.BlockSize;

                var chunk = new SparseChunk(chunkHeader) { StartBlock = currentBlockStart };

                switch (chunkHeader.ChunkType)
                {
                    case (ushort)ChunkType.Raw:
                        if (dataSize != expectedRawSize)
                        {
                            throw new InvalidDataException($"Total size ({chunkHeader.TotalSize}) for RAW chunk {i} does not match expected data size ({expectedRawSize})");
                        }
                        var rawData = new byte[dataSize];
                        stream.ReadExactly(rawData, 0, (int)dataSize);
                        if (validateCrc)
                        {
                            checksum = Crc32.Update(checksum, rawData);
                        }
                        chunk.DataProvider = new MemoryDataProvider(rawData);
                        sparseFile.AddChunkRaw(chunk);
                        currentBlockStart += chunkHeader.ChunkSize;
                        break;
                    case (ushort)ChunkType.Fill:
                        if (dataSize != 4)
                        {
                            throw new InvalidDataException($"Data size ({dataSize}) for FILL chunk {i} must be 4 bytes");
                        }
                        var fillData = new byte[4];
                        stream.ReadExactly(fillData, 0, 4);
                        var fillValue = BinaryPrimitives.ReadUInt32LittleEndian(fillData);
                        if (validateCrc)
                        {
                            checksum = Crc32.UpdateRepeated(checksum, fillValue, expectedRawSize);
                        }
                        chunk.FillValue = fillValue;
                        sparseFile.AddChunkRaw(chunk);
                        currentBlockStart += chunkHeader.ChunkSize;
                        break;
                    case (ushort)ChunkType.DontCare:
                        if (dataSize != 0)
                        {
                            throw new InvalidDataException($"Data size ({dataSize}) for DONT_CARE chunk {i} must be 0");
                        }
                        if (validateCrc)
                        {
                            checksum = Crc32.UpdateZero(checksum, expectedRawSize);
                        }
                        sparseFile.AddChunkRaw(chunk);
                        currentBlockStart += chunkHeader.ChunkSize;
                        break;
                    case (ushort)ChunkType.Crc32:
                        if (dataSize != 4)
                        {
                            throw new InvalidDataException($"Data size ({dataSize}) for CRC32 chunk {i} must be 4");
                        }
                        var crcFileData = new byte[4];
                        stream.ReadExactly(crcFileData, 0, 4);
                        if (validateCrc)
                        {
                            var fileCrc = BinaryPrimitives.ReadUInt32LittleEndian(crcFileData);
                            if (fileCrc != Crc32.Finish(checksum))
                            {
                                throw new InvalidDataException("CRC32 validation failed");
                            }
                        }
                        break;
                    default:
                        break;
                }
            }
            return;
        }

        // Normal or Hole mode: scan stream and sparsify
        var blockSize = sparseFile.Header.BlockSize;
        var bufferScan = new byte[blockSize];
        long currentPos = 0;
        var streamLen = stream.Length;
        long rawStart = -1;

        while (currentPos < streamLen)
        {
            if (stream.CanSeek)
            {
                stream.Position = currentPos;
            }
            var bytesRead = stream.Read(bufferScan, 0, (int)Math.Min(blockSize, streamLen - currentPos));
            if (bytesRead == 0)
            {
                break;
            }

            uint fillValue = 0;
            var isZero = IsZeroBlock(bufferScan, bytesRead);
            var isFill = !isZero && bytesRead == blockSize && IsFillBlock(bufferScan, out fillValue);

            if (isZero || isFill)
            {
                if (rawStart != -1)
                {
                    sparseFile.AddStreamChunk(stream, rawStart, (uint)(currentPos - rawStart));
                    rawStart = -1;
                }

                if (isZero)
                {
                    var zeroStart = currentPos;
                    currentPos += bytesRead;
                    while (currentPos < streamLen)
                    {
                        var innerRead = stream.Read(bufferScan, 0, (int)Math.Min(blockSize, streamLen - currentPos));
                        if (innerRead > 0 && IsZeroBlock(bufferScan, innerRead))
                        {
                            currentPos += innerRead;
                        }
                        else
                        {
                            break;
                        }
                    }
                    if (mode == SparseReadMode.Hole)
                    {
                        sparseFile.AddDontCareChunk(currentPos - zeroStart);
                    }
                    else
                    {
                        sparseFile.AddFillChunk(0, currentPos - zeroStart);
                    }
                }
                else
                {
                    var fillStart = currentPos;
                    var currentFillValue = fillValue;
                    currentPos += bytesRead;
                    while (currentPos < streamLen)
                    {
                        var innerRead = stream.Read(bufferScan, 0, (int)Math.Min(blockSize, streamLen - currentPos));
                        if (innerRead == blockSize && IsFillBlock(bufferScan, out var innerFill) && innerFill == currentFillValue)
                        {
                            currentPos += innerRead;
                        }
                        else
                        {
                            break;
                        }
                    }
                    sparseFile.AddFillChunk(currentFillValue, currentPos - fillStart);
                }
            }
            else
            {
                if (rawStart == -1)
                {
                    rawStart = currentPos;
                }
                currentPos += bytesRead;
            }
        }

        if (rawStart != -1)
        {
            sparseFile.AddStreamChunk(stream, rawStart, (uint)(streamLen - rawStart));
        }
    }

    private static bool IsZeroBlock(byte[] buffer, int length)
    {
        if (length == 0) return true;
        Span<byte> span = buffer.AsSpan(0, length);
        Span<ulong> ulongSpan = MemoryMarshal.Cast<byte, ulong>(span);
        foreach (var v in ulongSpan) if (v != 0) return false;
        for (var i = ulongSpan.Length * 8; i < length; i++) if (buffer[i] != 0) return false;
        return true;
    }

    private static bool IsFillBlock(byte[] buffer, out uint fillValue)
    {
        fillValue = 0;
        if (buffer.Length < 4) return false;
        var pattern = BinaryPrimitives.ReadUInt32LittleEndian(buffer);
        Span<uint> uintSpan = MemoryMarshal.Cast<byte, uint>(buffer.AsSpan());
        foreach (var v in uintSpan) if (v != pattern) return false;
        for (var i = uintSpan.Length * 4; i < buffer.Length; i++) if (buffer[i] != (byte)(pattern >> (i % 4 * 8))) return false;
        fillValue = pattern;
        return true;
    }
}
