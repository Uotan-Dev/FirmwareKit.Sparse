namespace FirmwareKit.Sparse.Utils;

internal static class CompatibilityExtensions
{
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

    public static long Clamp(long value, long min, long max)
    {
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP2_0_OR_GREATER || NET5_0_OR_GREATER
        return System.Math.Clamp(value, min, max);
#else
        return value < min ? min : (value > max ? max : value);
#endif
    }
}
