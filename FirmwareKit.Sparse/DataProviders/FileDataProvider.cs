namespace FirmwareKit.Sparse.DataProviders;

public class FileDataProvider : ISparseDataProvider
{
    private readonly string filePath;
    private readonly long offset;
    private readonly long length;

    public FileDataProvider(string filePath, long offset, long length)
    {
        this.filePath = filePath;
        this.offset = offset;
        this.length = length;
    }

    public long Length => length;

    public void WriteTo(Stream stream)
    {
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(1024 * 1024);
        try
        {
            var remaining = length;
            fs.Seek(offset, SeekOrigin.Begin);

            while (remaining > 0)
            {
                var toRead = (int)Math.Min(buffer.Length, remaining);
                var read = fs.Read(buffer, 0, toRead);
                if (read == 0)
                {
                    break;
                }

                stream.Write(buffer, 0, read);
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

        var toRead = (int)Math.Min(buffer.Length, length - inOffset);
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
#if NET6_0_OR_GREATER
        System.IO.RandomAccess.Read(fs.SafeFileHandle, buffer.Slice(0, toRead), offset + inOffset);
        return toRead;
#else
        fs.Seek(offset + inOffset, SeekOrigin.Begin);
        return fs.Read(buffer.Slice(0, toRead));
#endif
    }

    public ISparseDataProvider GetSubProvider(long subOffset, long subLength)
        => new FileDataProvider(filePath, offset + subOffset, subLength);

    public void Dispose() { }
}
