namespace FirmwareKit.Sparse.DataProviders;

public interface ISparseDataProvider : IDisposable
{
    long Length { get; }
    void WriteTo(Stream stream);
    int Read(long offset, byte[] buffer, int bufferOffset, int count);
    int Read(long offset, Span<byte> buffer);
    ISparseDataProvider GetSubProvider(long offset, long length);
}
