using System.Buffers;
using System.IO.MemoryMappedFiles;
using Microsoft.Win32.SafeHandles;

namespace FirmwareKit.Sparse.DataProviders;

/// <summary>
/// Provides access to a region of a file using memory-mapped I/O.
/// This is optimized for 32-bit AOT environments handling large files (up to 16GB).
/// </summary>
public class MemoryMappedDataProvider : ISparseDataProvider, IDisposable
{
    private readonly MemoryMappedFile mmf;
    private readonly MemoryMappedViewAccessor accessor;
    private readonly long offset;
    private readonly long length;

    /// <summary>
    /// Initialize a new MemoryMappedDataProvider.
    /// </summary>
    /// <param name="filePath">Path to the source file</param>
    /// <param name="offset">Byte offset within the file</param>
    /// <param name="length">Length of the data segment</param>
    public MemoryMappedDataProvider(string filePath, long offset, long length)
    {
        this.offset = offset;
        this.length = length;

        // Use MemoryMappedFile for efficient access to large files
        mmf = MemoryMappedFile.CreateFromFile(
            filePath,
            FileMode.Open,
            null,
            offset + length,
            MemoryMappedFileAccess.Read);

        accessor = mmf.CreateViewAccessor(offset, length, MemoryMappedFileAccess.Read);
    }

    /// <summary>
    /// Gets the length of the data segment.
    /// </summary>
    public long Length => length;

    /// <summary>
    /// Write data to stream.
    /// </summary>
    public void WriteTo(Stream stream)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            long remaining = length;
            long pos = 0;

            while (remaining > 0)
            {
                int toRead = (int)Math.Min(buffer.Length, remaining);
                accessor.ReadArray(pos, buffer, 0, toRead);
                stream.Write(buffer, 0, toRead);
                pos += toRead;
                remaining -= toRead;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Asynchronously write data to stream.
    /// </summary>
    public async Task WriteToAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            long remaining = length;
            long pos = 0;

            while (remaining > 0 && !cancellationToken.IsCancellationRequested)
            {
                int toRead = (int)Math.Min(buffer.Length, remaining);
                accessor.ReadArray(pos, buffer, 0, toRead);
                await stream.WriteAsync(buffer, 0, toRead, cancellationToken);
                pos += toRead;
                remaining -= toRead;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Read data into buffer.
    /// </summary>
    public int Read(long inOffset, byte[] buffer, int bufferOffset, int count)
    {
        if (inOffset >= length)
            return 0;

        int toRead = (int)Math.Min(count, length - inOffset);
        accessor.ReadArray(inOffset, buffer, bufferOffset, toRead);
        return toRead;
    }

    /// <summary>
    /// Read data into span.
    /// </summary>
    public int Read(long inOffset, Span<byte> buffer)
    {
        if (inOffset >= length)
            return 0;

        int toRead = (int)Math.Min(buffer.Length, length - inOffset);
        byte[] tempBuffer = ArrayPool<byte>.Shared.Rent(toRead);
        try
        {
            accessor.SafeMemoryMappedViewHandle.ReadArray<byte>((ulong)inOffset, tempBuffer, 0, toRead);
            tempBuffer.AsSpan(0, toRead).CopyTo(buffer);
            return toRead;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(tempBuffer);
        }
    }

    /// <summary>
    /// Create a sub-provider.
    /// </summary>
    public ISparseDataProvider GetSubProvider(long subOffset, long subLength)
    {
        return new MemoryMappedDataProvider(mmf, offset + subOffset, subLength);
    }

    private MemoryMappedDataProvider(MemoryMappedFile mmf, long offset, long length)
    {
        this.mmf = mmf;
        this.offset = offset;
        this.length = length;
        this.accessor = mmf.CreateViewAccessor(offset, length, MemoryMappedFileAccess.Read);
    }

    /// <summary>
    /// Dispose resources.
    /// </summary>
    public void Dispose()
    {
        accessor.Dispose();
        mmf.Dispose();
    }
}