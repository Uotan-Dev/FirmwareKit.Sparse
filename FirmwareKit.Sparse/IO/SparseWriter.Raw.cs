namespace FirmwareKit.Sparse.IO;

using FirmwareKit.Sparse.Core;
using FirmwareKit.Sparse.Models;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Raw data writing methods for <see cref="SparseWriter"/>.
/// <para>SparseWriter 的原始数据写入方法。</para>
/// </summary>
public static partial class SparseWriter
{
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
}
