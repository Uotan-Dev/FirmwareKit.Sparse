namespace FirmwareKit.Sparse.Utils;

/// <summary>
/// Provides cross-framework compatibility extension methods for Stream and math operations.
/// <para>提供 Stream 和数学运算的跨框架兼容性扩展方法。</para>
/// </summary>
internal static class CompatibilityExtensions
{
    /// <summary>
    /// Reads exactly <paramref name="count"/> bytes from the stream into the buffer.
    /// Throws <see cref="EndOfStreamException"/> if the stream ends before all bytes are read.
    /// <para>从流中精确读取指定数量的字节到缓冲区。如果流在读取完所有字节之前结束，则抛出 EndOfStreamException。</para>
    /// </summary>
    /// <param name="stream">The source stream. <para>源流。</para></param>
    /// <param name="buffer">Destination buffer. <para>目标缓冲区。</para></param>
    /// <param name="offset">Offset in the buffer to start writing. <para>缓冲区中开始写入的偏移量。</para></param>
    /// <param name="count">Number of bytes to read. <para>要读取的字节数。</para></param>
    public static void ReadExactly(this Stream stream, byte[] buffer, int offset, int count)
    {
#if NET7_0_OR_GREATER
        stream.ReadExactly(buffer, offset, count);
#else
        int totalRead = 0;
        while (totalRead < count)
        {
            int read = stream.Read(buffer, offset + totalRead, count - totalRead);
            if (read == 0) throw new EndOfStreamException();
            totalRead += read;
        }
#endif
    }

    /// <summary>
    /// Reads exactly enough bytes to fill the provided span from the stream.
    /// Throws <see cref="EndOfStreamException"/> if the stream ends before the span is filled.
    /// <para>从流中精确读取足够字节以填充提供的跨度。如果流在填充跨度之前结束，则抛出 EndOfStreamException。</para>
    /// </summary>
    /// <param name="stream">The source stream. <para>源流。</para></param>
    /// <param name="buffer">Destination span. <para>目标跨度。</para></param>
    public static void ReadExactly(this Stream stream, Span<byte> buffer)
    {
#if NET7_0_OR_GREATER
        stream.ReadExactly(buffer);
#elif NETSTANDARD2_1_OR_GREATER || NETCOREAPP2_1_OR_GREATER || NET5_0_OR_GREATER
        int totalRead = 0;
        while (totalRead < buffer.Length)
        {
            int read = stream.Read(buffer.Slice(totalRead));
            if (read == 0) throw new EndOfStreamException();
            totalRead += read;
        }
#else
        var pool = System.Buffers.ArrayPool<byte>.Shared.Rent(buffer.Length);
        try
        {
            int totalRead = 0;
            while (totalRead < buffer.Length)
            {
                int read = stream.Read(pool, totalRead, buffer.Length - totalRead);
                if (read == 0) throw new EndOfStreamException();
                totalRead += read;
            }
            new ReadOnlySpan<byte>(pool, 0, buffer.Length).CopyTo(buffer);
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(pool);
        }
#endif
    }

    /// <summary>
    /// Reads a sequence of bytes from the stream into the span.
    /// <para>从流中读取字节序列到跨度。</para>
    /// </summary>
    /// <param name="stream">The source stream. <para>源流。</para></param>
    /// <param name="buffer">Destination span. <para>目标跨度。</para></param>
    /// <returns>The number of bytes read. <para>读取的字节数。</para></returns>
    public static int Read(this Stream stream, Span<byte> buffer)
    {
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP2_1_OR_GREATER || NET5_0_OR_GREATER
        return stream.Read(buffer);
#else
        var pool = System.Buffers.ArrayPool<byte>.Shared.Rent(buffer.Length);
        try
        {
            var read = stream.Read(pool, 0, buffer.Length);
            if (read > 0)
            {
                new ReadOnlySpan<byte>(pool, 0, read).CopyTo(buffer);
            }
            return read;
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(pool);
        }
#endif
    }

    /// <summary>
    /// Writes a sequence of bytes from the read-only span to the stream.
    /// <para>将只读跨度中的字节序列写入流。</para>
    /// </summary>
    /// <param name="stream">The destination stream. <para>目标流。</para></param>
    /// <param name="buffer">Source read-only span. <para>源只读跨度。</para></param>
    public static void Write(this Stream stream, ReadOnlySpan<byte> buffer)
    {
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP2_1_OR_GREATER || NET5_0_OR_GREATER
        stream.Write(buffer);
#else
        var pool = System.Buffers.ArrayPool<byte>.Shared.Rent(buffer.Length);
        try
        {
            buffer.CopyTo(pool);
            stream.Write(pool, 0, buffer.Length);
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(pool);
        }
#endif
    }

    /// <summary>
    /// Clamps a value to be within the specified inclusive range.
    /// <para>将值限制在指定的包含范围内。</para>
    /// </summary>
    /// <param name="value">The value to clamp. <para>要限制的值。</para></param>
    /// <param name="min">The minimum allowed value. <para>允许的最小值。</para></param>
    /// <param name="max">The maximum allowed value. <para>允许的最大值。</para></param>
    /// <returns>The clamped value. <para>限制后的值。</para></returns>
    public static long Clamp(long value, long min, long max)
    {
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP2_0_OR_GREATER || NET5_0_OR_GREATER
        return System.Math.Clamp(value, min, max);
#else
        return value < min ? min : (value > max ? max : value);
#endif
    }
}
