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
    /// Asynchronously write the serialized sparse image to the provided <see cref="Stream"/>.
    /// </summary>
    /// <param name="sparseFile">The <see cref="SparseFile"/> to serialize and write.</param>
    /// <param name="stream">Destination stream to receive the sparse image bytes.</param>
    /// <param name="sparse">If false, write raw/un-sparsed data instead of sparse format (bool).</param>
    /// <param name="gzip">If true, compress the output with GZip (bool).</param>
    /// <param name="includeCrc">If true, include a CRC chunk and update header checksum (bool).</param>
    /// <param name="cancellationToken">Cancellation token to cancel the asynchronous write operation.</param>
    /// <returns>A task that completes when the write operation finishes.</returns>
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
            // Optimized: Chunks are already sorted in SparseFile, skip sorting
            var chunks = sparseFile.Chunks;
            int chunkCount = chunks.Count;

            // Preallocate capacity to avoid dynamic expansion
            var finalChunks = new List<SparseChunk>(chunkCount + 10);
            uint currentBlock = 0;

            for (int i = 0; i < chunkCount; i++)
            {
                var chunk = chunks[i];
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
            // Optimized: Reuse header buffer instead of creating new one for each chunk
            var headerBuf = ArrayPool<byte>.Shared.Rent(outHeader.ChunkHeaderSize);
            try
            {
                var fillValData = new byte[4];
                foreach (SparseChunk chunk in finalChunks)
                {
                    var headerLen = outHeader.ChunkHeaderSize;
                    var expectedDataSize = (long)chunk.Header.ChunkSize * outHeader.BlockSize;
                    // Ensure the TotalSize field matches the header size + payload size
                    uint expectedTotal = chunk.Header.ChunkType switch
                    {
                        (ushort)ChunkType.Raw => (uint)(outHeader.ChunkHeaderSize + expectedDataSize),
                        (ushort)ChunkType.Fill => (uint)(outHeader.ChunkHeaderSize + 4),
                        _ => (uint)outHeader.ChunkHeaderSize
                    };

                    ChunkHeader headerToWrite = chunk.Header;
                    if (headerToWrite.TotalSize != expectedTotal)
                    {
                        headerToWrite = headerToWrite with { TotalSize = expectedTotal };
                    }

                    headerToWrite.WriteTo(headerBuf);
                    await targetStream.WriteAsync(headerBuf, 0, headerLen, cancellationToken);


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
                    skipChunkHeader.WriteTo(headerBuf); // 优化：复用 headerBuf
                    await targetStream.WriteAsync(headerBuf, 0, outHeader.ChunkHeaderSize, cancellationToken);
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
                    crcChunkHeader.WriteTo(headerBuf); // 优化：复用 headerBuf
                    await targetStream.WriteAsync(headerBuf, 0, outHeader.ChunkHeaderSize, cancellationToken);
                    BinaryPrimitives.WriteUInt32LittleEndian(fillValData, finalChecksum);
                    await targetStream.WriteAsync(fillValData, 0, 4, cancellationToken);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
                ArrayPool<byte>.Shared.Return(headerBuf);
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
    /// Asynchronously write only the raw data payloads of the sparse file to the specified stream.
    /// This writes RAW/FILL/DONT_CARE payloads unframed (no sparse headers) and is useful for exports.
    /// </summary>
    /// <param name="sparseFile">The <see cref="SparseFile"/> containing chunks to export.</param>
    /// <param name="stream">Destination stream to receive raw payload bytes.</param>
    /// <param name="sparseMode">If true and the stream supports seeking, use seeking for sparse skips (bool).</param>
    /// <param name="cancellationToken">Cancellation token to cancel the asynchronous write operation.</param>
    /// <returns>A task that completes when the raw write operation finishes.</returns>
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
    /// Write the serialized sparse image to the provided <see cref="Stream"/> synchronously.
    /// </summary>
    /// <param name="sparseFile">The <see cref="SparseFile"/> to serialize and write.</param>
    /// <param name="stream">Destination stream to receive the sparse image bytes.</param>
    /// <param name="sparse">If false, write raw/un-sparsed data instead of sparse format (bool).</param>
    /// <param name="gzip">If true, compress the output with GZip (bool).</param>
    /// <param name="includeCrc">If true, include a CRC chunk and update header checksum (bool).</param>
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
            // Optimized: Chunks are already sorted in SparseFile, skip sorting
            var chunks = sparseFile.Chunks;
            int chunkCount = chunks.Count;

            // Preallocate capacity to avoid dynamic expansion
            var finalChunks = new List<SparseChunk>(chunkCount + 10);
            uint currentBlock = 0;

            for (int i = 0; i < chunkCount; i++)
            {
                var chunk = chunks[i];
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
                Span<byte> padBuf = stackalloc byte[pad];
                targetStream.Write(padBuf);
            }

            var checksum = Crc32.Begin();
            var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
            var headerBuf = ArrayPool<byte>.Shared.Rent(outHeader.ChunkHeaderSize); // 优化：只租用一次 header 缓冲区
            try
            {
                Span<byte> fillValData = stackalloc byte[4];
                foreach (SparseChunk chunk in finalChunks)
                {
                    var headerLen = outHeader.ChunkHeaderSize;
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

                    targetStream.Write(headerBuf, 0, headerLen); // 优化：使用 headerLen

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
                            BinaryPrimitives.WriteUInt32LittleEndian(fillValData, chunk.FillValue); // 优化：直接用 stackalloc 的 span
                            targetStream.Write(fillValData);
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
                    skipHeader.WriteTo(headerBuf); // 优化：复用 header 缓冲区
                    targetStream.Write(headerBuf, 0, outHeader.ChunkHeaderSize);
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
                    crcHeader.WriteTo(headerBuf); // 优化：复用 header 缓冲区
                    BinaryPrimitives.WriteUInt32LittleEndian(fillValData, finalChecksum); // 优化：复用 span
                    targetStream.Write(headerBuf, 0, outHeader.ChunkHeaderSize);
                    targetStream.Write(fillValData);

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
                            Span<byte> padBuf = stackalloc byte[pad]; // 优化：用 stackalloc
                            targetStream.Write(padBuf);
                        }
                        targetStream.Position = currentPos;
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
                ArrayPool<byte>.Shared.Return(headerBuf); // 优化：归还 header 缓冲区
            }
        }
        finally
        {
            if (gzip) targetStream.Dispose();
        }
    }

    /// <summary>
    /// Write only the raw data payloads of the sparse file to the specified stream synchronously.
    /// </summary>
    /// <param name="sparseFile">The <see cref="SparseFile"/> containing chunks to export.</param>
    /// <param name="stream">Destination stream to receive raw payload bytes.</param>
    /// <param name="sparseMode">If true and the stream supports seeking, use seeking for sparse skips (bool).</param>
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
    /// Serialize the sparse file and deliver the output in-memory using a custom <paramref name="callback"/>.
    /// The callback receives byte blocks and should return a non-negative value to continue.
    /// </summary>
    /// <param name="sparseFile">The <see cref="SparseFile"/> to serialize.</param>
    /// <param name="callback">Callback invoked with serialized byte blocks; returning &lt;0 will abort.</param>
    /// <param name="sparse">If false, produce raw/un-sparsed bytes rather than sparse format (bool).</param>
    /// <param name="includeCrc">If true, include a CRC chunk and update header checksum (bool).</param>
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
                        for (var i = 0; i <= buffer.Length - 4; i += 4)
                        {
                            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(i), chunk.FillValue);
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
