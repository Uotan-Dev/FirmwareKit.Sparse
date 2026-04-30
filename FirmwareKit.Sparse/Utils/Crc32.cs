namespace FirmwareKit.Sparse.Utils;

using Force.Crc32;
using System.Buffers;
using System.Buffers.Binary;

/// <summary>
/// CRC32 utility class for checksum calculation using Crc32.NET.
/// Optimized for AOT compatibility with minimal allocations.
/// </summary>
public static class Crc32
{
    private const int BufferSize = 8192;

    // 优化：使用 ThreadLocal 静态缓冲区，减少对 ArrayPool 的频繁调用
    private static readonly ThreadLocal<byte[]> LocalBuffer = new ThreadLocal<byte[]>(() => new byte[BufferSize]);

    private static byte[] GetLocalBuffer()
    {
        return LocalBuffer.Value ?? throw new InvalidOperationException("ThreadLocal buffer not initialized");
    }

    /// <summary>
    /// Calculates the CRC32 checksum of the given data range.
    /// </summary>
    /// <param name="data">The data range.</param>
    /// <returns>The calculated CRC32 checksum.</returns>
    public static uint Calculate(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return 0;
        }

        // 优化：小数据使用 ThreadLocal 本地缓冲区，避免 ArrayPool 开销
        var localBuffer = GetLocalBuffer();
        if (data.Length <= BufferSize)
        {
            data.CopyTo(localBuffer);
            return Crc32Algorithm.Compute(localBuffer, 0, data.Length);
        }

        // 只有超过 BufferSize 才租用 ArrayPool
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

        if (length <= 0)
        {
            return crc;
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

        // 优化：小数据使用 ThreadLocal 本地缓冲区
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
    /// </summary>
    public static uint UpdateZero(uint crc, long length)
    {
        if (length <= 0)
        {
            return crc;
        }

        // 优化：使用 ThreadLocal 本地缓冲区，避免每次调用 ArrayPool
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
    /// </summary>
    public static uint UpdateRepeated(uint crc, uint value, long totalLength)
    {
        if (totalLength <= 0)
        {
            return crc;
        }

        // 优化：使用 ThreadLocal 本地缓冲区
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
    /// </summary>
    public static uint Begin() => 0;

    /// <summary>
    /// Finalizes the CRC32 calculation.
    /// </summary>
    public static uint Finish(uint crc) => crc;
}
