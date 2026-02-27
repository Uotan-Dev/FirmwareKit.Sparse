namespace FirmwareKit.Sparse.Models;

/// <summary>
/// Represents the type of a sparse chunk.
/// </summary>
public enum ChunkType : ushort
{
    /// <summary>Raw data.</summary>
    Raw = 0xCAC1,
    /// <summary>Fill data.</summary>
    Fill = 0xCAC2,
    /// <summary>"Don't care" data (skip).</summary>
    DontCare = 0xCAC3,
    /// <summary>CRC32 checksum.</summary>
    Crc32 = 0xCAC4
}

/// <summary>
/// Defines constants for the sparse file format.
/// </summary>
public static class SparseFormat
{
    /// <summary>Magic number for the sparse file header.</summary>
    public const uint SparseHeaderMagic = 0xed26ff3a;

    /// <summary>Size of the sparse file header in bytes.</summary>
    public const ushort SparseHeaderSize = 28;
    /// <summary>Size of the chunk header in bytes.</summary>
    public const ushort ChunkHeaderSize = 12;

    /// <summary>Maximum chunk payload size.</summary>
    public const uint MaxChunkDataSize = 64 * 1024 * 1024;
}

/// <summary>
/// Represents the structure of a sparse file header.
/// </summary>
public readonly struct SparseHeader
{
    /// <summary>The magic number.</summary>
    public uint Magic { get; init; }
    /// <summary>The major version number.</summary>
    public ushort MajorVersion { get; init; }
    /// <summary>The minor version number.</summary>
    public ushort MinorVersion { get; init; }
    /// <summary>The size of the file header.</summary>
    public ushort FileHeaderSize { get; init; }
    /// <summary>The size of the chunk header.</summary>
    public ushort ChunkHeaderSize { get; init; }
    /// <summary>The block size in bytes.</summary>
    public uint BlockSize { get; init; }
    /// <summary>The total number of blocks.</summary>
    public uint TotalBlocks { get; init; }
    /// <summary>The total number of chunks.</summary>
    public uint TotalChunks { get; init; }
    /// <summary>The image checksum.</summary>
    public uint ImageChecksum { get; init; }

    /// <summary>
    /// Parses a <see cref="SparseHeader"/> from a byte sequence.
    /// </summary>
    /// <param name="data">A read-only byte sequence containing the header data.</param>
    /// <returns>A parsed instance of <see cref="SparseHeader"/>.</returns>
    public static SparseHeader FromBytes(ReadOnlySpan<byte> data)
    {
        return data.Length < SparseFormat.SparseHeaderSize
            ? throw new ArgumentException("Data length is insufficient to build SparseHeader")
            : new SparseHeader
            {
                Magic = BinaryPrimitives.ReadUInt32LittleEndian(data),
                MajorVersion = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(4)),
                MinorVersion = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(6)),
                FileHeaderSize = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(8)),
                ChunkHeaderSize = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(10)),
                BlockSize = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(12)),
                TotalBlocks = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(16)),
                TotalChunks = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(20)),
                ImageChecksum = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(24))
            };
    }

    /// <summary>
    /// Writes the <see cref="SparseHeader"/> to a byte sequence.
    /// </summary>
    /// <param name="span">The target byte sequence.</param>
    public void WriteTo(Span<byte> span)
    {
        if (span.Length < SparseFormat.SparseHeaderSize)
        {
            throw new ArgumentException("Span length is insufficient to write SparseHeader");
        }

        BinaryPrimitives.WriteUInt32LittleEndian(span, Magic);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(4), MajorVersion);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(6), MinorVersion);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(8), FileHeaderSize);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(10), ChunkHeaderSize);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(12), BlockSize);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(16), TotalBlocks);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(20), TotalChunks);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(24), ImageChecksum);
    }

    /// <summary>
    /// Converts the <see cref="SparseHeader"/> to a byte array.
    /// </summary>
    /// <returns>A byte array containing the header data.</returns>
    public byte[] ToBytes()
    {
        var data = new byte[SparseFormat.SparseHeaderSize];
        WriteTo(data);
        return data;
    }

    /// <summary>
    /// Validates whether the sparse header is valid.
    /// </summary>
    /// <returns>True if valid, otherwise false.</returns>
    public bool IsValid()
    {
        return Magic == SparseFormat.SparseHeaderMagic &&
               MajorVersion == 1 &&
               FileHeaderSize >= SparseFormat.SparseHeaderSize &&
               ChunkHeaderSize >= SparseFormat.ChunkHeaderSize &&
               BlockSize > 0 && BlockSize % 4 == 0;
    }
}

/// <summary>
/// Represents the structure of a chunk header.
/// </summary>
public readonly struct ChunkHeader
{
    /// <summary>The chunk type.</summary>
    public ushort ChunkType { get; init; }
    /// <summary>Reserved field.</summary>
    public ushort Reserved { get; init; }
    /// <summary>The number of blocks (in units of <see cref="SparseHeader.BlockSize"/>).</summary>
    public uint ChunkSize { get; init; }
    /// <summary>The total size of this chunk in the file (including the header).</summary>
    public uint TotalSize { get; init; }

    /// <summary>
    /// Parses a <see cref="ChunkHeader"/> from a byte sequence.
    /// </summary>
    /// <param name="data">A read-only byte sequence.</param>
    /// <returns>A parsed instance of <see cref="ChunkHeader"/>.</returns>
    public static ChunkHeader FromBytes(ReadOnlySpan<byte> data)
    {
        return data.Length < SparseFormat.ChunkHeaderSize
            ? throw new ArgumentException("Data length is insufficient to build ChunkHeader")
            : new ChunkHeader
            {
                ChunkType = BinaryPrimitives.ReadUInt16LittleEndian(data),
                Reserved = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(2)),
                ChunkSize = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(4)),
                TotalSize = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(8))
            };
    }

    /// <summary>
    /// Writes the <see cref="ChunkHeader"/> to a byte sequence.
    /// </summary>
    /// <param name="span">The target byte sequence.</param>
    public void WriteTo(Span<byte> span)
    {
        if (span.Length < SparseFormat.ChunkHeaderSize)
        {
            throw new ArgumentException("Span length is insufficient to write ChunkHeader");
        }

        BinaryPrimitives.WriteUInt16LittleEndian(span, ChunkType);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(2), Reserved);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(4), ChunkSize);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(8), TotalSize);
    }

    /// <summary>
    /// Converts the <see cref="ChunkHeader"/> to a byte array.
    /// </summary>
    /// <returns>A byte array containing the header data.</returns>
    public byte[] ToBytes()
    {
        var data = new byte[SparseFormat.ChunkHeaderSize];
        WriteTo(data);
        return data;
    }

    /// <summary>
    /// Validates whether the chunk header is valid.
    /// </summary>
    /// <returns>True if valid, otherwise false.</returns>
    public bool IsValid()
    {
        return ChunkType is (ushort)Models.ChunkType.Raw or
               (ushort)Models.ChunkType.Fill or
               (ushort)Models.ChunkType.DontCare or
               (ushort)Models.ChunkType.Crc32;
    }
}
