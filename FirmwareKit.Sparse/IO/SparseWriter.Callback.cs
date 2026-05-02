namespace FirmwareKit.Sparse.IO;

using FirmwareKit.Sparse.Core;
using FirmwareKit.Sparse.Models;
using FirmwareKit.Sparse.Utils;
using System.Buffers;
using System.Buffers.Binary;

/// <summary>
/// Callback-based writing methods for <see cref="SparseWriter"/>.
/// <para>SparseWriter 的基于回调的写入方法。</para>
/// </summary>
public static partial class SparseWriter
{
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
