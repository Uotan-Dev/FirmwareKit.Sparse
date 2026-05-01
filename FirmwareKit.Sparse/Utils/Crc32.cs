namespace FirmwareKit.Sparse.Utils;

using Force.Crc32;
using System.Buffers;

/// <summary>
/// CRC32 utility class for checksum calculation using Crc32.NET.
/// Optimized for AOT compatibility with minimal allocations.
/// <para>使用 Crc32.NET 进行校验和计算的 CRC32 工具类。针对 AOT 兼容性优化，最小化内存分配。</para>
/// </summary>
public static class Crc32
{
    private const int BufferSize = 8192;

    private static readonly ThreadLocal<byte[]> LocalBuffer = new ThreadLocal<byte[]>(() => new byte[BufferSize]);

    private static byte[] GetLocalBuffer()
    {
        return LocalBuffer.Value ?? throw new InvalidOperationException("ThreadLocal buffer not initialized");
    }

    /// <summary>
    /// Calculates the CRC32 checksum of the given data range.
    /// <para>计算给定数据范围的 CRC32 校验和。</para>
    /// </summary>
    /// <param name="data">The data range. <para>数据范围。</para></param>
    /// <returns>The calculated CRC32 checksum. <para>计算得到的 CRC32 校验和。</para></returns>
    public static uint Calculate(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return 0;
        }

        var localBuffer = GetLocalBuffer();
        if (data.Length <= BufferSize)
        {
            data.CopyTo(localBuffer);
            return Crc32Algorithm.Compute(localBuffer, 0, data.Length);
        }

        var poolBuffer = ArrayPool<byte>.Shared.Rent(data.Length);
        try
        {
            data.CopyTo(poolBuffer);
            return Crc32Algorithm.Compute(poolBuffer, 0, data.Length);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(poolBuffer);
        }
    }

    /// <summary>
    /// Updates the CRC32 checksum with the given data using incremental calculation.
    /// <para>使用增量计算方式，用给定数据更新 CRC32 校验和。</para>
    /// </summary>
    /// <param name="crc">The current CRC32 value. <para>当前的 CRC32 值。</para></param>
    /// <param name="data">The data byte array. <para>数据字节数组。</para></param>
    /// <param name="offset">The starting offset in the array. <para>数组中的起始偏移量。</para></param>
    /// <param name="length">The length of data to process. <para>要处理的数据长度。</para></param>
    /// <returns>The updated CRC32 value. <para>更新后的 CRC32 值。</para></returns>
    public static uint Update(uint crc, byte[] data, int offset = 0, int length = -1)
    {
        if (length == -1)
        {
            length = data.Length - offset;
        }

        if (length <= 0)
        {
            return crc;
        }

        return Update(crc, new ReadOnlySpan<byte>(data, offset, length));
    }

    /// <summary>
    /// Updates the CRC32 checksum with the given data range using incremental calculation.
    /// <para>使用增量计算方式，用给定数据范围更新 CRC32 校验和。</para>
    /// </summary>
    /// <param name="crc">The current CRC32 value. <para>当前的 CRC32 值。</para></param>
    /// <param name="data">The data range. <para>数据范围。</para></param>
    /// <returns>The updated CRC32 value. <para>更新后的 CRC32 值。</para></returns>
    public static uint Update(uint crc, ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return crc;
        }

        var localBuffer = GetLocalBuffer();
        if (data.Length <= BufferSize)
        {
            data.CopyTo(localBuffer);
            return Crc32Algorithm.Append(crc, localBuffer, 0, data.Length);
        }

        var poolBuffer = ArrayPool<byte>.Shared.Rent(data.Length);
        try
        {
            data.CopyTo(poolBuffer);
            return Crc32Algorithm.Append(crc, poolBuffer, 0, data.Length);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(poolBuffer);
        }
    }

    /// <summary>
    /// Updates the CRC32 checksum with zero bytes (simulating a gap or sparse area).
    /// <para>用零字节更新 CRC32 校验和（模拟间隙或稀疏区域）。</para>
    /// </summary>
    /// <param name="crc">The current CRC32 value. <para>当前的 CRC32 值。</para></param>
    /// <param name="length">Number of zero bytes to process. <para>要处理的零字节数。</para></param>
    /// <returns>The updated CRC32 value. <para>更新后的 CRC32 值。</para></returns>
    public static uint UpdateZero(uint crc, long length)
    {
        if (length <= 0)
        {
            return crc;
        }

        var localBuffer = GetLocalBuffer();
        var buffer = localBuffer;
        if (buffer.Length < BufferSize)
        {
            buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        }
        try
        {
            Array.Clear(buffer, 0, BufferSize);

            var result = crc;
            var remaining = length;
            while (remaining > 0)
            {
                var chunk = (int)Math.Min(BufferSize, remaining);
                result = Crc32Algorithm.Append(result, buffer, 0, chunk);
                remaining -= chunk;
            }

            return result;
        }
        finally
        {
            if (buffer != localBuffer)
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }

    /// <summary>
    /// Updates the CRC32 checksum with a repeated 4-byte value.
    /// <para>用重复的 4 字节值更新 CRC32 校验和。</para>
    /// </summary>
    /// <param name="crc">The current CRC32 value. <para>当前的 CRC32 值。</para></param>
    /// <param name="value">The 4-byte pattern to repeat. <para>要重复的 4 字节模式。</para></param>
    /// <param name="totalLength">Total number of bytes to process. <para>要处理的总字节数。</para></param>
    /// <returns>The updated CRC32 value. <para>更新后的 CRC32 值。</para></returns>
    public static uint UpdateRepeated(uint crc, uint value, long totalLength)
    {
        if (totalLength <= 0)
        {
            return crc;
        }

        var localBuffer = GetLocalBuffer();
        var block = localBuffer;
        if (block.Length < BufferSize)
        {
            block = ArrayPool<byte>.Shared.Rent(BufferSize);
        }
        try
        {
            var pattern = (byte)(value & 0xFF);
            var pattern1 = (byte)((value >> 8) & 0xFF);
            var pattern2 = (byte)((value >> 16) & 0xFF);
            var pattern3 = (byte)((value >> 24) & 0xFF);

            for (var i = 0; i < BufferSize; i += 4)
            {
                block[i] = pattern;
                block[i + 1] = pattern1;
                block[i + 2] = pattern2;
                block[i + 3] = pattern3;
            }

            var result = crc;
            var remaining = totalLength;
            while (remaining > 0)
            {
                var chunk = (int)Math.Min(BufferSize, remaining);
                result = Crc32Algorithm.Append(result, block, 0, chunk);
                remaining -= chunk;
            }

            return result;
        }
        finally
        {
            if (block != localBuffer)
            {
                ArrayPool<byte>.Shared.Return(block);
            }
        }
    }

    /// <summary>
    /// Returns the initial CRC32 value.
    /// <para>返回 CRC32 的初始值。</para>
    /// </summary>
    public static uint Begin() => 0;

    /// <summary>
    /// Finalizes the CRC32 calculation.
    /// <para>完成 CRC32 计算。</para>
    /// </summary>
    /// <param name="crc">The current CRC32 value. <para>当前的 CRC32 值。</para></param>
    /// <returns>The finalized CRC32 value. <para>完成后的 CRC32 值。</para></returns>
    public static uint Finish(uint crc) => crc;
}
