namespace FirmwareKit.Sparse.Models;

public class SparseChunk : IDisposable
{
    public SparseChunk(ChunkHeader header)
    {
        Header = header;
    }

    public uint StartBlock { get; set; } = 0;
    public ChunkHeader Header { get; set; }
    public ISparseDataProvider? DataProvider { get; set; }
    public uint FillValue { get; set; }

    public void Dispose() => DataProvider?.Dispose();
}
