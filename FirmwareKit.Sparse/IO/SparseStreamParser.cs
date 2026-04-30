using FirmwareKit.Sparse.Models;
using System.Buffers;

namespace FirmwareKit.Sparse.IO;

/// <summary>
/// Provides streaming parsing of sparse files for memory-efficient processing.
/// Optimized for 32-bit AOT environments handling large files (up to 16GB).
/// </summary>
public class SparseStreamParser : IDisposable
{
    private readonly Stream stream;
    private readonly bool leaveOpen;
    private readonly SparseHeader header;
    private long position;
    private bool disposed;

    /// <summary>
    /// Gets the sparse header.
    /// </summary>
    public SparseHeader Header => header;

    /// <summary>
    /// Initialize a new SparseStreamParser.
    /// </summary>
    public SparseStreamParser(Stream stream, bool leaveOpen = false)
    {
        this.stream = stream;
        this.leaveOpen = leaveOpen;
        this.position = 0;

        // Read header first
        header = ReadHeader();
        position = header.FileHeaderSize;
    }

    private SparseHeader ReadHeader()
    {
        var buffer = ArrayPool<byte>.Shared.Rent(512);
        try
        {
            stream.Position = 0;
            int read = stream.Read(buffer, 0, SparseFormat.SparseHeaderSize);
            if (read < SparseFormat.SparseHeaderSize)
                throw new InvalidDataException("Invalid sparse header");

            return SparseHeader.FromBytes(buffer);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Enumerate chunks lazily.
    /// </summary>
    public IEnumerable<SparseChunk> EnumerateChunks()
    {
        stream.Position = position;

        for (uint i = 0; i < header.TotalChunks; i++)
        {
            var chunk = ReadNextChunk();
            if (chunk == null)
                break;

            yield return chunk;
        }
    }

    /// <summary>
    /// Read the next chunk from stream.
    /// </summary>
    public SparseChunk? ReadNextChunk()
    {
        if (stream.Position >= stream.Length)
            return null;

        var headerBuffer = ArrayPool<byte>.Shared.Rent(header.ChunkHeaderSize);
        try
        {
            int read = stream.Read(headerBuffer, 0, header.ChunkHeaderSize);
            if (read < header.ChunkHeaderSize)
                return null;

            var chunkHeader = ChunkHeader.FromBytes(headerBuffer);

            // Skip CRC if present (we don't use it here)
            if ((chunkHeader.ChunkType & 0x8000) != 0)
            {
                stream.Seek(4, SeekOrigin.Current);
            }

            var chunk = new SparseChunk(chunkHeader);

            // Read chunk data if needed
            switch ((ChunkType)chunkHeader.ChunkType)
            {
                case ChunkType.Raw:
                    long dataSize = chunkHeader.TotalSize - header.ChunkHeaderSize;
                    var dataBuffer = new byte[dataSize];
                    stream.Read(dataBuffer, 0, (int)dataSize);
                    chunk.DataProvider = new MemoryDataProvider(dataBuffer, 0, (int)dataSize);
                    break;

                case ChunkType.Fill:
                    Span<byte> fillBuffer = stackalloc byte[4];
                    stream.Read(fillBuffer);
                    chunk.FillValue = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(fillBuffer);
                    break;

                case ChunkType.DontCare:
                    break;
            }

            position = stream.Position;
            return chunk;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(headerBuffer);
        }
    }

    /// <summary>
    /// Dispose resources.
    /// </summary>
    public void Dispose()
    {
        if (!disposed)
        {
            if (!leaveOpen)
                stream.Dispose();
            disposed = true;
        }
    }
}