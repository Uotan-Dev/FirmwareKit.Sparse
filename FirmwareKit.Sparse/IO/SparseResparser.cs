namespace FirmwareKit.Sparse.IO;

using FirmwareKit.Sparse.Core;
using FirmwareKit.Sparse.Models;
using FirmwareKit.Sparse.DataProviders;
using System.Collections.Generic;
using System.Buffers;

/// <summary>
/// Provides logic for splitting a sparse file into multiple smaller sparse files.
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

    /// <summary>
    /// Split a large <see cref="SparseFile"/> into multiple smaller sparse files, each not exceeding <paramref name="maxFileSize"/>.
    /// Memory-optimized version for 32-bit AOT environments.
    /// </summary>
    /// <param name="sparseFile">Source <see cref="SparseFile"/> to split.</param>
    /// <param name="maxFileSize">Maximum allowed size in bytes for each output file (long).</param>
    /// <returns>An enumerable of <see cref="SparseFile"/> instances representing the split parts.</returns>
    public static IEnumerable<SparseFile> Resparse(SparseFile sparseFile, long maxFileSize)
    {
        var overhead = sparseFile.Header.FileHeaderSize + (2 * sparseFile.Header.ChunkHeaderSize) + 4;

        if (maxFileSize <= overhead)
        {
            throw new ArgumentException($"maxFileSize must be greater than the infrastructure overhead ({overhead} bytes)");
        }

        var fileLimit = maxFileSize - overhead;

        var estimatedEntries = sparseFile.Chunks.Count;
        var entries = new List<ResparseEntry>(estimatedEntries);
        uint currentBlock = 0;
        foreach (SparseChunk chunk in sparseFile.Chunks)
        {
            if (chunk.StartBlock > currentBlock)
            {
                currentBlock = chunk.StartBlock;
            }

            switch (chunk.Header.ChunkType)
            {
                case (ushort)ChunkType.Raw:
                case (ushort)ChunkType.Fill:
                    entries.Add(new ResparseEntry(currentBlock, chunk));
                    break;
                case (ushort)ChunkType.DontCare:
                    break;
            }

            currentBlock += chunk.Header.ChunkSize;
        }

        NormalizeChunkBoundaries(sparseFile, entries);

        if (entries.Count == 0)
        {
            SparseFile emptyFile = CreateNewSparseForResparse(sparseFile);
            emptyFile.Header = emptyFile.Header with { TotalBlocks = sparseFile.Header.TotalBlocks };
            if (sparseFile.Header.TotalBlocks > 0)
            {
                emptyFile.AddChunkRaw(CreateDontCareChunk(sparseFile.Header.TotalBlocks, emptyFile.Header.ChunkHeaderSize));
            }
            FinishCurrentResparseFile(emptyFile);
            yield return emptyFile;
            yield break;
        }

        SparseFile currentFile = CreateNewSparseForResparse(sparseFile);
        currentFile.Header = currentFile.Header with { TotalBlocks = sparseFile.Header.TotalBlocks };
        currentFile.RawExportStartBlock = 0;
        long fileLen = 0;
        uint fileCurrentBlock = 0;
        bool anyChunkAdded = false;
        bool rawExportStartSet = false;

        for (int index = 0; index < entries.Count; index++)
        {
            ResparseEntry entry = entries[index];
            uint startBlock = entry.StartBlock;

            if (startBlock > fileCurrentBlock)
            {
                uint gap = startBlock - fileCurrentBlock;
                var gapChunk = CreateDontCareChunk(gap, currentFile.Header.ChunkHeaderSize, fileCurrentBlock);
                currentFile.AddChunkRaw(gapChunk);
                fileLen += currentFile.Header.ChunkHeaderSize;
                fileCurrentBlock = startBlock;
            }

            long chunkSize = GetSparseChunkSize(sparseFile, entry.Chunk);

            if (fileLen + chunkSize > fileLimit)
            {
                bool canSplitData = entry.Chunk.Header.ChunkType == (ushort)ChunkType.Raw
                                    || entry.Chunk.Header.ChunkType == (ushort)ChunkType.Fill;

                long currentFileLenWithHeader = fileLen + currentFile.Header.ChunkHeaderSize;
                long availableForData = fileLimit - currentFileLenWithHeader;
                bool canSplit = canSplitData && (!anyChunkAdded || availableForData > (fileLimit / 8));

                if (canSplit)
                {
                    var blocksToTake = availableForData > 0
                        ? (uint)(availableForData / sparseFile.Header.BlockSize)
                        : 0u;

                    if (blocksToTake > 0 && blocksToTake < entry.Chunk.Header.ChunkSize)
                    {
                        (SparseChunk? part1, SparseChunk? part2) = SplitChunkInternal(sparseFile, entry.Chunk, blocksToTake);
                        part1.StartBlock = startBlock;
                        currentFile.AddChunkRaw(part1);
                        anyChunkAdded = true;
                        fileLen += GetSparseChunkSize(sparseFile, part1);
                        fileCurrentBlock += part1.Header.ChunkSize;

                        FinishCurrentResparseFile(currentFile);
                        currentFile.Header = currentFile.Header with { TotalBlocks = sparseFile.Header.TotalBlocks };
                        yield return currentFile;

                        currentFile = CreateNewSparseForResparse(sparseFile);
                        currentFile.Header = currentFile.Header with { TotalBlocks = sparseFile.Header.TotalBlocks };
                        currentFile.RawExportStartBlock = null;
                        fileLen = 0;
                        fileCurrentBlock = 0;
                        anyChunkAdded = false;
                        rawExportStartSet = false;

                        entries.Insert(index + 1, new ResparseEntry(startBlock + part1.Header.ChunkSize, part2));
                        continue;
                    }
                }

                if (fileLen == 0)
                {
                    throw new InvalidOperationException("Cannot fit chunk into SparseFile, please increase maxFileSize.");
                }

                FinishCurrentResparseFile(currentFile);
                currentFile.Header = currentFile.Header with { TotalBlocks = sparseFile.Header.TotalBlocks };
                yield return currentFile;
                currentFile = CreateNewSparseForResparse(sparseFile);
                currentFile.Header = currentFile.Header with { TotalBlocks = sparseFile.Header.TotalBlocks };
                currentFile.RawExportStartBlock = null;
                fileLen = 0;
                fileCurrentBlock = 0;
                anyChunkAdded = false;
                rawExportStartSet = false;
                index--;
                continue;
            }

            var cloned = CloneChunkForResparse(entry.Chunk, startBlock);
            currentFile.AddChunkRaw(cloned);
            if (!rawExportStartSet && cloned.Header.ChunkType is (ushort)ChunkType.Raw or (ushort)ChunkType.Fill)
            {
                currentFile.RawExportStartBlock = startBlock;
                rawExportStartSet = true;
            }
            anyChunkAdded = true;
            fileLen += GetSparseChunkSize(sparseFile, cloned);
            fileCurrentBlock += cloned.Header.ChunkSize;
        }

        FinishCurrentResparseFile(currentFile);
        currentFile.Header = currentFile.Header with { TotalBlocks = sparseFile.Header.TotalBlocks };
        yield return currentFile;
    }

    private static (SparseChunk First, SparseChunk Second) SplitChunkInternal(SparseFile sparseFile, SparseChunk chunk, uint blocksToTake)
    {
        ChunkHeader h1 = chunk.Header with { ChunkSize = blocksToTake };
        ChunkHeader h2 = chunk.Header with { ChunkSize = chunk.Header.ChunkSize - blocksToTake };

        if (chunk.Header.ChunkType == (ushort)ChunkType.Raw)
        {
            h1 = h1 with { TotalSize = (uint)(sparseFile.Header.ChunkHeaderSize + ((long)blocksToTake * sparseFile.Header.BlockSize)) };
            h2 = h2 with { TotalSize = (uint)(sparseFile.Header.ChunkHeaderSize + ((long)h2.ChunkSize * sparseFile.Header.BlockSize)) };
        }
        else if (chunk.Header.ChunkType == (ushort)ChunkType.Fill)
        {
            h1 = h1 with { TotalSize = (uint)(sparseFile.Header.ChunkHeaderSize + 4) };
            h2 = h2 with { TotalSize = (uint)(sparseFile.Header.ChunkHeaderSize + 4) };
        }
        else
        {
            h1 = h1 with { TotalSize = (uint)sparseFile.Header.ChunkHeaderSize };
            h2 = h2 with { TotalSize = (uint)sparseFile.Header.ChunkHeaderSize };
        }

        var part1 = new SparseChunk(h1);
        var part2 = new SparseChunk(h2);

        if (chunk.Header.ChunkType == (ushort)ChunkType.Raw && chunk.DataProvider != null)
        {
            part1.DataProvider = chunk.DataProvider.GetSubProvider(0, (long)blocksToTake * sparseFile.Header.BlockSize);
            part2.DataProvider = chunk.DataProvider.GetSubProvider((long)blocksToTake * sparseFile.Header.BlockSize, (long)h2.ChunkSize * sparseFile.Header.BlockSize);
        }
        else if (chunk.Header.ChunkType == (ushort)ChunkType.Fill)
        {
            part1.FillValue = chunk.FillValue;
            part2.FillValue = chunk.FillValue;
        }

        return (part1, part2);
    }

    private static SparseFile CreateNewSparseForResparse(SparseFile parent)
    {
        return new SparseFile
        {
            Header = new SparseHeader
            {
                Magic = SparseFormat.SparseHeaderMagic,
                MajorVersion = parent.Header.MajorVersion,
                MinorVersion = parent.Header.MinorVersion,
                FileHeaderSize = parent.Header.FileHeaderSize,
                ChunkHeaderSize = parent.Header.ChunkHeaderSize,
                BlockSize = parent.Header.BlockSize
            }
        };
    }

    private static void FinishCurrentResparseFile(SparseFile file)
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
    }

    private static long GetSparseChunkSize(SparseFile sparseFile, SparseChunk chunk)
    {
        return chunk.Header.ChunkType switch
        {
            (ushort)ChunkType.Raw => sparseFile.Header.ChunkHeaderSize + ((long)chunk.Header.ChunkSize * sparseFile.Header.BlockSize),
            (ushort)ChunkType.Fill => sparseFile.Header.ChunkHeaderSize + 4,
            _ => sparseFile.Header.ChunkHeaderSize
        };
    }

    private static long GetLogicalChunkSize(SparseFile sparseFile, SparseChunk chunk)
    {
        return (long)chunk.Header.ChunkSize * sparseFile.Header.BlockSize;
    }

    private static void NormalizeChunkBoundaries(SparseFile sparseFile, List<ResparseEntry> entries)
    {
        if (entries.Count == 0 || sparseFile.Header.BlockSize == 0)
        {
            return;
        }

        uint maxBlocksPerChunk = (uint)(SparseFormat.MaxChunkDataSize / sparseFile.Header.BlockSize);
        if (maxBlocksPerChunk == 0)
        {
            return;
        }

        // Optimized: Build new list instead of O(n) insertions in the middle
        var newEntries = new List<ResparseEntry>(entries.Count * 2);

        for (int i = 0; i < entries.Count; i++)
        {
            ResparseEntry entry = entries[i];
            if (!CanSplitChunkForMaxDataSize(entry.Chunk))
            {
                newEntries.Add(entry);
                continue;
            }

            if (GetLogicalChunkSize(sparseFile, entry.Chunk) <= SparseFormat.MaxChunkDataSize)
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
                (SparseChunk part1, SparseChunk part2) = SplitChunkInternal(sparseFile, entry.Chunk, maxBlocksPerChunk);
                part1.StartBlock = currentStart;
                newEntries.Add(new ResparseEntry(currentStart, part1));
                currentStart += part1.Header.ChunkSize;
                remainingBlocks -= maxBlocksPerChunk;

                // Create a new chunk for the remaining part to continue splitting
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

    private static bool CanSplitChunkForMaxDataSize(SparseChunk chunk)
    {
        return chunk.Header.ChunkType == (ushort)ChunkType.Raw || chunk.Header.ChunkType == (ushort)ChunkType.Fill;
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

    private static SparseChunk CloneChunkForResparse(SparseChunk src, uint startBlock)
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
}
