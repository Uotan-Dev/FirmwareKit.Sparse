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
public static partial class SparseWriter
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
            await WriteRawToStreamAsync(sparseFile, stream, false, cancellationToken).ConfigureAwait(false);
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
            await targetStream.WriteAsync(headerDataArr, 0, headerDataArr.Length, cancellationToken).ConfigureAwait(false);
            if (outHeader.FileHeaderSize > SparseFormat.SparseHeaderSize)
            {
                var pad = outHeader.FileHeaderSize - SparseFormat.SparseHeaderSize;
                var padBuf = new byte[pad];
                await targetStream.WriteAsync(padBuf, 0, padBuf.Length, cancellationToken).ConfigureAwait(false);
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
                    await targetStream.WriteAsync(headerBuf, 0, headerLen, cancellationToken).ConfigureAwait(false);

                    switch (chunk.Header.ChunkType)
                    {
                        case (ushort)ChunkType.Raw:
                            if (chunk.DataProvider != null)
                            {
                                await chunk.DataProvider.WriteToAsync(targetStream, cancellationToken).ConfigureAwait(false);

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
                                        await targetStream.WriteAsync(buffer, 0, toWrite, cancellationToken).ConfigureAwait(false);
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
                                    await targetStream.WriteAsync(buffer, 0, toWrite, cancellationToken).ConfigureAwait(false);
                                    if (includeCrc) checksum = Crc32.UpdateZero(checksum, toWrite);
                                    remaining -= toWrite;
                                }
                            }
                            break;

                        case (ushort)ChunkType.Fill:
                            BinaryPrimitives.WriteUInt32LittleEndian(fillValData, chunk.FillValue);
                            await targetStream.WriteAsync(fillValData, 0, fillValData.Length, cancellationToken).ConfigureAwait(false);
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
                    await targetStream.WriteAsync(headerBuf, 0, outHeader.ChunkHeaderSize, cancellationToken).ConfigureAwait(false);
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
                    await targetStream.WriteAsync(headerBuf, 0, outHeader.ChunkHeaderSize, cancellationToken).ConfigureAwait(false);
                    BinaryPrimitives.WriteUInt32LittleEndian(fillValData, finalChecksum);
                    await targetStream.WriteAsync(fillValData, 0, 4, cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
                ArrayPool<byte>.Shared.Return(headerBuf);
            }

            if (gzip) await targetStream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (gzip && targetStream != null)
            {
#if NET6_0_OR_GREATER
                await targetStream.DisposeAsync().ConfigureAwait(false);
#else
                targetStream.Dispose();
#endif
            }
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

}
