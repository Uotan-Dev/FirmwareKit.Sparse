namespace FirmwareKit.Sparse.IO;

using FirmwareKit.Sparse.Core;
using FirmwareKit.Sparse.Models;
using FirmwareKit.Sparse.Utils;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Provides methods for writing sparse image data.
/// </summary>
public static class SparseWriter
{
    private const int BufferSize = 1024 * 1024;

    /// <summary>
    /// Writes the sparse file content to the specified stream asynchronously.
    /// </summary>
    public static async Task WriteToStreamAsync(SparseFile sparseFile, Stream stream, bool sparse = true, bool gzip = false, bool includeCrc = false, CancellationToken cancellationToken = default)
    {
        if (!sparse)
        {
            await WriteRawToStreamAsync(sparseFile, stream, false, cancellationToken);
            return;
        }

        Stream targetStream = stream;
        if (gzip)
        {
            targetStream = new System.IO.Compression.GZipStream(stream, System.IO.Compression.CompressionMode.Compress, true);
        }

        try
        {
            var sortedChunks = sparseFile.Chunks.OrderBy(c => c.StartBlock).ToList();
            var finalChunks = new List<SparseChunk>();
            uint currentBlock = 0;

            foreach (SparseChunk? chunk in sortedChunks)
            {
                if (chunk.StartBlock > currentBlock)
                {
                    var gapBlocks = chunk.StartBlock - currentBlock;
                    finalChunks.Add(new SparseChunk(new ChunkHeader
                    {
                        ChunkType = (ushort)ChunkType.DontCare,
                        ChunkSize = gapBlocks,
                        TotalSize = (uint)sparseFile.Header.ChunkHeaderSize
                    })
                    { StartBlock = currentBlock });
                }
                finalChunks.Add(chunk);
                currentBlock = chunk.StartBlock + chunk.Header.ChunkSize;
            }

            SparseHeader outHeader = sparseFile.Header;
            var sumBlocks = currentBlock;
            var needsTrailingSkip = outHeader.TotalBlocks > sumBlocks;

            var totalChunks = (uint)finalChunks.Count;
            if (needsTrailingSkip)
            {
                totalChunks++;
            }
            if (includeCrc) totalChunks++;

            outHeader = outHeader with { TotalChunks = totalChunks };
            if (sumBlocks > outHeader.TotalBlocks)
            {
                outHeader = outHeader with { TotalBlocks = sumBlocks };
            }

            var headerDataArr = new byte[SparseFormat.SparseHeaderSize];
            outHeader.WriteTo(headerDataArr);
            await targetStream.WriteAsync(headerDataArr, 0, headerDataArr.Length, cancellationToken);
            if (outHeader.FileHeaderSize > SparseFormat.SparseHeaderSize)
            {
                var pad = outHeader.FileHeaderSize - SparseFormat.SparseHeaderSize;
                var padBuf = new byte[pad];
                await targetStream.WriteAsync(padBuf, 0, padBuf.Length, cancellationToken);
            }

            var checksum = Crc32.Begin();
            var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
            try
            {
                var fillValData = new byte[4];
                foreach (SparseChunk chunk in finalChunks)
                {
                    var headerLen = outHeader.ChunkHeaderSize;
                    var headerBuf = new byte[headerLen];
                    var expectedDataSize = (long)chunk.Header.ChunkSize * outHeader.BlockSize;
                    // Ensure the TotalSize field matches the header size + payload size
                    {
                        uint expectedTotal = chunk.Header.ChunkType == (ushort)ChunkType.Raw
                            ? (uint)(outHeader.ChunkHeaderSize + expectedDataSize)
                            : chunk.Header.ChunkType == (ushort)ChunkType.Fill
                                ? (uint)(outHeader.ChunkHeaderSize + 4)
                                : (uint)outHeader.ChunkHeaderSize;

                        var headerToWrite = chunk.Header;
                        if (headerToWrite.TotalSize != expectedTotal)
                        {
                            headerToWrite = headerToWrite with { TotalSize = expectedTotal };
                        }

                        headerToWrite.WriteTo(headerBuf);
                    }

                    await targetStream.WriteAsync(headerBuf, 0, headerBuf.Length, cancellationToken);


                    switch (chunk.Header.ChunkType)
                    {
                        case (ushort)ChunkType.Raw:
                            if (chunk.DataProvider != null)
                            {
                                await chunk.DataProvider.WriteToAsync(targetStream, cancellationToken);

                                if (includeCrc)
                                {
                                    long providerOffset = 0;
                                    while (providerOffset < chunk.DataProvider.Length)
                                    {
                                        var toRead = (int)Math.Min(buffer.Length, chunk.DataProvider.Length - providerOffset);
                                        var read = chunk.DataProvider.Read(providerOffset, buffer.AsSpan(0, toRead));
                                        if (read <= 0) break;
                                        checksum = Crc32.Update(checksum, buffer.AsSpan(0, read));
                                        providerOffset += read;
                                    }
                                }

                                var providerLength = chunk.DataProvider.Length;
                                var padding = expectedDataSize - providerLength;
                                if (padding > 0)
                                {
                                    Array.Clear(buffer, 0, (int)Math.Min(buffer.Length, padding));
                                    while (padding > 0)
                                    {
                                        var toWrite = (int)Math.Min(buffer.Length, padding);
                                        await targetStream.WriteAsync(buffer, 0, toWrite, cancellationToken);
                                        if (includeCrc) checksum = Crc32.UpdateZero(checksum, toWrite);
                                        padding -= toWrite;
                                    }
                                }
                            }
                            else
                            {
                                Array.Clear(buffer, 0, (int)Math.Min(buffer.Length, (int)expectedDataSize));
                                var remaining = expectedDataSize;
                                while (remaining > 0)
                                {
                                    var toWrite = (int)Math.Min(buffer.Length, remaining);
                                    await targetStream.WriteAsync(buffer, 0, toWrite, cancellationToken);
                                    if (includeCrc) checksum = Crc32.UpdateZero(checksum, toWrite);
                                    remaining -= toWrite;
                                }
                            }
                            break;

                        case (ushort)ChunkType.Fill:
                            BinaryPrimitives.WriteUInt32LittleEndian(fillValData, chunk.FillValue);
                            await targetStream.WriteAsync(fillValData, 0, fillValData.Length, cancellationToken);
                            if (includeCrc) checksum = Crc32.UpdateRepeated(checksum, chunk.FillValue, expectedDataSize);
                            break;



                        case (ushort)ChunkType.DontCare:
                            if (includeCrc) checksum = Crc32.UpdateZero(checksum, expectedDataSize);
                            break;
                    }
                }

                if (needsTrailingSkip)
                {
                    var skipBlocks = outHeader.TotalBlocks - sumBlocks;
                    var skipChunkHeader = new ChunkHeader
                    {
                        ChunkType = (ushort)ChunkType.DontCare,
                        ChunkSize = skipBlocks,
                        TotalSize = (uint)outHeader.ChunkHeaderSize
                    };
                    var skipHeaderBuf = new byte[outHeader.ChunkHeaderSize];
                    skipChunkHeader.WriteTo(skipHeaderBuf);
                    await targetStream.WriteAsync(skipHeaderBuf, 0, skipHeaderBuf.Length, cancellationToken);
                    if (includeCrc) checksum = Crc32.UpdateZero(checksum, (long)skipBlocks * outHeader.BlockSize);
                }

                if (includeCrc)
                {
                    var finalChecksum = Crc32.Finish(checksum);
                    var crcChunkHeader = new ChunkHeader
                    {
                        ChunkType = (ushort)ChunkType.Crc32,
                        ChunkSize = 0,
                        TotalSize = (uint)(outHeader.ChunkHeaderSize + 4)
                    };
                    var crcHeaderBuf = new byte[outHeader.ChunkHeaderSize];
                    crcChunkHeader.WriteTo(crcHeaderBuf);
                    await targetStream.WriteAsync(crcHeaderBuf, 0, crcHeaderBuf.Length, cancellationToken);
                    BinaryPrimitives.WriteUInt32LittleEndian(fillValData, finalChecksum);
                    await targetStream.WriteAsync(fillValData, 0, 4, cancellationToken);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            if (gzip) await targetStream.FlushAsync(cancellationToken);
        }
        finally
        {
            if (gzip && targetStream != null)
            {
#if NET6_0_OR_GREATER
                await targetStream.DisposeAsync();
#else
                targetStream.Dispose();
#endif
            }
        }
    }

    /// <summary>
    /// Writes the raw (uncompressed) data of the sparse file to the specified stream asynchronously.
    /// </summary>
    public static async Task WriteRawToStreamAsync(SparseFile sparseFile, Stream stream, bool sparseMode = false, CancellationToken cancellationToken = default)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        uint? currentBufferFillValue = null;
        try
        {
            foreach (SparseChunk chunk in sparseFile.Chunks)
            {
                var size = (long)chunk.Header.ChunkSize * sparseFile.Header.BlockSize;
                switch (chunk.Header.ChunkType)
                {
                    case (ushort)ChunkType.Raw:
                        currentBufferFillValue = null;
                        if (chunk.DataProvider != null)
                        {
                            await chunk.DataProvider.WriteToAsync(stream, cancellationToken);
                            var remainingPadding = size - chunk.DataProvider.Length;
                            if (remainingPadding > 0)
                            {
                                Array.Clear(buffer, 0, (int)Math.Min(buffer.Length, remainingPadding));
                                while (remainingPadding > 0)
                                {
                                    var toWrite = (int)Math.Min(buffer.Length, remainingPadding);
                                    await stream.WriteAsync(buffer, 0, toWrite, cancellationToken);
                                    remainingPadding -= toWrite;
                                }
                            }
                        }
                        else
                        {
                            if (sparseMode && stream.CanSeek)
                            {
                                stream.Seek(size, SeekOrigin.Current);
                            }
                            else
                            {
                                Array.Clear(buffer, 0, (int)Math.Min(buffer.Length, size));
                                var remaining = size;
                                while (remaining > 0)
                                {
                                    var toWrite = (int)Math.Min(buffer.Length, remaining);
                                    await stream.WriteAsync(buffer, 0, toWrite, cancellationToken);
                                    remaining -= toWrite;
                                }
                            }
                        }
                        break;

                    case (ushort)ChunkType.Fill:
                        if (currentBufferFillValue != chunk.FillValue)
                        {
                            for (var r = 0; r <= buffer.Length - 4; r += 4)
                            {
                                BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(r), chunk.FillValue);
                            }
                            currentBufferFillValue = chunk.FillValue;
                        }

                        var remainingFill = size;
                        while (remainingFill > 0)
                        {
                            var toWrite = (int)Math.Min(buffer.Length, remainingFill);
                            await stream.WriteAsync(buffer, 0, toWrite, cancellationToken);
                            remainingFill -= toWrite;
                        }
                        break;

                    case (ushort)ChunkType.DontCare:
                        currentBufferFillValue = null;
                        if (sparseMode && stream.CanSeek)
                        {
                            stream.Seek(size, SeekOrigin.Current);
                        }
                        else
                        {
                            Array.Clear(buffer, 0, (int)Math.Min(buffer.Length, size));
                            var remainingSkip = size;
                            while (remainingSkip > 0)
                            {
                                var toWrite = (int)Math.Min(buffer.Length, remainingSkip);
                                await stream.WriteAsync(buffer, 0, toWrite, cancellationToken);
                                remainingSkip -= toWrite;
                            }
                        }
                        break;
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Writes the sparse file content to the specified stream.
    /// </summary>
    public static void WriteToStream(SparseFile sparseFile, Stream stream, bool sparse = true, bool gzip = false, bool includeCrc = false)
    {
        if (!sparse)
        {
            WriteRawToStream(sparseFile, stream);
            return;
        }

        Stream targetStream = stream;
        if (gzip)
        {
            targetStream = new System.IO.Compression.GZipStream(stream, System.IO.Compression.CompressionMode.Compress, true);
        }

        try
        {
            var sortedChunks = sparseFile.Chunks.OrderBy(c => c.StartBlock).ToList();
            var finalChunks = new List<SparseChunk>();
            uint currentBlock = 0;

            foreach (SparseChunk? chunk in sortedChunks)
            {
                if (chunk.StartBlock > currentBlock)
                {
                    var gapBlocks = chunk.StartBlock - currentBlock;
                    finalChunks.Add(new SparseChunk(new ChunkHeader
                    {
                        ChunkType = (ushort)ChunkType.DontCare,
                        ChunkSize = gapBlocks,
                        TotalSize = (uint)sparseFile.Header.ChunkHeaderSize
                    })
                    { StartBlock = currentBlock });
                }
                finalChunks.Add(chunk);
                currentBlock = chunk.StartBlock + chunk.Header.ChunkSize;
            }

            SparseHeader outHeader = sparseFile.Header;
            var sumBlocks = currentBlock;
            var needsTrailingSkip = outHeader.TotalBlocks > sumBlocks;

            var totalChunks = (uint)finalChunks.Count;
            if (needsTrailingSkip) totalChunks++;
            if (includeCrc) totalChunks++;

            outHeader = outHeader with { TotalChunks = totalChunks };
            if (sumBlocks > outHeader.TotalBlocks) outHeader = outHeader with { TotalBlocks = sumBlocks };

            Span<byte> headerData = stackalloc byte[SparseFormat.SparseHeaderSize];
            outHeader.WriteTo(headerData);
            targetStream.Write(headerData);
            if (outHeader.FileHeaderSize > SparseFormat.SparseHeaderSize)
            {
                var pad = outHeader.FileHeaderSize - SparseFormat.SparseHeaderSize;
                var padBuf = new byte[pad];
                targetStream.Write(padBuf, 0, padBuf.Length);
            }

            var checksum = Crc32.Begin();
            var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
            try
            {
                Span<byte> fillValData = stackalloc byte[4];
                foreach (SparseChunk chunk in finalChunks)
                {
                    var headerLen = outHeader.ChunkHeaderSize;
                    var headerBuf = new byte[headerLen];
                    var expectedDataSize = (long)chunk.Header.ChunkSize * outHeader.BlockSize;
                    // Ensure the TotalSize field matches the header size + payload size
                    {
                        uint expectedTotal = chunk.Header.ChunkType == (ushort)ChunkType.Raw
                            ? (uint)(outHeader.ChunkHeaderSize + expectedDataSize)
                            : chunk.Header.ChunkType == (ushort)ChunkType.Fill
                                ? (uint)(outHeader.ChunkHeaderSize + 4)
                                : (uint)outHeader.ChunkHeaderSize;

                        var headerToWrite = chunk.Header;
                        if (headerToWrite.TotalSize != expectedTotal)
                        {
                            headerToWrite = headerToWrite with { TotalSize = expectedTotal };
                        }

                        headerToWrite.WriteTo(headerBuf);
                    }

                    targetStream.Write(headerBuf, 0, headerBuf.Length);


                    switch (chunk.Header.ChunkType)
                    {
                        case (ushort)ChunkType.Raw:
                            if (chunk.DataProvider != null)
                            {
                                long providerOffset = 0;
                                while (providerOffset < chunk.DataProvider.Length)
                                {
                                    var toRead = (int)Math.Min(buffer.Length, chunk.DataProvider.Length - providerOffset);
                                    var read = chunk.DataProvider.Read(providerOffset, buffer, 0, toRead);
                                    if (read <= 0) break;

                                    targetStream.Write(buffer, 0, read);
                                    if (includeCrc) checksum = Crc32.Update(checksum, buffer, 0, read);
                                    providerOffset += read;
                                }

                                var padding = expectedDataSize - providerOffset;
                                if (padding > 0)
                                {
                                    Array.Clear(buffer, 0, (int)Math.Min(buffer.Length, padding));
                                    while (padding > 0)
                                    {
                                        var toWrite = (int)Math.Min(buffer.Length, padding);
                                        targetStream.Write(buffer, 0, toWrite);
                                        if (includeCrc) checksum = Crc32.UpdateZero(checksum, toWrite);
                                        padding -= toWrite;
                                    }
                                }
                            }
                            else
                            {
                                Array.Clear(buffer, 0, buffer.Length);
                                var remaining = expectedDataSize;
                                while (remaining > 0)
                                {
                                    var toWrite = (int)Math.Min(buffer.Length, remaining);
                                    targetStream.Write(buffer, 0, toWrite);
                                    if (includeCrc) checksum = Crc32.UpdateZero(checksum, toWrite);
                                    remaining -= toWrite;
                                }
                            }
                            break;

                        case (ushort)ChunkType.Fill:
                            var fillBytes = new byte[4];
                            BinaryPrimitives.WriteUInt32LittleEndian(fillBytes, chunk.FillValue);
                            targetStream.Write(fillBytes, 0, fillBytes.Length);
                            if (includeCrc) checksum = Crc32.UpdateRepeated(checksum, chunk.FillValue, expectedDataSize);

                            break;

                        case (ushort)ChunkType.DontCare:
                            if (includeCrc) checksum = Crc32.UpdateZero(checksum, expectedDataSize);
                            break;
                    }
                }

                if (needsTrailingSkip)
                {
                    var skipSize = outHeader.TotalBlocks - sumBlocks;
                    var skipHeader = new ChunkHeader
                    {
                        ChunkType = (ushort)ChunkType.DontCare,
                        Reserved = 0,
                        ChunkSize = skipSize,
                        TotalSize = (uint)outHeader.ChunkHeaderSize
                    };
                    var skipBytes = new byte[outHeader.ChunkHeaderSize];
                    skipHeader.WriteTo(skipBytes);
                    targetStream.Write(skipBytes, 0, skipBytes.Length);
                    if (includeCrc) checksum = Crc32.UpdateZero(checksum, (long)skipSize * outHeader.BlockSize);
                }

                if (includeCrc)
                {
                    var finalChecksum = Crc32.Finish(checksum);
                    var crcHeader = new ChunkHeader
                    {
                        ChunkType = (ushort)ChunkType.Crc32,
                        Reserved = 0,
                        ChunkSize = 0,
                        TotalSize = (uint)(outHeader.ChunkHeaderSize + 4)
                    };
                    var finalCrcData = new byte[4];
                    BinaryPrimitives.WriteUInt32LittleEndian(finalCrcData, finalChecksum);
                    var crcHeaderBytes = new byte[outHeader.ChunkHeaderSize];
                    crcHeader.WriteTo(crcHeaderBytes);
                    targetStream.Write(crcHeaderBytes, 0, crcHeaderBytes.Length);
                    targetStream.Write(finalCrcData, 0, 4);

                    if (targetStream.CanSeek)
                    {
                        var currentPos = targetStream.Position;
                        targetStream.Position = 0;
                        SparseHeader updatedHeader = outHeader with { ImageChecksum = finalChecksum };
                        Span<byte> hData = stackalloc byte[SparseFormat.SparseHeaderSize];
                        updatedHeader.WriteTo(hData);
                        targetStream.Write(hData);
                        if (updatedHeader.FileHeaderSize > SparseFormat.SparseHeaderSize)
                        {
                            var pad = updatedHeader.FileHeaderSize - SparseFormat.SparseHeaderSize;
                            var padBuf = new byte[pad];
                            targetStream.Write(padBuf, 0, padBuf.Length);
                        }
                        targetStream.Position = currentPos;
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
        finally
        {
            if (gzip) targetStream.Dispose();
        }
    }

    /// <summary>
    /// Writes the raw (uncompressed) data of the sparse file to the specified stream.
    /// </summary>
    public static void WriteRawToStream(SparseFile sparseFile, Stream stream, bool sparseMode = false)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            var exportStartBlock = sparseFile.RawExportStartBlock;
            uint currentBlock = 0;
            foreach (SparseChunk chunk in sparseFile.Chunks)
            {
                var size = (long)chunk.Header.ChunkSize * sparseFile.Header.BlockSize;
                if (exportStartBlock.HasValue && currentBlock + chunk.Header.ChunkSize <= exportStartBlock.Value)
                {
                    currentBlock += chunk.Header.ChunkSize;
                    continue;
                }

                switch (chunk.Header.ChunkType)
                {
                    case (ushort)ChunkType.Raw:
                        if (chunk.DataProvider != null)
                        {
                            long written = 0;
                            while (written < chunk.DataProvider.Length)
                            {
                                var toRead = (int)Math.Min(buffer.Length, chunk.DataProvider.Length - written);
                                var read = chunk.DataProvider.Read(written, buffer, 0, toRead);
                                if (read <= 0) break;
                                stream.Write(buffer, 0, read);
                                written += read;
                            }
                            if (written < size)
                            {
                                Array.Clear(buffer, 0, (int)Math.Min(buffer.Length, size - written));
                                while (written < size)
                                {
                                    var toWrite = (int)Math.Min(buffer.Length, size - written);
                                    stream.Write(buffer, 0, toWrite);
                                    written += toWrite;
                                }
                            }
                        }
                        else
                        {
                            if (sparseMode && stream.CanSeek)
                            {
                                stream.Seek(size, SeekOrigin.Current);
                            }
                            else
                            {
                                Array.Clear(buffer, 0, buffer.Length);
                                var remaining = size;
                                while (remaining > 0)
                                {
                                    var toWrite = (int)Math.Min(buffer.Length, remaining);
                                    stream.Write(buffer, 0, toWrite);
                                    remaining -= toWrite;
                                }
                            }
                        }
                        break;

                    case (ushort)ChunkType.Fill:
                        var valBytes = new byte[4];
                        BinaryPrimitives.WriteUInt32LittleEndian(valBytes, chunk.FillValue);
                        for (var i = 0; i <= buffer.Length - 4; i += 4)
                        {
                            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(i), chunk.FillValue);
                        }

                        var fillRemaining = size;
                        while (fillRemaining > 0)
                        {
                            var toWrite = (int)Math.Min(buffer.Length, fillRemaining);
                            stream.Write(buffer, 0, toWrite);
                            fillRemaining -= toWrite;
                        }
                        break;

                    case (ushort)ChunkType.DontCare:
                        if (sparseMode && stream.CanSeek)
                        {
                            stream.Seek(size, SeekOrigin.Current);
                        }
                        else
                        {
                            Array.Clear(buffer, 0, buffer.Length);
                            var remaining = size;
                            while (remaining > 0)
                            {
                                var toWrite = (int)Math.Min(buffer.Length, remaining);
                                stream.Write(buffer, 0, toWrite);
                                remaining -= toWrite;
                            }
                        }
                        break;
                }

                currentBlock += chunk.Header.ChunkSize;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        if (sparseMode && stream.CanSeek)
        {
            stream.SetLength(stream.Position);
        }
        else if (!sparseMode && stream.CanSeek && !sparseFile.RawExportStartBlock.HasValue)
        {
            stream.SetLength((long)sparseFile.Header.TotalBlocks * sparseFile.Header.BlockSize);
        }

    }

    /// <summary>
    /// Writes the sparse file content using a custom callback for each data block.
    /// </summary>
    public static void WriteWithCallback(SparseFile sparseFile, SparseFile.SparseWriteCallback callback, bool sparse = true, bool includeCrc = false)
    {
        if (!sparse)
        {
            var buffer = new byte[BufferSize];
            foreach (SparseChunk chunk in sparseFile.Chunks)
            {
                var size = (long)chunk.Header.ChunkSize * sparseFile.Header.BlockSize;
                switch (chunk.Header.ChunkType)
                {
                    case (ushort)ChunkType.Raw:
                        if (chunk.DataProvider != null)
                        {
                            long written = 0;
                            while (written < chunk.DataProvider.Length)
                            {
                                var toRead = (int)Math.Min(buffer.Length, chunk.DataProvider.Length - written);
                                var read = chunk.DataProvider.Read(written, buffer, 0, toRead);
                                if (read <= 0) break;
                                if (callback(buffer, read) < 0) return;
                                written += read;
                            }
                            if (written < size)
                            {
                                Array.Clear(buffer, 0, (int)Math.Min(buffer.Length, size - written));
                                while (written < size)
                                {
                                    var toWrite = (int)Math.Min(buffer.Length, size - written);
                                    if (callback(buffer, toWrite) < 0) return;
                                    written += toWrite;
                                }
                            }
                        }
                        else
                        {
                            if (callback(null, (int)size) < 0) return;
                        }
                        break;
                    case (ushort)ChunkType.Fill:
                        var fillValBytes = new byte[4];
                        BinaryPrimitives.WriteUInt32LittleEndian(fillValBytes, chunk.FillValue);
                        for (var i = 0; i <= buffer.Length - 4; i += 4)
                        {
                            Array.Copy(fillValBytes, 0, buffer, i, 4);
                        }

                        var fillRemaining = size;
                        while (fillRemaining > 0)
                        {
                            var toWrite = (int)Math.Min(buffer.Length, fillRemaining);
                            if (callback(buffer, toWrite) < 0) return;
                            fillRemaining -= toWrite;
                        }
                        break;
                    case (ushort)ChunkType.DontCare:
                        if (callback(null, (int)size) < 0) return;
                        break;
                }
            }
            return;
        }

        using var ms = new MemoryStream();
        WriteToStream(sparseFile, ms, true, false, includeCrc);
        var bytes = ms.ToArray();
        callback(bytes, bytes.Length);
    }
}
