using System.IO.MemoryMappedFiles;

namespace FirmwareKit.Sparse.IO;

/// <summary>
/// Provides logic for splitting a sparse file into multiple smaller sparse files.
/// Supports both in-memory and streaming (memory-efficient) resparsing modes.
/// <para>提供将稀疏文件拆分为多个较小稀疏文件的逻辑。
/// 支持内存中和流式（内存高效）两种重解析模式。</para>
/// </summary>
public static class SparseResparser
{
    private readonly struct ResparseEntry
    {
        public ResparseEntry(uint startBlock, SparseChunk chunk)
        {
            StartBlock = startBlock;
            Chunk = chunk;
        }

        public uint StartBlock { get; }
        public SparseChunk Chunk { get; }
    }

    private class ResparseContext
    {
        public SparseFile CurrentFile;
        public long FileLen;
        public uint FileCurrentBlock;
        public bool AnyChunkAdded;
        public bool RawExportStartSet;

        public ResparseContext(SparseHeader header, uint totalBlocks)
        {
            CurrentFile = CreateNewSparse(header);
            CurrentFile.Header = CurrentFile.Header with { TotalBlocks = totalBlocks };
            CurrentFile.RawExportStartBlock = 0;
            FileLen = 0;
            FileCurrentBlock = 0;
            AnyChunkAdded = false;
            RawExportStartSet = false;
        }

        public void Reset(SparseHeader header, uint totalBlocks)
        {
            CurrentFile = CreateNewSparse(header);
            CurrentFile.Header = CurrentFile.Header with { TotalBlocks = totalBlocks };
            CurrentFile.RawExportStartBlock = null;
            CurrentFile.RawExportTotalBlocks = null;
            FileLen = 0;
            FileCurrentBlock = 0;
            AnyChunkAdded = false;
            RawExportStartSet = false;
        }
    }

    #region Public API

    /// <summary>
    /// Splits a large <see cref="SparseFile"/> into multiple smaller sparse files,
    /// each not exceeding <paramref name="maxFileSize"/>.
    /// <para>将大型 SparseFile 拆分为多个较小的稀疏文件，每个文件不超过 maxFileSize。</para>
    /// </summary>
    /// <param name="sparseFile">Source <see cref="SparseFile"/> to split. <para>要拆分的源 SparseFile。</para></param>
    /// <param name="maxFileSize">Maximum allowed size in bytes for each output file. <para>每个输出文件的最大允许字节数。</para></param>
    /// <returns>An enumerable of <see cref="SparseFile"/> instances representing the split parts. <para>表示拆分部分的 SparseFile 实例的可枚举集合。</para></returns>
    public static IEnumerable<SparseFile> Resparse(SparseFile sparseFile, long maxFileSize)
    {
        var header = sparseFile.Header;
        var fileLimit = ValidateAndComputeFileLimit(header, maxFileSize);

        var entries = CollectEntriesFromSparseFile(sparseFile);
        NormalizeChunkBoundaries(header, entries);

        if (entries.Count == 0)
        {
            yield return CreateEmptyResparseFile(header);
            yield break;
        }

        foreach (var file in ProcessEntries(entries, header, fileLimit))
        {
            yield return file;
        }
    }

    /// <summary>
    /// Splits a sparse file from stream into multiple smaller sparse files using streaming parsing.
    /// Optimized for 32-bit AOT environments handling large files (up to 16GB).
    /// <para>使用流式解析将稀疏文件从流拆分为多个较小的稀疏文件。
    /// 针对 32 位 AOT 环境处理大文件（最大 16GB）进行了优化。</para>
    /// </summary>
    /// <param name="stream">Stream containing the sparse file data. <para>包含稀疏文件数据的流。</para></param>
    /// <param name="maxFileSize">Maximum allowed size for each output file. <para>每个输出文件的最大允许大小。</para></param>
    /// <param name="leaveOpen">Whether to leave the stream open after processing. <para>处理完成后是否保持流打开。</para></param>
    /// <returns>An enumerable of <see cref="SparseFile"/> instances representing the split parts. <para>表示拆分部分的 SparseFile 实例的可枚举集合。</para></returns>
    public static IEnumerable<SparseFile> ResparseStreamed(Stream stream, long maxFileSize, bool leaveOpen = false)
    {
        using var parser = new SparseStreamParser(stream, leaveOpen);
        var header = parser.Header;
        var fileLimit = ValidateAndComputeFileLimit(header, maxFileSize);

        const int BatchSize = 1000;
        var currentBatch = new List<ResparseEntry>(BatchSize);
        uint currentBlock = 0;

        foreach (var chunk in parser.EnumerateChunks())
        {
            if (chunk.StartBlock > currentBlock)
            {
                currentBlock = chunk.StartBlock;
            }

            if (chunk.Header.ChunkType is (ushort)ChunkType.Raw or (ushort)ChunkType.Fill)
            {
                currentBatch.Add(new ResparseEntry(currentBlock, chunk));
            }

            currentBlock += chunk.Header.ChunkSize;

            if (currentBatch.Count >= BatchSize)
            {
                foreach (var file in ProcessBatch(currentBatch, header, fileLimit))
                {
                    yield return file;
                }
                currentBatch.Clear();
            }
        }

        if (currentBatch.Count > 0)
        {
            foreach (var file in ProcessBatch(currentBatch, header, fileLimit))
            {
                yield return file;
            }
        }
    }

    /// <summary>
    /// Splits a sparse file from disk into multiple smaller sparse files using memory-mapped I/O.
    /// Optimized for 32-bit AOT environments handling large files (up to 16GB).
    /// <para>使用内存映射 I/O 将磁盘上的稀疏文件拆分为多个较小的稀疏文件。
    /// 针对 32 位 AOT 环境处理大文件（最大 16GB）进行了优化。</para>
    /// </summary>
    /// <param name="filePath">Path to the sparse file. <para>稀疏文件的路径。</para></param>
    /// <param name="maxFileSize">Maximum allowed size for each output file. <para>每个输出文件的最大允许大小。</para></param>
    /// <returns>An enumerable of <see cref="SparseFile"/> instances representing the split parts. <para>表示拆分部分的 SparseFile 实例的可枚举集合。</para></returns>
    public static IEnumerable<SparseFile> ResparseMapped(string filePath, long maxFileSize)
    {
        using var mmf = MemoryMappedFile.CreateFromFile(
            filePath,
            FileMode.Open,
            null,
            0,
            MemoryMappedFileAccess.Read);

        using var stream = mmf.CreateViewStream();
        foreach (var file in ResparseStreamed(stream, maxFileSize))
        {
            yield return file;
        }
    }

    #endregion

    #region Entry Collection

    private static List<ResparseEntry> CollectEntriesFromSparseFile(SparseFile sparseFile)
    {
        var entries = new List<ResparseEntry>(sparseFile.Chunks.Count);
        uint currentBlock = 0;
        foreach (var chunk in sparseFile.Chunks)
        {
            if (chunk.StartBlock > currentBlock)
            {
                currentBlock = chunk.StartBlock;
            }

            if (chunk.Header.ChunkType is (ushort)ChunkType.Raw or (ushort)ChunkType.Fill)
            {
                entries.Add(new ResparseEntry(currentBlock, chunk));
            }

            currentBlock += chunk.Header.ChunkSize;
        }
        return entries;
    }

    #endregion

    #region Chunk Normalization

    private static void NormalizeChunkBoundaries(SparseHeader header, List<ResparseEntry> entries)
    {
        if (entries.Count == 0 || header.BlockSize == 0)
        {
            return;
        }

        uint maxBlocksPerChunk = (uint)(SparseFormat.MaxChunkDataSize / header.BlockSize);
        if (maxBlocksPerChunk == 0)
        {
            return;
        }

        var newEntries = new List<ResparseEntry>(entries.Count * 2);

        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (!ChunkHelper.IsSplittableChunkType(entry.Chunk.Header.ChunkType))
            {
                newEntries.Add(entry);
                continue;
            }

            if ((long)entry.Chunk.Header.ChunkSize * header.BlockSize <= SparseFormat.MaxChunkDataSize)
            {
                newEntries.Add(entry);
                continue;
            }

            if (entry.Chunk.Header.ChunkSize <= maxBlocksPerChunk)
            {
                newEntries.Add(entry);
                continue;
            }

            uint remainingBlocks = entry.Chunk.Header.ChunkSize;
            uint currentStart = entry.StartBlock;

            while (remainingBlocks > maxBlocksPerChunk)
            {
                var (part1, part2) = SplitChunk(entry.Chunk, maxBlocksPerChunk, header);
                part1.StartBlock = currentStart;
                newEntries.Add(new ResparseEntry(currentStart, part1));
                currentStart += part1.Header.ChunkSize;
                remainingBlocks -= maxBlocksPerChunk;
                entry = new ResparseEntry(currentStart, part2);
            }

            if (remainingBlocks > 0)
            {
                newEntries.Add(new ResparseEntry(currentStart, entry.Chunk));
            }
        }

        entries.Clear();
        entries.AddRange(newEntries);
    }

    #endregion

    #region Core Processing

    private static IEnumerable<SparseFile> ProcessEntries(List<ResparseEntry> entries, SparseHeader header, long fileLimit)
    {
        var ctx = new ResparseContext(header, header.TotalBlocks);

        for (int index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            uint startBlock = entry.StartBlock;

            if (startBlock > ctx.FileCurrentBlock)
            {
                uint gap = startBlock - ctx.FileCurrentBlock;
                ctx.CurrentFile.AddChunkRaw(CreateDontCareChunk(gap, header.ChunkHeaderSize, ctx.FileCurrentBlock));
                ctx.FileLen += header.ChunkHeaderSize;
                ctx.FileCurrentBlock = startBlock;
            }

            long chunkSize = ChunkHelper.GetDiskSize(entry.Chunk, header);

            if (ctx.FileLen + chunkSize > fileLimit)
            {
                if (TrySplitAndAppend(ctx, entry, header, fileLimit, ref index, entries, out var completedFile))
                {
                    yield return completedFile;
                    continue;
                }

                if (ctx.FileLen == 0)
                {
                    throw new InvalidOperationException("Cannot fit chunk into SparseFile, please increase maxFileSize.");
                }

                FinalizeFile(ctx.CurrentFile);
                ctx.CurrentFile.Header = ctx.CurrentFile.Header with { TotalBlocks = header.TotalBlocks };
                yield return ctx.CurrentFile;
                ctx.Reset(header, header.TotalBlocks);
                index--;
                continue;
            }

            AppendChunkToContext(ctx, entry, header);
        }

        FinalizeFile(ctx.CurrentFile);
        ctx.CurrentFile.Header = ctx.CurrentFile.Header with { TotalBlocks = header.TotalBlocks };
        yield return ctx.CurrentFile;
    }

    private static IEnumerable<SparseFile> ProcessBatch(List<ResparseEntry> entries, SparseHeader header, long fileLimit)
    {
        if (entries.Count == 0)
            yield break;

        entries.Sort((a, b) => a.StartBlock.CompareTo(b.StartBlock));

        var ctx = new ResparseContext(header, header.TotalBlocks);

        for (int index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            uint startBlock = entry.StartBlock;

            if (startBlock > ctx.FileCurrentBlock)
            {
                uint gap = startBlock - ctx.FileCurrentBlock;
                ctx.CurrentFile.AddChunkRaw(CreateDontCareChunk(gap, header.ChunkHeaderSize, ctx.FileCurrentBlock));
                ctx.FileLen += header.ChunkHeaderSize;
                ctx.FileCurrentBlock = startBlock;
            }

            long chunkSize = ChunkHelper.GetDiskSize(entry.Chunk, header);

            if (ctx.FileLen + chunkSize > fileLimit)
            {
                if (TrySplitAndAppendBatch(ctx, entry, header, fileLimit, ref index, entries, out var completedFile))
                {
                    yield return completedFile;
                    continue;
                }

                if (ctx.FileLen == 0)
                {
                    throw new InvalidOperationException("Cannot fit chunk into SparseFile, please increase maxFileSize.");
                }

                FinalizeFile(ctx.CurrentFile);
                yield return ctx.CurrentFile;
                ctx.Reset(header, header.TotalBlocks);
                index--;
                continue;
            }

            AppendChunkToContext(ctx, entry, header);
        }

        FinalizeFile(ctx.CurrentFile);
        yield return ctx.CurrentFile;
    }

    private static bool TrySplitAndAppend(
        ResparseContext ctx, ResparseEntry entry, SparseHeader header, long fileLimit,
        ref int index, List<ResparseEntry> entries, out SparseFile completedFile)
    {
        completedFile = null!;

        if (!ChunkHelper.IsSplittableChunkType(entry.Chunk.Header.ChunkType))
            return false;

        long availableForData = fileLimit - (ctx.FileLen + header.ChunkHeaderSize);
        if (ctx.AnyChunkAdded && availableForData <= fileLimit / 8)
            return false;

        var blocksToTake = availableForData > 0
            ? (uint)(availableForData / header.BlockSize)
            : 0u;

        if (blocksToTake == 0 || blocksToTake >= entry.Chunk.Header.ChunkSize)
            return false;

        var (part1, part2) = SplitChunk(entry.Chunk, blocksToTake, header);
        part1.StartBlock = entry.StartBlock;
        ctx.CurrentFile.AddChunkRaw(part1);
        ctx.AnyChunkAdded = true;
        ctx.FileLen += ChunkHelper.GetDiskSize(part1, header);
        ctx.FileCurrentBlock += part1.Header.ChunkSize;

        FinalizeFile(ctx.CurrentFile);
        ctx.CurrentFile.Header = ctx.CurrentFile.Header with { TotalBlocks = header.TotalBlocks };
        completedFile = ctx.CurrentFile;

        ctx.Reset(header, header.TotalBlocks);
        entries.Insert(index + 1, new ResparseEntry(entry.StartBlock + part1.Header.ChunkSize, part2));
        return true;
    }

    private static bool TrySplitAndAppendBatch(
        ResparseContext ctx, ResparseEntry entry, SparseHeader header, long fileLimit,
        ref int index, List<ResparseEntry> entries, out SparseFile completedFile)
    {
        completedFile = null!;

        if (!ChunkHelper.IsSplittableChunkType(entry.Chunk.Header.ChunkType))
            return false;

        long availableForData = fileLimit - (ctx.FileLen + header.ChunkHeaderSize);
        if (ctx.AnyChunkAdded && availableForData <= fileLimit / 8)
            return false;

        var blocksToTake = availableForData > 0
            ? (uint)(availableForData / header.BlockSize)
            : 0u;

        if (blocksToTake == 0 || blocksToTake >= entry.Chunk.Header.ChunkSize)
            return false;

        var (part1, part2) = SplitChunk(entry.Chunk, blocksToTake, header);
        part1.StartBlock = entry.StartBlock;
        ctx.CurrentFile.AddChunkRaw(part1);
        ctx.AnyChunkAdded = true;
        ctx.FileLen += ChunkHelper.GetDiskSize(part1, header);
        ctx.FileCurrentBlock += part1.Header.ChunkSize;

        FinalizeFile(ctx.CurrentFile);
        completedFile = ctx.CurrentFile;

        ctx.Reset(header, header.TotalBlocks);
        entries[index] = new ResparseEntry(entry.StartBlock + part1.Header.ChunkSize, part2);
        return true;
    }

    private static void AppendChunkToContext(ResparseContext ctx, ResparseEntry entry, SparseHeader header)
    {
        var cloned = CloneChunk(entry.Chunk, entry.StartBlock);
        ctx.CurrentFile.AddChunkRaw(cloned);
        if (!ctx.RawExportStartSet && cloned.Header.ChunkType is (ushort)ChunkType.Raw or (ushort)ChunkType.Fill)
        {
            ctx.CurrentFile.RawExportStartBlock = entry.StartBlock;
            ctx.RawExportStartSet = true;
        }
        ctx.AnyChunkAdded = true;
        ctx.FileLen += ChunkHelper.GetDiskSize(cloned, header);
        ctx.FileCurrentBlock += cloned.Header.ChunkSize;
    }

    #endregion

    #region Utility Methods

    private static long ValidateAndComputeFileLimit(SparseHeader header, long maxFileSize)
    {
        var overhead = header.FileHeaderSize + (2 * header.ChunkHeaderSize) + 4;

        if (maxFileSize <= overhead)
        {
            throw new ArgumentException(
                $"maxFileSize must be greater than the infrastructure overhead ({overhead} bytes)");
        }

        return maxFileSize - overhead;
    }

    private static SparseFile CreateEmptyResparseFile(SparseHeader header)
    {
        var emptyFile = CreateNewSparse(header);
        emptyFile.Header = emptyFile.Header with { TotalBlocks = header.TotalBlocks };
        if (header.TotalBlocks > 0)
        {
            emptyFile.AddChunkRaw(CreateDontCareChunk(header.TotalBlocks, header.ChunkHeaderSize));
        }
        FinalizeFile(emptyFile);
        return emptyFile;
    }

    private static SparseFile CreateNewSparse(SparseHeader header)
    {
        return new SparseFile
        {
            Header = new SparseHeader
            {
                Magic = SparseFormat.SparseHeaderMagic,
                MajorVersion = header.MajorVersion,
                MinorVersion = header.MinorVersion,
                FileHeaderSize = header.FileHeaderSize,
                ChunkHeaderSize = header.ChunkHeaderSize,
                BlockSize = header.BlockSize
            }
        };
    }

    private static void FinalizeFile(SparseFile file)
    {
        uint totalChunks = 0;
        uint totalBlocks = 0;
        foreach (SparseChunk chunk in file.Chunks)
        {
            totalChunks++;
            totalBlocks += chunk.Header.ChunkSize;
        }

        file.Header = file.Header with
        {
            TotalChunks = totalChunks,
            TotalBlocks = totalBlocks
        };
        file.RawExportTotalBlocks = totalBlocks;
    }

    private static SparseChunk CreateDontCareChunk(uint blocks, ushort chunkHeaderSize, uint startBlock = 0)
    {
        var chunk = new SparseChunk(new ChunkHeader
        {
            ChunkType = (ushort)ChunkType.DontCare,
            Reserved = 0,
            ChunkSize = blocks,
            TotalSize = chunkHeaderSize
        });
        chunk.StartBlock = startBlock;
        return chunk;
    }

    private static SparseChunk CloneChunk(SparseChunk src, uint startBlock)
    {
        var clone = new SparseChunk(src.Header) { StartBlock = startBlock };
        if (src.Header.ChunkType == (ushort)ChunkType.Raw && src.DataProvider != null)
        {
            clone.DataProvider = src.DataProvider.GetSubProvider(0, src.DataProvider.Length);
        }
        else if (src.Header.ChunkType == (ushort)ChunkType.Fill)
        {
            clone.FillValue = src.FillValue;
        }
        return clone;
    }

    private static (SparseChunk First, SparseChunk Second) SplitChunk(SparseChunk chunk, uint blocksToTake, SparseHeader header)
    {
        ChunkHeader h1 = chunk.Header with { ChunkSize = blocksToTake };
        ChunkHeader h2 = chunk.Header with { ChunkSize = chunk.Header.ChunkSize - blocksToTake };

        h1 = h1 with { TotalSize = ChunkHelper.GetExpectedTotalSize(h1.ChunkType, h1.ChunkSize, header.ChunkHeaderSize, header.BlockSize) };
        h2 = h2 with { TotalSize = ChunkHelper.GetExpectedTotalSize(h2.ChunkType, h2.ChunkSize, header.ChunkHeaderSize, header.BlockSize) };

        var part1 = new SparseChunk(h1);
        var part2 = new SparseChunk(h2);

        if (chunk.Header.ChunkType == (ushort)ChunkType.Raw && chunk.DataProvider != null)
        {
            part1.DataProvider = chunk.DataProvider.GetSubProvider(0, (long)blocksToTake * header.BlockSize);
            part2.DataProvider = chunk.DataProvider.GetSubProvider((long)blocksToTake * header.BlockSize, (long)h2.ChunkSize * header.BlockSize);
        }
        else if (chunk.Header.ChunkType == (ushort)ChunkType.Fill)
        {
            part1.FillValue = chunk.FillValue;
            part2.FillValue = chunk.FillValue;
        }

        return (part1, part2);
    }

    #endregion
}
