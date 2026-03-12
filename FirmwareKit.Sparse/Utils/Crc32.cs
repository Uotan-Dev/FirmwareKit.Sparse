namespace FirmwareKit.Sparse.Utils;

using Force.Crc32;

/// <summary>
/// CRC32 utility class for checksum calculation.
/// </summary>
public static class Crc32
{
    /// <summary>
    /// Calculates the CRC32 checksum of the given data.
    /// </summary>
    /// <param name="data">The data byte array.</param>
    /// <param name="offset">The starting offset.</param>
    /// <param name="length">The length. If -1, all data from the offset to the end of the array is used.</param>
    /// <returns>The calculated CRC32 checksum.</returns>
    public static uint Calculate(byte[] data, int offset = 0, int length = -1)
    {
        if (length == -1)
        {
            length = data.Length - offset;
        }
        return Calculate(new ReadOnlySpan<byte>(data, offset, length));
    }

    /// <summary>
    /// Calculates the CRC32 checksum of the given data range.
    /// </summary>
    /// <param name="data">The data range.</param>
    /// <returns>The calculated CRC32 checksum.</returns>
    public static uint Calculate(ReadOnlySpan<byte> data)
    {
        return Finish(Update(Begin(), data));
    }

    /// <summary>
    /// Updates the CRC32 checksum with the given data using incremental calculation.
    /// </summary>
    /// <param name="crc">The current CRC32 value.</param>
    /// <param name="data">The data byte array.</param>
    /// <param name="offset">The starting offset in the array.</param>
    /// <param name="length">The length.</param>
    /// <returns>The updated CRC32 value.</returns>
    public static uint Update(uint crc, byte[] data, int offset = 0, int length = -1)
    {
        if (length == -1)
        {
            length = data.Length - offset;
        }
        return Update(crc, new ReadOnlySpan<byte>(data, offset, length));
    }

    /// <summary>
    /// Updates the CRC32 checksum with the given data range using incremental calculation.
    /// </summary>
    /// <param name="crc">The current CRC32 value.</param>
    /// <param name="data">The data range.</param>
    /// <returns>The updated CRC32 value.</returns>
    public static uint Update(uint crc, ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return crc;
        }

        return Crc32Algorithm.Append(crc, data.ToArray());
    }

    /// <summary>
    /// Updates the CRC32 checksum with zero bytes (simulating a gap or sparse area).
    /// </summary>
    public static uint UpdateZero(uint crc, long length)
    {
        if (length <= 0)
        {
            return crc;
        }

        var buffer = new byte[8192];
        var result = crc;
        var remaining = length;
        while (remaining > 0)
        {
            var chunk = (int)Math.Min(buffer.Length, remaining);
            result = Crc32Algorithm.Append(result, buffer, 0, chunk);
            remaining -= chunk;
        }

        return result;
    }

    /// <summary>
    /// Updates the CRC32 checksum with a repeated 4-byte value.
    /// </summary>
    public static uint UpdateRepeated(uint crc, uint value, long totalLength)
    {
        if (totalLength <= 0)
        {
            return crc;
        }

        var pattern = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(pattern, value);

        var block = new byte[8192];
        for (var i = 0; i < block.Length; i += 4)
        {
            Buffer.BlockCopy(pattern, 0, block, i, 4);
        }

        var result = crc;
        var remaining = totalLength;
        while (remaining > 0)
        {
            var chunk = (int)Math.Min(block.Length, remaining);
            result = Crc32Algorithm.Append(result, block, 0, chunk);
            remaining -= chunk;
        }

        return result;
    }

    /// <summary>
    /// Returns the initial CRC32 value.
    /// </summary>
    public static uint Begin() => 0xFFFFFFFF;

    /// <summary>
    /// Finalizes the CRC32 calculation.
    /// </summary>
    public static uint Finish(uint crc) => crc ^ 0xFFFFFFFF;
}
