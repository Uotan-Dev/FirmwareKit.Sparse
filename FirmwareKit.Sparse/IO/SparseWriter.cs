namespace FirmwareKit.Sparse.IO;

using FirmwareKit.Sparse.Core;
using FirmwareKit.Sparse.Models;
using FirmwareKit.Sparse.Utils;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Provides methods for writing sparse image data.
/// <para>提供写入稀疏镜像数据的方法。</para>
/// </summary>
public static class SparseWriter
{
    private const int BufferSize = 1024 * 1024;

    /// <summary>
    /// Asynchronously writes the serialized sparse image to the provided <see cref="Stream"/>.
    /// <para>异步将序列化的稀疏镜像写入提供的流。</para>
    /// </summary>
    /// <param name="sparseFile">The <see cref="SparseFile"/> to serialize and write. <para>要序列化并写入的 SparseFile。</para></param>
    /// <param name="stream">Destination stream to receive the sparse image bytes. <para>接收稀疏镜像字节的目标流。</para></param>
    /// <param name="sparse">If false, write raw/un-sparsed data instead of sparse format. <para>如果为 false，写入原始/非稀疏数据而非稀疏格式。</para></param>
    /// <param name="gzip">If true, compress the output with GZip. <para>如果为 true，使用 GZip 压缩输出。</para></param>
    /// <param name="includeCrc">If true, include a CRC chunk and update header checksum. <para>如果为 true，包含 CRC 数据块并更新头部校验和。</para></param>
    /// <param name="cancellationToken">Cancellation token to cancel the asynchronous write operation. <para>取消异步写入操作的取消令牌。</para></param>
    /// <returns>A task that completes when the write operation finishes. <para>写入操作完成时结束的任务。</para></returns>
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
            var chunks = sparseFile.Chunks;
            int chunkCount = chunks.Count;

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
            var headerBuf = ArrayPool<byte>.Shared.Rent(outHeader.ChunkHeaderSize);
            try
            {
                var fillValData = new byte[4];
                foreach (SparseChunk chunk in finalChunks)
                {
                    var headerLen = outHeader.ChunkHeaderSize;
                    var expectedDataSize = (long)chunk.Header.ChunkSize * outHeader.BlockSize;
                    uint expectedTotal = ChunkHelper.GetExpectedTotalSize(
                        chunk.Header.ChunkType, chunk.Header.ChunkSize,
                        outHeader.ChunkHeaderSize, outHeader.BlockSize);

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
                    skipChunkHeader.WriteTo(headerBuf);
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
                    crcChunkHeader.WriteTo(headerBuf);
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
    /// Asynchronously writes only the raw data payloads of the sparse file to the specified stream.
    /// This writes RAW/FILL/DONT_CARE payloads unframed (no sparse headers) and is useful for exports.
    /// <para>异步仅将稀疏文件的原始数据负载写入指定流。
    /// 写入 RAW/FILL/DONT_CARE 负载时不带帧（无稀疏头部），适用于导出。</para>
    /// </summary>
    /// <param name="sparseFile">The <see cref="SparseFile"/> containing chunks to export. <para>包含要导出数据块的 SparseFile。</para></param>
    /// <param name="stream">Destination stream to receive raw payload bytes. <para>接收原始负载字节的目标流。</para></param>
    /// <param name="sparseMode">If true and the stream supports seeking, use seeking for sparse skips. <para>如果为 true 且流支持定位，使用定位进行稀疏跳过。</para></param>
    /// <param name="cancellationToken">Cancellation token to cancel the asynchronous write operation. <para>取消异步写入操作的取消令牌。</para></param>
    /// <returns>A task that completes when the raw write operation finishes. <para>原始写入操作完成时结束的任务。</para></returns>
    public static async Task WriteRawToStreamAsync(SparseFile sparseFile, Stream stream, bool sparseMode = false, CancellationToken cancellationToken = default)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        uint? currentBufferFillValue = null;
        try
        {
            long writtenBytes = 0;
            uint skippedLeadingBlocks = 0;
            var exportStartBlock = sparseFile.RawExportStartBlock;

            foreach (SparseChunk chunk in sparseFile.Chunks)
            {
                if (exportStartBlock.HasValue &&
                    chunk.Header.ChunkType == (ushort)ChunkType.DontCare &&
                    chunk.StartBlock + chunk.Header.ChunkSize <= exportStartBlock.Value)
                {
                    skippedLeadingBlocks += chunk.Header.ChunkSize;
                    continue;
                }

                var size = (long)chunk.Header.ChunkSize * sparseFile.Header.BlockSize;
                switch (chunk.Header.ChunkType)
                {
                    case (ushort)ChunkType.Raw:
                        currentBufferFillValue = null;
                        if (chunk.DataProvider != null)
                        {
                            await chunk.DataProvider.WriteToAsync(stream, cancellationToken);
                            writtenBytes += size;
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
                                if (stream.Position > stream.Length)
                                    stream.SetLength(stream.Position);
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
                            writtenBytes += size;
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
                        writtenBytes += size;
                        break;

                    case (ushort)ChunkType.DontCare:
                        currentBufferFillValue = null;
                        if (sparseMode && stream.CanSeek)
                        {
                            stream.Seek(size, SeekOrigin.Current);
                            if (stream.Position > stream.Length)
                                stream.SetLength(stream.Position);
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
                        writtenBytes += size;
                        break;
                }
            }

            var effectiveTotalBlocks = sparseFile.RawExportTotalBlocks.HasValue
                ? sparseFile.RawExportTotalBlocks.Value - skippedLeadingBlocks
                : sparseFile.Header.TotalBlocks;
            var totalRawSize = (long)effectiveTotalBlocks * sparseFile.Header.BlockSize;
            var trailingZeros = totalRawSize - writtenBytes;
            if (trailingZeros > 0)
            {
                Array.Clear(buffer, 0, (int)Math.Min(buffer.Length, trailingZeros));
                while (trailingZeros > 0)
                {
                    var toWrite = (int)Math.Min(buffer.Length, trailingZeros);
                    await stream.WriteAsync(buffer, 0, toWrite, cancellationToken);
                    trailingZeros -= toWrite;
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Synchronously writes the serialized sparse image to the provided <see cref="Stream"/>.
    /// <para>同步将序列化的稀疏镜像写入提供的流。</para>
    /// </summary>
    /// <param name="sparseFile">The <see cref="SparseFile"/> to serialize and write. <para>要序列化并写入的 SparseFile。</para></param>
    /// <param name="stream">Destination stream to receive the sparse image bytes. <para>接收稀疏镜像字节的目标流。</para></param>
    /// <param name="sparse">If false, write raw/un-sparsed data instead of sparse format. <para>如果为 false，写入原始/非稀疏数据而非稀疏格式。</para></param>
    /// <param name="gzip">If true, compress the output with GZip. <para>如果为 true，使用 GZip 压缩输出。</para></param>
    /// <param name="includeCrc">If true, include a CRC chunk and update header checksum. <para>如果为 true，包含 CRC 数据块并更新头部校验和。</para></param>
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
            var chunks = sparseFile.Chunks;
            int chunkCount = chunks.Count;

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
            var headerBuf = ArrayPool<byte>.Shared.Rent(outHeader.ChunkHeaderSize);
            try
            {
                Span<byte> fillValData = stackalloc byte[4];
                foreach (SparseChunk chunk in finalChunks)
                {
                    var headerLen = outHeader.ChunkHeaderSize;
                    var expectedDataSize = (long)chunk.Header.ChunkSize * outHeader.BlockSize;
                    uint expectedTotal = ChunkHelper.GetExpectedTotalSize(
                        chunk.Header.ChunkType, chunk.Header.ChunkSize,
                        outHeader.ChunkHeaderSize, outHeader.BlockSize);

                    var headerToWrite = chunk.Header;
                    if (headerToWrite.TotalSize != expectedTotal)
                    {
                        headerToWrite = headerToWrite with { TotalSize = expectedTotal };
                    }

                    headerToWrite.WriteTo(headerBuf);
                    targetStream.Write(headerBuf, 0, headerLen);

                    switch (chunk.Header.ChunkType)
                    {
                        case (ushort)ChunkType.Raw:
                            if (chunk.DataProvider != null)
                            {
                                chunk.DataProvider.WriteTo(targetStream);

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
                                    var remainingPad = padding;
                                    while (remainingPad > 0)
                                    {
                                        var toWrite = (int)Math.Min(buffer.Length, remainingPad);
                                        targetStream.Write(buffer, 0, toWrite);
                                        if (includeCrc) checksum = Crc32.UpdateZero(checksum, toWrite);
                                        remainingPad -= toWrite;
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
                                    targetStream.Write(buffer, 0, toWrite);
                                    if (includeCrc) checksum = Crc32.UpdateZero(checksum, toWrite);
                                    remaining -= toWrite;
                                }
                            }
                            break;

                        case (ushort)ChunkType.Fill:
                            BinaryPrimitives.WriteUInt32LittleEndian(fillValData, chunk.FillValue);
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
                    var skipBlocks = outHeader.TotalBlocks - sumBlocks;
                    var skipChunkHeader = new ChunkHeader
                    {
                        ChunkType = (ushort)ChunkType.DontCare,
                        ChunkSize = skipBlocks,
                        TotalSize = (uint)outHeader.ChunkHeaderSize
                    };
                    skipChunkHeader.WriteTo(headerBuf);
                    targetStream.Write(headerBuf, 0, outHeader.ChunkHeaderSize);
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
                    crcChunkHeader.WriteTo(headerBuf);
                    targetStream.Write(headerBuf, 0, outHeader.ChunkHeaderSize);
                    BinaryPrimitives.WriteUInt32LittleEndian(fillValData, finalChecksum);
                    targetStream.Write(fillValData);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
                ArrayPool<byte>.Shared.Return(headerBuf);
            }
        }
        finally
        {
            if (gzip && targetStream != null)
            {
                targetStream.Dispose();
            }
        }
    }

    /// <summary>
    /// Synchronously writes only the raw data payloads of the sparse file to the specified stream.
    /// <para>同步仅将稀疏文件的原始数据负载写入指定流。</para>
    /// </summary>
    /// <param name="sparseFile">The <see cref="SparseFile"/> containing chunks to export. <para>包含要导出数据块的 SparseFile。</para></param>
    /// <param name="stream">Destination stream to receive raw payload bytes. <para>接收原始负载字节的目标流。</para></param>
    /// <param name="sparseMode">If true and the stream supports seeking, use seeking for sparse skips. <para>如果为 true 且流支持定位，使用定位进行稀疏跳过。</para></param>
    public static void WriteRawToStream(SparseFile sparseFile, Stream stream, bool sparseMode = false)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        uint? currentBufferFillValue = null;
        try
        {
            long writtenBytes = 0;
            uint skippedLeadingBlocks = 0;
            var exportStartBlock = sparseFile.RawExportStartBlock;

            foreach (SparseChunk chunk in sparseFile.Chunks)
            {
                if (exportStartBlock.HasValue &&
                    chunk.Header.ChunkType == (ushort)ChunkType.DontCare &&
                    chunk.StartBlock + chunk.Header.ChunkSize <= exportStartBlock.Value)
                {
                    skippedLeadingBlocks += chunk.Header.ChunkSize;
                    continue;
                }

                var size = (long)chunk.Header.ChunkSize * sparseFile.Header.BlockSize;
                switch (chunk.Header.ChunkType)
                {
                    case (ushort)ChunkType.Raw:
                        currentBufferFillValue = null;
                        if (chunk.DataProvider != null)
                        {
                            chunk.DataProvider.WriteTo(stream);
                            writtenBytes += size;
                            var remainingPadding = size - chunk.DataProvider.Length;
                            if (remainingPadding > 0)
                            {
                                Array.Clear(buffer, 0, (int)Math.Min(buffer.Length, remainingPadding));
                                var remainingPad = remainingPadding;
                                while (remainingPad > 0)
                                {
                                    var toWrite = (int)Math.Min(buffer.Length, remainingPad);
                                    stream.Write(buffer, 0, toWrite);
                                    remainingPad -= toWrite;
                                }
                            }
                        }
                        else
                        {
                            if (sparseMode && stream.CanSeek)
                            {
                                stream.Seek(size, SeekOrigin.Current);
                                if (stream.Position > stream.Length)
                                    stream.SetLength(stream.Position);
                            }
                            else
                            {
                                Array.Clear(buffer, 0, (int)Math.Min(buffer.Length, size));
                                var remaining = size;
                                while (remaining > 0)
                                {
                                    var toWrite = (int)Math.Min(buffer.Length, remaining);
                                    stream.Write(buffer, 0, toWrite);
                                    remaining -= toWrite;
                                }
                            }
                            writtenBytes += size;
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
                            stream.Write(buffer, 0, toWrite);
                            remainingFill -= toWrite;
                        }
                        writtenBytes += size;
                        break;

                    case (ushort)ChunkType.DontCare:
                        currentBufferFillValue = null;
                        if (sparseMode && stream.CanSeek)
                        {
                            stream.Seek(size, SeekOrigin.Current);
                            if (stream.Position > stream.Length)
                                stream.SetLength(stream.Position);
                        }
                        else
                        {
                            Array.Clear(buffer, 0, (int)Math.Min(buffer.Length, size));
                            var remainingSkip = size;
                            while (remainingSkip > 0)
                            {
                                var toWrite = (int)Math.Min(buffer.Length, remainingSkip);
                                stream.Write(buffer, 0, toWrite);
                                remainingSkip -= toWrite;
                            }
                        }
                        writtenBytes += size;
                        break;
                }
            }

            var effectiveTotalBlocks = sparseFile.RawExportTotalBlocks.HasValue
                ? sparseFile.RawExportTotalBlocks.Value - skippedLeadingBlocks
                : sparseFile.Header.TotalBlocks;
            var totalRawSize = (long)effectiveTotalBlocks * sparseFile.Header.BlockSize;
            var trailingZeros = totalRawSize - writtenBytes;
            if (trailingZeros > 0)
            {
                Array.Clear(buffer, 0, (int)Math.Min(buffer.Length, trailingZeros));
                while (trailingZeros > 0)
                {
                    var toWrite = (int)Math.Min(buffer.Length, trailingZeros);
                    stream.Write(buffer, 0, toWrite);
                    trailingZeros -= toWrite;
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Writes the sparse file using a custom callback for each data block instead of writing to a stream.
    /// <para>使用自定义回调为每个数据块写入稀疏文件，而非写入流。</para>
    /// </summary>
    /// <param name="sparseFile">The <see cref="SparseFile"/> to write. <para>要写入的 SparseFile。</para></param>
    /// <param name="callback">Callback invoked for each data block. <para>每个数据块调用的回调。</para></param>
    /// <param name="sparse">If true, write in sparse format; otherwise write raw blocks. <para>如果为 true，以稀疏格式写入；否则写入原始块。</para></param>
    /// <param name="includeCrc">If true, include CRC32 chunks. <para>如果为 true，包含 CRC32 数据块。</para></param>
    public static void WriteWithCallback(SparseFile sparseFile, SparseFile.SparseWriteCallback callback, bool sparse = true, bool includeCrc = false)
    {
        if (!sparse)
        {
            WriteRawWithCallback(sparseFile, callback);
            return;
        }

        var chunks = sparseFile.Chunks;
        int chunkCount = chunks.Count;

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

        var headerData = outHeader.ToBytes();
        callback(headerData, headerData.Length);

        if (outHeader.FileHeaderSize > SparseFormat.SparseHeaderSize)
        {
            var pad = new byte[outHeader.FileHeaderSize - SparseFormat.SparseHeaderSize];
            callback(pad, pad.Length);
        }

        var checksum = Crc32.Begin();
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            Span<byte> fillValData = stackalloc byte[4];
            foreach (SparseChunk chunk in finalChunks)
            {
                var expectedDataSize = (long)chunk.Header.ChunkSize * outHeader.BlockSize;
                uint expectedTotal = ChunkHelper.GetExpectedTotalSize(
                    chunk.Header.ChunkType, chunk.Header.ChunkSize,
                    outHeader.ChunkHeaderSize, outHeader.BlockSize);

                ChunkHeader headerToWrite = chunk.Header;
                if (headerToWrite.TotalSize != expectedTotal)
                {
                    headerToWrite = headerToWrite with { TotalSize = expectedTotal };
                }

                var chunkHeaderData = headerToWrite.ToBytes();
                callback(chunkHeaderData, chunkHeaderData.Length);

                switch (chunk.Header.ChunkType)
                {
                    case (ushort)ChunkType.Raw:
                        if (chunk.DataProvider != null)
                        {
                            long providerOffset = 0;
                            while (providerOffset < chunk.DataProvider.Length)
                            {
                                var toRead = (int)Math.Min(buffer.Length, chunk.DataProvider.Length - providerOffset);
                                var read = chunk.DataProvider.Read(providerOffset, buffer.AsSpan(0, toRead));
                                if (read <= 0) break;
                                callback(buffer, read);
                                if (includeCrc) checksum = Crc32.Update(checksum, buffer.AsSpan(0, read));
                                providerOffset += read;
                            }

                            var providerLength = chunk.DataProvider.Length;
                            var padding = expectedDataSize - providerLength;
                            if (padding > 0)
                            {
                                Array.Clear(buffer, 0, (int)Math.Min(buffer.Length, padding));
                                var remainingPad = padding;
                                while (remainingPad > 0)
                                {
                                    var toWrite = (int)Math.Min(buffer.Length, remainingPad);
                                    callback(buffer, toWrite);
                                    if (includeCrc) checksum = Crc32.UpdateZero(checksum, toWrite);
                                    remainingPad -= toWrite;
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
                                callback(buffer, toWrite);
                                if (includeCrc) checksum = Crc32.UpdateZero(checksum, toWrite);
                                remaining -= toWrite;
                            }
                        }
                        break;

                    case (ushort)ChunkType.Fill:
                        BinaryPrimitives.WriteUInt32LittleEndian(fillValData, chunk.FillValue);
                        callback(fillValData.ToArray(), 4);
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
                var skipHeaderData = skipChunkHeader.ToBytes();
                callback(skipHeaderData, skipHeaderData.Length);
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
                var crcHeaderData = crcChunkHeader.ToBytes();
                callback(crcHeaderData, crcHeaderData.Length);
                Span<byte> crcData = stackalloc byte[4];
                BinaryPrimitives.WriteUInt32LittleEndian(crcData, finalChecksum);
                callback(crcData.ToArray(), 4);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Writes raw data using a callback for each block.
    /// <para>使用回调为每个块写入原始数据。</para>
    /// </summary>
    private static void WriteRawWithCallback(SparseFile sparseFile, SparseFile.SparseWriteCallback callback)
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
                            long providerOffset = 0;
                            while (providerOffset < chunk.DataProvider.Length)
                            {
                                var toRead = (int)Math.Min(buffer.Length, chunk.DataProvider.Length - providerOffset);
                                var read = chunk.DataProvider.Read(providerOffset, buffer.AsSpan(0, toRead));
                                if (read <= 0) break;
                                callback(buffer, read);
                                providerOffset += read;
                            }
                            var remainingPadding = size - chunk.DataProvider.Length;
                            if (remainingPadding > 0)
                            {
                                Array.Clear(buffer, 0, (int)Math.Min(buffer.Length, remainingPadding));
                                var pad = remainingPadding;
                                while (pad > 0)
                                {
                                    var toWrite = (int)Math.Min(buffer.Length, pad);
                                    callback(buffer, toWrite);
                                    pad -= toWrite;
                                }
                            }
                        }
                        else
                        {
                            Array.Clear(buffer, 0, (int)Math.Min(buffer.Length, size));
                            var remaining = size;
                            while (remaining > 0)
                            {
                                var toWrite = (int)Math.Min(buffer.Length, remaining);
                                callback(buffer, toWrite);
                                remaining -= toWrite;
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
                            callback(buffer, toWrite);
                            remainingFill -= toWrite;
                        }
                        break;

                    case (ushort)ChunkType.DontCare:
                        currentBufferFillValue = null;
                        Array.Clear(buffer, 0, (int)Math.Min(buffer.Length, size));
                        var remainingSkip = size;
                        while (remainingSkip > 0)
                        {
                            var toWrite = (int)Math.Min(buffer.Length, remainingSkip);
                            callback(buffer, toWrite);
                            remainingSkip -= toWrite;
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
}
