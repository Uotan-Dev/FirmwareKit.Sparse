namespace FirmwareKit.Sparse.IO;

using FirmwareKit.Sparse.Core;
using FirmwareKit.Sparse.Models;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Provides logic for splitting a sparse file into multiple smaller sparse files.
/// </summary>
public static class SparseResparser
{
    private sealed class ResparseEntry
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
    /// Splits the current sparse file into multiple sparse files, each not exceeding the specified maximum size.
    /// </summary>
    public static List<SparseFile> Resparse(SparseFile sparseFile, long maxFileSize)
    {
        var result = new List<SparseFile>();
        long overhead = SparseFormat.SparseHeaderSize + (2 * SparseFormat.ChunkHeaderSize) + 4;

        if (maxFileSize <= overhead)
        {
            throw new ArgumentException($"maxFileSize must be greater than the infrastructure overhead ({overhead} bytes)");
        }

        var fileLimit = maxFileSize - overhead;
        var entries = BuildResparseEntries(sparseFile);

        if (entries.Count == 0)
        {
            var emptyFile = CreateNewSparseForResparse(sparseFile);
            emptyFile.Header = emptyFile.Header with { TotalBlocks = sparseFile.Header.TotalBlocks };
            if (sparseFile.Header.TotalBlocks > 0)
            {
                emptyFile.AddChunkRaw(CreateDontCareChunk(sparseFile.Header.TotalBlocks));
            }
            FinishCurrentResparseFile(emptyFile);
            result.Add(emptyFile);
            return result;
        }

        var startIndex = 0;
        while (startIndex < entries.Count)
        {
            long fileLen = 0;
            uint lastBlock = 0;
            var lastIncludedIndex = -1;

            for (var i = startIndex; i < entries.Count; i++)
            {
                var entry = entries[i];
                var count = GetSparseChunkSize(sparseFile, entry.Chunk);
                if (entry.StartBlock > lastBlock)
                {
                    count += SparseFormat.ChunkHeaderSize;
                }

                lastBlock = entry.StartBlock + entry.Chunk.Header.ChunkSize;

                if (fileLen + count > fileLimit)
                {
                    fileLen += SparseFormat.ChunkHeaderSize;
                    var availableForData = fileLimit - fileLen;
                    var canSplit = lastIncludedIndex < 0 || availableForData > (fileLimit / 8);

                    if (canSplit)
                    {
                        var blocksToTake = availableForData > 0
                            ? (uint)(availableForData / sparseFile.Header.BlockSize)
                            : 0u;

                        if (blocksToTake > 0 && blocksToTake < entry.Chunk.Header.ChunkSize)
                        {
                            var (part1, part2) = SplitChunkInternal(sparseFile, entry.Chunk, blocksToTake);
                            entries[i] = new ResparseEntry(entry.StartBlock, part1);
                            entries.Insert(i + 1, new ResparseEntry(entry.StartBlock + blocksToTake, part2));
                            lastIncludedIndex = i;
                        }
                    }

                    break;
                }

                fileLen += count;
                lastIncludedIndex = i;
            }

            if (lastIncludedIndex < startIndex)
            {
                throw new InvalidOperationException("Cannot fit chunk into SparseFile, please increase maxFileSize.");
            }

            var currentFile = BuildResparseFile(sparseFile, entries, startIndex, lastIncludedIndex);
            result.Add(currentFile);
            startIndex = lastIncludedIndex + 1;
        }

        return result;
    }

    private static (SparseChunk First, SparseChunk Second) SplitChunkInternal(SparseFile sparseFile, SparseChunk chunk, uint blocksToTake)
    {
        var h1 = chunk.Header with { ChunkSize = blocksToTake };
        var h2 = chunk.Header with { ChunkSize = chunk.Header.ChunkSize - blocksToTake };

        if (chunk.Header.ChunkType == (ushort)ChunkType.Raw)
        {
            h1 = h1 with { TotalSize = (uint)(SparseFormat.ChunkHeaderSize + ((long)blocksToTake * sparseFile.Header.BlockSize)) };
            h2 = h2 with { TotalSize = (uint)(SparseFormat.ChunkHeaderSize + ((long)h2.ChunkSize * sparseFile.Header.BlockSize)) };
        }
        else if (chunk.Header.ChunkType == (ushort)ChunkType.Fill)
        {
            h1 = h1 with { TotalSize = SparseFormat.ChunkHeaderSize + 4 };
            h2 = h2 with { TotalSize = SparseFormat.ChunkHeaderSize + 4 };
        }
        else
        {
            h1 = h1 with { TotalSize = SparseFormat.ChunkHeaderSize };
            h2 = h2 with { TotalSize = SparseFormat.ChunkHeaderSize };
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
        file.Header = file.Header with
        {
            TotalChunks = (uint)file.Chunks.Count,
            TotalBlocks = (uint)file.Chunks.Sum(c => (long)c.Header.ChunkSize)
        };
    }

    private static List<ResparseEntry> BuildResparseEntries(SparseFile sparseFile)
    {
        var entries = new List<ResparseEntry>();
        uint currentBlock = 0;

        foreach (var chunk in sparseFile.Chunks)
        {
            switch (chunk.Header.ChunkType)
            {
                case (ushort)ChunkType.Raw:
                case (ushort)ChunkType.Fill:
                    entries.Add(new ResparseEntry(currentBlock, chunk));
                    break;
                default:
                    break;
            }

            currentBlock += chunk.Header.ChunkSize;
        }

        return entries;
    }

    private static long GetSparseChunkSize(SparseFile sparseFile, SparseChunk chunk)
    {
        return chunk.Header.ChunkType switch
        {
            (ushort)ChunkType.Raw => SparseFormat.ChunkHeaderSize + ((long)chunk.Header.ChunkSize * sparseFile.Header.BlockSize),
            (ushort)ChunkType.Fill => SparseFormat.ChunkHeaderSize + 4,
            _ => SparseFormat.ChunkHeaderSize
        };
    }

    private static SparseChunk CreateDontCareChunk(uint blocks)
    {
        return new SparseChunk(new ChunkHeader
        {
            ChunkType = (ushort)ChunkType.DontCare,
            Reserved = 0,
            ChunkSize = blocks,
            TotalSize = SparseFormat.ChunkHeaderSize
        });
    }

    private static SparseFile BuildResparseFile(SparseFile source, List<ResparseEntry> entries, int startIndex, int endIndex)
    {
        var file = CreateNewSparseForResparse(source);
        file.Header = file.Header with { TotalBlocks = source.Header.TotalBlocks };

        uint currentBlock = 0;
        for (var i = startIndex; i <= endIndex; i++)
        {
            var entry = entries[i];
            if (entry.StartBlock > currentBlock)
            {
                file.AddChunkRaw(CreateDontCareChunk(entry.StartBlock - currentBlock));
            }

            file.AddChunkRaw(entry.Chunk);
            currentBlock = entry.StartBlock + entry.Chunk.Header.ChunkSize;
        }

        if (currentBlock < source.Header.TotalBlocks)
        {
            file.AddChunkRaw(CreateDontCareChunk(source.Header.TotalBlocks - currentBlock));
        }

        FinishCurrentResparseFile(file);
        return file;
    }
}
