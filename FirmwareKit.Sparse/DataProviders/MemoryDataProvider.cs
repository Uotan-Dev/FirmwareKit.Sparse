namespace FirmwareKit.Sparse.DataProviders;

public class MemoryDataProvider : ISparseDataProvider
{
    private readonly byte[] data;
    private readonly int _offset;
    private readonly int _length;

    public MemoryDataProvider(byte[] data, int offset = 0, int length = -1)
    {
        this.data = data;
        _offset = offset;
        _length = length < 0 ? data.Length - offset : length;
    }

    public long Length => _length;

    public void WriteTo(Stream stream) => stream.Write(data, _offset, _length);

    public int Read(long offset, byte[] buffer, int bufferOffset, int count)
    {
        return Read(offset, buffer.AsSpan(bufferOffset, count));
    }

    public int Read(long offset, Span<byte> buffer)
    {
        var available = (int)Math.Max(0, _length - offset);
        var toCopy = Math.Min(buffer.Length, available);
        if (toCopy <= 0)
        {
            return 0;
        }

        data.AsSpan(_offset + (int)offset, toCopy).CopyTo(buffer);
        return toCopy;
    }

    public ISparseDataProvider GetSubProvider(long offset, long length)
        => new MemoryDataProvider(data, _offset + (int)offset, (int)length);

    public void Dispose() { }
}
