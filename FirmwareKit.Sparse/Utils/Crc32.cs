namespace FirmwareKit.Sparse.Utils;

/// <summary>
/// CRC32 utility class for checksum calculation.
/// </summary>
public static class Crc32
{
    private static readonly uint[] CrcTable = new uint[256];
    private const uint Polynomial = 0xEDB88320;

    static Crc32()
    {
        InitializeCrcTable();
    }

    /// <summary>
    /// Initializes the CRC32 lookup table.
    /// </summary>
    private static void InitializeCrcTable()
    {
        for (uint i = 0; i < 256; i++)
        {
            var crc = i;
            for (var j = 0; j < 8; j++)
            {
                if ((crc & 1) != 0)
                {
                    crc = (crc >> 1) ^ Polynomial;
                }
                else
                {
                    crc >>= 1;
                }
            }
            CrcTable[i] = crc;
        }
    }

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
        var crc = 0xFFFFFFFF;
        foreach (var b in data)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }
        return crc ^ 0xFFFFFFFF;
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
        var result = crc;
        foreach (var b in data)
        {
            result = CrcTable[(result ^ b) & 0xFF] ^ (result >> 8);
        }
        return result;
    }

    /// <summary>
    /// Updates the CRC32 checksum with a repeated 4-byte pattern.
    /// </summary>
    /// <param name="crc">The current CRC32 value.</param>
    /// <param name="pattern">The 4-byte pattern to repeat.</param>
    /// <param name="length">The total length in bytes (must be a multiple of 4).</param>
    /// <returns>The updated CRC32 value.</returns>
    public static uint UpdateRepeated(uint crc, uint pattern, long length)
    {
        var result = crc;
        var p0 = (byte)(pattern & 0xFF);
        var p1 = (byte)((pattern >> 8) & 0xFF);
        var p2 = (byte)((pattern >> 16) & 0xFF);
        var p3 = (byte)((pattern >> 24) & 0xFF);

        for (long i = 0; i < length / 4; i++)
        {
            result = CrcTable[(result ^ p0) & 0xFF] ^ (result >> 8);
            result = CrcTable[(result ^ p1) & 0xFF] ^ (result >> 8);
            result = CrcTable[(result ^ p2) & 0xFF] ^ (result >> 8);
            result = CrcTable[(result ^ p3) & 0xFF] ^ (result >> 8);
        }

        // Handle remaining bytes if any (though usually it's multiple of 4)
        var remaining = (int)(length % 4);
        if (remaining > 0) result = CrcTable[(result ^ p0) & 0xFF] ^ (result >> 8);
        if (remaining > 1) result = CrcTable[(result ^ p1) & 0xFF] ^ (result >> 8);
        if (remaining > 2) result = CrcTable[(result ^ p2) & 0xFF] ^ (result >> 8);

        return result;
    }

    /// <summary>
    /// Updates the CRC32 checksum with a zero-filled sequence of the specified length.
    /// </summary>
    /// <param name="crc">The current CRC32 value.</param>
    /// <param name="length">The length of the zero-filled sequence.</param>
    /// <returns>The updated CRC32 value.</returns>
    public static uint UpdateZero(uint crc, long length)
    {
        var result = crc;
        for (long i = 0; i < length; i++)
        {
            result = CrcTable[(result ^ 0) & 0xFF] ^ (result >> 8);
        }
        return result;
    }

    /// <summary>
    /// Starts a new CRC32 calculation.
    /// </summary>
    /// <returns>The initial CRC32 value.</returns>
    public static uint Begin()
    {
        return 0xFFFFFFFF;
    }

    /// <summary>
    /// Completes the CRC32 calculation.
    /// </summary>
    /// <param name="crc">The final CRC32 value.</param>
    /// <returns>The finished checksum.</returns>
    public static uint Finish(uint crc)
    {
        return crc ^ 0xFFFFFFFF;
    }
}
