namespace FirmwareKit.Sparse.DataProviders;

public class StreamDataProvider(Stream stream, long offset, long length, bool leaveOpen = true) : ISparseDataProvider
{
    public long Length => length;

    public void WriteTo(Stream outStream)
    {
        if (stream.CanSeek)
        {
            stream.Seek(offset, SeekOrigin.Begin);
        }
        var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(1024 * 1024);
        try
        {
            var remaining = length;
            while (remaining > 0)
            {
                var toRead = (int)Math.Min(buffer.Length, remaining);
                var read = stream.Read(buffer, 0, toRead);
                if (read == 0)
                {
                    break;
                }

                outStream.Write(buffer, 0, read);
                remaining -= read;
            }
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public int Read(long inOffset, byte[] buffer, int bufferOffset, int count)
    {
        return Read(inOffset, buffer.AsSpan(bufferOffset, count));
    }

    public int Read(long inOffset, Span<byte> buffer)
    {
        if (inOffset >= length)
        {
            return 0;
        }

        var toRead = (int)Math.Min(buffer.Length, (int)(length - inOffset));
        if (stream.CanSeek)
        {
            stream.Seek(offset + inOffset, SeekOrigin.Begin);
        }
        return stream.Read(buffer.Slice(0, toRead));
    }

    public ISparseDataProvider GetSubProvider(long subOffset, long subLength)
        => new StreamDataProvider(stream, offset + subOffset, subLength, true);

    public void Dispose()
    {
        if (!leaveOpen)
        {
            stream.Dispose();
        }
    }
}
