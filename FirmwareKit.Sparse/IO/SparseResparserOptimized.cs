using FirmwareKit.Sparse.Core;
using FirmwareKit.Sparse.Models;
using FirmwareKit.Sparse.DataProviders;
using System.Collections.Generic;
using System.Buffers;
using System.IO.MemoryMappedFiles;

namespace FirmwareKit.Sparse.IO;

/// <summary>
/// Provides memory-efficient logic for splitting a sparse file into multiple smaller sparse files.
/// Optimized for 32-bit AOT environments handling large files (up to 16GB with hundreds of thousands of chunks).
/// </summary>
public static class SparseResparserOptimized
{
    private struct ResparseEntry
    {
        public uint StartBlock;
        public SparseChunk Chunk;

        public ResparseEntry(uint startBlock, SparseChunk chunk)
        {
            StartBlock = startBlock;
            Chunk = chunk;
        }
    }

    /// <summary>
    /// Split a large SparseFile into multiple smaller sparse files using streaming parsing.
    /// This method is optimized for memory efficiency in 32-bit AOT environments.
    /// </summary>
    /// <param name="stream">Stream containing the sparse file data</param>
    /// <param name="maxFileSize">Maximum allowed size for each output file</param>
    /// <param name="leaveOpen">Whether to leave the stream open after processing</param>
    /// <returns>An enumerable of SparseFile instances</returns>
    public static IEnumerable<SparseFile> ResparseStreamed(Stream stream, long maxFileSize, bool leaveOpen = false)
    {
        using var parser = new SparseStreamParser(stream, leaveOpen);
        foreach (var file in ResparseFromParser(parser, maxFileSize))
        {
            yield return file;
        }
    }

    /// <summary>
    /// Split a large SparseFile into multiple smaller sparse files using memory-mapped I/O.
    /// </summary>
    /// <param name="filePath">Path to the sparse file</param>
    /// <param name="maxFileSize">Maximum allowed size for each output file</param>
    /// <returns>An enumerable of SparseFile instances</returns>
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

    private static IEnumerable<SparseFile> ResparseFromParser(SparseStreamParser parser, long maxFileSize)
    {
        var header = parser.Header;
        var overhead = header.FileHeaderSize + (2 * header.ChunkHeaderSize) + 4;

        if (maxFileSize <= overhead)
        {
            throw new ArgumentException($"maxFileSize must be greater than infrastructure overhead ({overhead} bytes)");
        }

        var fileLimit = maxFileSize - overhead;

        // Process chunks in batches to avoid loading all into memory
        const int BatchSize = 1000;
        var currentBatch = new List<ResparseEntry>(BatchSize);
        uint currentBlock = 0;
        uint entryCount = 0;

        foreach (var chunk in parser.EnumerateChunks())
        {
            if (chunk.StartBlock > currentBlock)
            {
                currentBlock = chunk.StartBlock;
            }

            switch (chunk.Header.ChunkType)
            {
                case (ushort)ChunkType.Raw:
                case (ushort)ChunkType.Fill:
                    currentBatch.Add(new ResparseEntry(currentBlock, chunk));
                    break;
            }

            currentBlock += chunk.Header.ChunkSize;
            entryCount++;

            // Process batch when full
            if (currentBatch.Count >= BatchSize)
            {
                foreach (var file in ProcessBatch(currentBatch, header, fileLimit))
                {
                    yield return file;
                }
                currentBatch.Clear();
            }
        }

        // Process remaining entries
        if (currentBatch.Count > 0)
        {
            foreach (var file in ProcessBatch(currentBatch, header, fileLimit))
            {
                yield return file;
            }
        }
    }

    private static IEnumerable<SparseFile> ProcessBatch(List<ResparseEntry> entries, SparseHeader header, long fileLimit)
    {
        if (entries.Count == 0)
            yield break;

        // Sort entries by start block
        entries.Sort((a, b) => a.StartBlock.CompareTo(b.StartBlock));

        SparseFile currentFile = CreateNewSparseForResparse(header);
        currentFile.Header = currentFile.Header with { TotalBlocks = header.TotalBlocks };
        currentFile.RawExportStartBlock = 0;
        long fileLen = 0;
        uint fileCurrentBlock = 0;
        bool anyChunkAdded = false;
        bool rawExportStartSet = false;

        for (int index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            uint startBlock = entry.StartBlock;

            // Add gap chunk if needed
            if (startBlock > fileCurrentBlock)
            {
                uint gap = startBlock - fileCurrentBlock;
                var gapChunk = CreateDontCareChunk(gap, header.ChunkHeaderSize, fileCurrentBlock);
                currentFile.AddChunkRaw(gapChunk);
                fileLen += header.ChunkHeaderSize;
                fileCurrentBlock = startBlock;
            }

            long chunkSize = GetSparseChunkSize(header, entry.Chunk);

            // Check if we need to split
            if (fileLen + chunkSize > fileLimit)
            {
                bool canSplitData = entry.Chunk.Header.ChunkType == (ushort)ChunkType.Raw
                                    || entry.Chunk.Header.ChunkType == (ushort)ChunkType.Fill;

                long availableForData = fileLimit - (fileLen + header.ChunkHeaderSize);
                bool canSplit = canSplitData && (!anyChunkAdded || availableForData > (fileLimit / 8));

                if (canSplit)
                {
                    var blocksToTake = availableForData > 0
                        ? (uint)(availableForData / header.BlockSize)
                        : 0u;

                    if (blocksToTake > 0 && blocksToTake < entry.Chunk.Header.ChunkSize)
                    {
                        var (part1, part2) = SplitChunk(entry.Chunk, blocksToTake, header);
                        part1.StartBlock = startBlock;
                        currentFile.AddChunkRaw(part1);
                        anyChunkAdded = true;
                        fileLen += GetSparseChunkSize(header, part1);
                        fileCurrentBlock += part1.Header.ChunkSize;

                        FinishCurrentResparseFile(currentFile);
                        yield return currentFile;

                        currentFile = CreateNewSparseForResparse(header);
                        currentFile.Header = currentFile.Header with { TotalBlocks = header.TotalBlocks };
                        currentFile.RawExportStartBlock = null;
                        fileLen = 0;
                        fileCurrentBlock = 0;
                        anyChunkAdded = false;
                        rawExportStartSet = false;

                        // Replace current entry with the remaining part
                        entries[index] = new ResparseEntry(startBlock + part1.Header.ChunkSize, part2);
                        continue;
                    }
                }

                if (fileLen == 0)
                {
                    throw new InvalidOperationException("Cannot fit chunk into SparseFile, please increase maxFileSize.");
                }

                FinishCurrentResparseFile(currentFile);
                yield return currentFile;
                currentFile = CreateNewSparseForResparse(header);
                currentFile.Header = currentFile.Header with { TotalBlocks = header.TotalBlocks };
                currentFile.RawExportStartBlock = null;
                fileLen = 0;
                fileCurrentBlock = 0;
                anyChunkAdded = false;
                rawExportStartSet = false;
                index--;
                continue;
            }

            var cloned = CloneChunk(entry.Chunk, startBlock);
            currentFile.AddChunkRaw(cloned);
            if (!rawExportStartSet && cloned.Header.ChunkType is (ushort)ChunkType.Raw or (ushort)ChunkType.Fill)
            {
                currentFile.RawExportStartBlock = startBlock;
                rawExportStartSet = true;
            }
            anyChunkAdded = true;
            fileLen += GetSparseChunkSize(header, cloned);
            fileCurrentBlock += cloned.Header.ChunkSize;
        }

        FinishCurrentResparseFile(currentFile);
        yield return currentFile;
    }

    private static (SparseChunk First, SparseChunk Second) SplitChunk(SparseChunk chunk, uint blocksToTake, SparseHeader header)
    {
        ChunkHeader h1 = chunk.Header with { ChunkSize = blocksToTake };
        ChunkHeader h2 = chunk.Header with { ChunkSize = chunk.Header.ChunkSize - blocksToTake };

        if (chunk.Header.ChunkType == (ushort)ChunkType.Raw)
        {
            h1 = h1 with { TotalSize = (uint)(header.ChunkHeaderSize + ((long)blocksToTake * header.BlockSize)) };
            h2 = h2 with { TotalSize = (uint)(header.ChunkHeaderSize + ((long)h2.ChunkSize * header.BlockSize)) };
        }
        else if (chunk.Header.ChunkType == (ushort)ChunkType.Fill)
        {
            h1 = h1 with { TotalSize = (uint)(header.ChunkHeaderSize + 4) };
            h2 = h2 with { TotalSize = (uint)(header.ChunkHeaderSize + 4) };
        }
        else
        {
            h1 = h1 with { TotalSize = (uint)header.ChunkHeaderSize };
            h2 = h2 with { TotalSize = (uint)header.ChunkHeaderSize };
        }

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

    private static SparseFile CreateNewSparseForResparse(SparseHeader header)
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

    private static long GetSparseChunkSize(SparseHeader header, SparseChunk chunk)
    {
        return chunk.Header.ChunkType switch
        {
            (ushort)ChunkType.Raw => header.ChunkHeaderSize + ((long)chunk.Header.ChunkSize * header.BlockSize),
            (ushort)ChunkType.Fill => header.ChunkHeaderSize + 4,
            _ => header.ChunkHeaderSize
        };
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
}