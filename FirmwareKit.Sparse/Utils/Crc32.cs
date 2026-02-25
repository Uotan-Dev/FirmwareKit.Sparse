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
    /// Calculates the CRC32 checksum for the given data.
    /// </summary>
    public static uint Calculate(byte[] data, int offset = 0, int length = -1)
    {
        if (length == -1)
        {
            length = data.Length - offset;
        }
        return Calculate(new ReadOnlySpan<byte>(data, offset, length));
    }

    /// <summary>
    /// Calculates the CRC32 checksum for the given data span.
    /// </summary>
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
    /// Updates the CRC32 checksum with the given data (incremental calculation).
    /// </summary>
    public static uint Update(uint crc, byte[] data, int offset = 0, int length = -1)
    {
        if (length == -1)
        {
            length = data.Length - offset;
        }
        return Update(crc, new ReadOnlySpan<byte>(data, offset, length));
    }

    /// <summary>
    /// Updates the CRC32 checksum with the given data span (incremental calculation).
    /// </summary>
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
    /// Starts a new CRC32 calculation.
    /// </summary>
    public static uint Begin() => 0xFFFFFFFF;

    /// <summary>
    /// Finalizes the CRC32 calculation.
    /// </summary>
    public static uint Finish(uint crc) => crc ^ 0xFFFFFFFF;
}
