namespace FirmwareKit.Sparse.IO;

using FirmwareKit.Sparse.Core;
using FirmwareKit.Sparse.Models;
using System.Collections.Generic;

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
    /// Splits the current sparse file into multiple sparse files, each not exceeding the specified maximum size.
    /// </summary>
    public static IEnumerable<SparseFile> Resparse(SparseFile sparseFile, long maxFileSize)
    {
        long overhead = SparseFormat.SparseHeaderSize + (2 * SparseFormat.ChunkHeaderSize) + 4;

        if (maxFileSize <= overhead)
        {
            throw new ArgumentException($"maxFileSize must be greater than the infrastructure overhead ({overhead} bytes)");
        }

        var fileLimit = maxFileSize - overhead;
        using IEnumerator<ResparseEntry> enumerator = GetResparseEntriesEnumerator(sparseFile);
        if (!enumerator.MoveNext())
        {
            SparseFile emptyFile = CreateNewSparseForResparse(sparseFile);
            emptyFile.Header = emptyFile.Header with { TotalBlocks = sparseFile.Header.TotalBlocks };
            if (sparseFile.Header.TotalBlocks > 0)
            {
                emptyFile.AddChunkRaw(CreateDontCareChunk(sparseFile.Header.TotalBlocks));
            }
            FinishCurrentResparseFile(emptyFile);
            yield return emptyFile;
            yield break;
        }

        SparseFile currentFile = CreateNewSparseForResparse(sparseFile);
        currentFile.Header = currentFile.Header with { TotalBlocks = sparseFile.Header.TotalBlocks };
        long fileLen = 0;              // bytes already consumed in this sparse file (including headers)
        uint fileCurrentBlock = 0;     // number of blocks written to currentFile (including dont‑care gaps)
        ResparseEntry? pending = enumerator.Current;
        while (pending != null)
        {
            ResparseEntry entry = pending.Value;
            pending = null;
            uint startBlock = entry.StartBlock;

            if (startBlock > fileCurrentBlock)
            {
                uint gap = startBlock - fileCurrentBlock;
                currentFile.AddChunkRaw(CreateDontCareChunk(gap));
                fileLen += SparseFormat.ChunkHeaderSize;
                fileCurrentBlock = startBlock;
            }

            long chunkSize = GetSparseChunkSize(sparseFile, entry.Chunk);

            if (fileLen + chunkSize > fileLimit)
            {
                // Can we fit part of this chunk into the current file?
                // We only try to split Raw chunks (Fill is 4 bytes, usually not worth splitting)
                bool canSplitData = entry.Chunk.Header.ChunkType == (ushort)ChunkType.Raw;

                long currentFileLenWithHeader = fileLen + SparseFormat.ChunkHeaderSize;
                long availableForData = fileLimit - currentFileLenWithHeader;
                bool canSplit = canSplitData && (fileCurrentBlock == startBlock || availableForData > (fileLimit / 8));

                if (canSplit)
                {
                    var blocksToTake = availableForData > 0
                        ? (uint)(availableForData / sparseFile.Header.BlockSize)
                        : 0u;

                    if (blocksToTake > 0 && blocksToTake < entry.Chunk.Header.ChunkSize)
                    {
                        (SparseChunk? part1, SparseChunk? part2) = SplitChunkInternal(sparseFile, entry.Chunk, blocksToTake);
                        currentFile.AddChunkRaw(part1);
                        fileLen += GetSparseChunkSize(sparseFile, part1);
                        fileCurrentBlock += part1.Header.ChunkSize;

                        FinishCurrentResparseFile(currentFile);
                        yield return currentFile;

                        currentFile = CreateNewSparseForResparse(sparseFile);
                        currentFile.Header = currentFile.Header with { TotalBlocks = sparseFile.Header.TotalBlocks };
                        fileLen = 0;
                        fileCurrentBlock = startBlock + part1.Header.ChunkSize;

                        pending = new ResparseEntry(fileCurrentBlock, part2);
                        continue;
                    }
                }

                if (fileLen == 0)
                {
                    throw new InvalidOperationException("Cannot fit chunk into SparseFile, please increase maxFileSize.");
                }

                FinishCurrentResparseFile(currentFile);
                yield return currentFile;
                currentFile = CreateNewSparseForResparse(sparseFile);
                currentFile.Header = currentFile.Header with { TotalBlocks = sparseFile.Header.TotalBlocks };
                fileLen = 0;
                fileCurrentBlock = startBlock;
                pending = entry;
                continue;
            }

            currentFile.AddChunkRaw(entry.Chunk);
            fileLen += chunkSize;
            fileCurrentBlock += entry.Chunk.Header.ChunkSize;

            if (enumerator.MoveNext())
            {
                pending = enumerator.Current;
            }
        }

        FinishCurrentResparseFile(currentFile);
        yield return currentFile;
    }

    private static (SparseChunk First, SparseChunk Second) SplitChunkInternal(SparseFile sparseFile, SparseChunk chunk, uint blocksToTake)
    {
        ChunkHeader h1 = chunk.Header with { ChunkSize = blocksToTake };
        ChunkHeader h2 = chunk.Header with { ChunkSize = chunk.Header.ChunkSize - blocksToTake };

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

    // enumerates all raw/fill chunks along with their starting block in the
    // source sparse file.  using an iterator avoids allocating a list of entries
    // when the file contains many chunks, keeping the peak memory usage low.
    private static IEnumerator<ResparseEntry> GetResparseEntriesEnumerator(SparseFile sparseFile)
    {
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
                    yield return new ResparseEntry(currentBlock, chunk);
                    break;
                case (ushort)ChunkType.DontCare:
                    // We don't yield DontCare as chunks, but we need to track the current block
                    break;
            }

            currentBlock += chunk.Header.ChunkSize;
        }
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
        SparseFile file = CreateNewSparseForResparse(source);
        file.Header = file.Header with { TotalBlocks = source.Header.TotalBlocks };

        uint currentBlock = 0;
        for (var i = startIndex; i <= endIndex; i++)
        {
            ResparseEntry entry = entries[i];
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
