namespace FirmwareKit.Sparse.Core;

using FirmwareKit.Sparse.DataProviders;
using FirmwareKit.Sparse.Models;

/// <summary>
/// Chunk manipulation methods for <see cref="SparseFile"/>.
/// <para>SparseFile 的数据块操作方法。</para>
/// </summary>
public partial class SparseFile
{
    /// <summary>
    /// Adds a RAW chunk that references data from an external file.
    /// <para>添加引用外部文件数据的 RAW 数据块。</para>
    /// </summary>
    /// <param name="filePath">Path to the external file containing the chunk data. <para>包含数据块数据的外部文件路径。</para></param>
    /// <param name="offset">Byte offset within the external file where the chunk starts. <para>外部文件中数据块开始的字节偏移。</para></param>
    /// <param name="size">Number of bytes to include in the chunk. <para>数据块中包含的字节数。</para></param>
    /// <param name="blockIndex">Optional explicit starting block index; if null, appended at the current end. <para>可选的显式起始块索引；如果为 null，追加到当前末尾。</para></param>
    public void AddRawFileChunk(string filePath, long offset, uint size, uint? blockIndex = null)
    {
        var blockSize = Header.BlockSize;
        var totalBlocks = (size + blockSize - 1) / blockSize;
        var currentBlockStart = GetNextBlockAndCheckOverlap(blockIndex, totalBlocks);

        var remaining = size;
        var currentOffset = offset;

        while (remaining > 0)
        {
            var partSize = Math.Min(remaining, SparseFormat.MaxChunkDataSize);
            if (partSize < remaining && partSize % blockSize != 0)
            {
                partSize = partSize / blockSize * blockSize;
                if (partSize == 0) partSize = remaining;
            }

            var chunkBlocks = (uint)((partSize + blockSize - 1) / blockSize);
            var chunkHeader = new ChunkHeader
            {
                ChunkType = (ushort)ChunkType.Raw,
                ChunkSize = chunkBlocks,
                TotalSize = ChunkHelper.GetExpectedTotalSize((ushort)ChunkType.Raw, chunkBlocks, Header.ChunkHeaderSize, blockSize)
            };

            AddChunkSorted(new SparseChunk(chunkHeader)
            {
                StartBlock = currentBlockStart,
                DataProvider = new FileDataProvider(filePath, currentOffset, partSize)
            });
            currentBlockStart += chunkBlocks;
            remaining -= (uint)partSize;
            currentOffset += partSize;
        }
    }

    /// <summary>
    /// Adds a RAW chunk using data from an in-memory byte array buffer.
    /// Optimized version that minimizes memory allocations.
    /// <para>使用内存中字节数组缓冲区的数据添加 RAW 数据块。最小化内存分配的优化版本。</para>
    /// </summary>
    /// <param name="data">Byte array with source data for the chunk. <para>数据块源数据的字节数组。</para></param>
    /// <param name="blockIndex">Optional explicit starting block index; if null, appended at the current end. <para>可选的显式起始块索引；如果为 null，追加到当前末尾。</para></param>
    public void AddRawChunk(byte[] data, uint? blockIndex = null)
    {
        var blockSize = Header.BlockSize;
        var totalBlocks = (uint)((data.Length + blockSize - 1) / blockSize);
        var currentBlockStart = GetNextBlockAndCheckOverlap(blockIndex, totalBlocks);

        var provider = new MemoryDataProvider(data, 0, data.Length);

        var remaining = (uint)data.Length;
        var currentOffset = 0;

        while (remaining > 0)
        {
            var partSize = Math.Min(remaining, SparseFormat.MaxChunkDataSize);
            if (partSize < remaining && partSize % blockSize != 0)
            {
                partSize = partSize / blockSize * blockSize;
                if (partSize == 0) partSize = remaining;
            }

            var chunkBlocks = (uint)((partSize + blockSize - 1) / blockSize);
            var chunkHeader = new ChunkHeader
            {
                ChunkType = (ushort)ChunkType.Raw,
                ChunkSize = chunkBlocks,
                TotalSize = ChunkHelper.GetExpectedTotalSize((ushort)ChunkType.Raw, chunkBlocks, Header.ChunkHeaderSize, blockSize)
            };

            AddChunkSorted(new SparseChunk(chunkHeader)
            {
                StartBlock = currentBlockStart,
                DataProvider = provider.GetSubProvider(currentOffset, (long)partSize)
            });
            currentBlockStart += chunkBlocks;
            remaining -= (uint)partSize;
            currentOffset += (int)partSize;
        }
    }

    /// <summary>
    /// Adds a RAW chunk using data read from a stream.
    /// <para>使用从流中读取的数据添加 RAW 数据块。</para>
    /// </summary>
    /// <param name="stream">Source stream to read chunk data from. <para>读取数据块数据的源流。</para></param>
    /// <param name="offset">Byte offset within the stream to start reading. <para>流中开始读取的字节偏移。</para></param>
    /// <param name="size">Number of bytes to include in the chunk. <para>数据块中包含的字节数。</para></param>
    /// <param name="blockIndex">Optional explicit starting block index; if null, appended at the current end. <para>可选的显式起始块索引；如果为 null，追加到当前末尾。</para></param>
    /// <param name="leaveOpen">If true, do not close the input stream after reading. <para>如果为 true，读取后不关闭输入流。</para></param>
    public void AddStreamChunk(Stream stream, long offset, uint size, uint? blockIndex = null, bool leaveOpen = true)
    {
        var blockSize = Header.BlockSize;
        var totalBlocks = (size + blockSize - 1) / blockSize;
        var currentBlockStart = GetNextBlockAndCheckOverlap(blockIndex, totalBlocks);

        var remaining = size;
        var currentOffset = offset;

        while (remaining > 0)
        {
            var partSize = Math.Min(remaining, (uint)SparseFormat.MaxChunkDataSize);
            if (partSize < remaining && partSize % blockSize != 0)
            {
                partSize = partSize / blockSize * blockSize;
                if (partSize == 0) partSize = remaining;
            }

            var chunkBlocks = (uint)((partSize + blockSize - 1) / blockSize);
            var chunkHeader = new ChunkHeader
            {
                ChunkType = (ushort)ChunkType.Raw,
                ChunkSize = chunkBlocks,
                TotalSize = ChunkHelper.GetExpectedTotalSize((ushort)ChunkType.Raw, chunkBlocks, Header.ChunkHeaderSize, blockSize)
            };

            AddChunkSorted(new SparseChunk(chunkHeader)
            {
                StartBlock = currentBlockStart,
                DataProvider = new StreamDataProvider(stream, currentOffset, partSize, leaveOpen)
            });
            currentBlockStart += chunkBlocks;
            remaining -= (uint)partSize;
            currentOffset += partSize;
        }
    }

    /// <summary>
    /// Adds a FILL chunk that repeats a 4-byte pattern to cover the specified range.
    /// <para>添加重复 4 字节模式以覆盖指定范围的 FILL 数据块。</para>
    /// </summary>
    /// <param name="fillValue">4-byte value to repeat. <para>要重复的 4 字节值。</para></param>
    /// <param name="size">Total size in bytes that the fill chunk should cover. <para>填充数据块应覆盖的总大小（字节）。</para></param>
    /// <param name="blockIndex">Optional explicit starting block index; if null, appended at the current end. <para>可选的显式起始块索引；如果为 null，追加到当前末尾。</para></param>
    public void AddFillChunk(uint fillValue, long size, uint? blockIndex = null)
    {
        var blockSize = Header.BlockSize;
        var totalBlocks = (uint)((size + blockSize - 1) / blockSize);
        var currentBlockStart = GetNextBlockAndCheckOverlap(blockIndex, totalBlocks);

        var remaining = size;

        while (remaining > 0)
        {
            var partSize = Math.Min(remaining, (long)0x00FFFFFF * blockSize);
            var partBlocks = Math.Min((uint)((partSize + blockSize - 1) / blockSize), 0x00FFFFFFu);
            var actualPartSize = (long)partBlocks * blockSize;

            AddChunkSorted(new SparseChunk(new ChunkHeader
            {
                ChunkType = (ushort)ChunkType.Fill,
                ChunkSize = partBlocks,
                TotalSize = ChunkHelper.GetExpectedTotalSize((ushort)ChunkType.Fill, partBlocks, Header.ChunkHeaderSize, blockSize)
            })
            {
                StartBlock = currentBlockStart,
                FillValue = fillValue
            });

            currentBlockStart += partBlocks;
            remaining -= actualPartSize;
        }
    }

    /// <summary>
    /// Adds a DONT_CARE (skip) chunk representing an unallocated or empty region.
    /// <para>添加表示未分配或空区域的 DONT_CARE（跳过）数据块。</para>
    /// </summary>
    /// <param name="size">Size in bytes of the unallocated region to represent. <para>要表示的未分配区域的大小（字节）。</para></param>
    /// <param name="blockIndex">Optional explicit starting block index; if null, appended at the current end. <para>可选的显式起始块索引；如果为 null，追加到当前末尾。</para></param>
    public void AddDontCareChunk(long size, uint? blockIndex = null)
    {
        var totalBlocks = (uint)((size + Header.BlockSize - 1) / Header.BlockSize);
        var currentBlockStart = GetNextBlockAndCheckOverlap(blockIndex, totalBlocks);
        AddDontCareChunkInternal(size, currentBlockStart);
    }

    /// <summary>
    /// Iterates through all chunks that contain actual data (RAW or FILL) and invokes the provided action.
    /// <para>遍历所有包含实际数据（RAW 或 FILL）的数据块，并调用提供的操作。</para>
    /// </summary>
    /// <param name="action">Action to invoke for each data chunk. Parameters: chunk, startBlock, chunkSize. <para>每个数据块调用的操作。参数：chunk, startBlock, chunkSize。</para></param>
    public void ForEachChunk(Action<SparseChunk, uint, uint> action)
    {
        uint currentBlock = 0;
        foreach (SparseChunk chunk in _chunks)
        {
            if (chunk.Header.ChunkType is (ushort)ChunkType.Raw or (ushort)ChunkType.Fill)
            {
                action(chunk, currentBlock, chunk.Header.ChunkSize);
            }
            currentBlock += chunk.Header.ChunkSize;
        }
    }

    /// <summary>
    /// Iterates through every chunk in the sparse file and invokes the provided action for each.
    /// <para>遍历稀疏文件中的每个数据块，并为每个数据块调用提供的操作。</para>
    /// </summary>
    /// <param name="action">Action to invoke for each chunk. Parameters: chunk, startBlock, chunkSize. <para>每个数据块调用的操作。参数：chunk, startBlock, chunkSize。</para></param>
    public void ForEachChunkAll(Action<SparseChunk, uint, uint> action)
    {
        uint currentBlock = 0;
        foreach (SparseChunk chunk in _chunks)
        {
            action(chunk, currentBlock, chunk.Header.ChunkSize);
            currentBlock += chunk.Header.ChunkSize;
        }
    }

    /// <summary>
    /// Determines the next available block index and checks for overlap with existing chunks.
    /// <para>确定下一个可用块索引并检查与现有数据块的重叠。</para>
    /// </summary>
    /// <param name="blockIndex">Optional explicit starting block index. <para>可选的显式起始块索引。</para></param>
    /// <param name="sizeInBlocks">Number of blocks the new chunk spans. <para>新数据块跨越的块数。</para></param>
    /// <returns>The validated starting block index. <para>验证后的起始块索引。</para></returns>
    private uint GetNextBlockAndCheckOverlap(uint? blockIndex, uint sizeInBlocks)
    {
        var start = blockIndex ?? CurrentBlock;
        var end = start + sizeInBlocks;

        int count = _chunks.Count;
        if (count > 0)
        {
            int left = 0, right = count - 1;

            while (left <= right)
            {
                int mid = left + ((right - left) >> 1);
                uint midEnd = _chunks[mid].StartBlock + _chunks[mid].Header.ChunkSize;

                if (midEnd <= start)
                    left = mid + 1;
                else
                    right = mid - 1;
            }

            int checkStart = Math.Max(0, left - 2);
            int checkEnd = Math.Min(count, left + 2);

            for (int i = checkStart; i < checkEnd; i++)
            {
                SparseChunk chunk = _chunks[i];
                var chunkEnd = chunk.StartBlock + chunk.Header.ChunkSize;
                if (start < chunkEnd && end > chunk.StartBlock)
                {
                    throw new ArgumentException($"Block region [{start}, {end}) overlaps with existing chunk [{chunk.StartBlock}, {chunkEnd}).");
                }
            }
        }

        if (blockIndex.HasValue && blockIndex.Value > CurrentBlock)
        {
            AddDontCareChunkInternal((long)(blockIndex.Value - CurrentBlock) * Header.BlockSize, CurrentBlock);
        }

        return start;
    }

    /// <summary>
    /// Internal helper to add DONT_CARE chunks, splitting into maximum-size parts as needed.
    /// <para>添加 DONT_CARE 数据块的内部辅助方法，根据需要拆分为最大大小的部分。</para>
    /// </summary>
    /// <param name="size">Size in bytes of the unallocated region. <para>未分配区域的大小（字节）。</para></param>
    /// <param name="startBlock">Starting block index. <para>起始块索引。</para></param>
    private void AddDontCareChunkInternal(long size, uint startBlock)
    {
        var blockSize = Header.BlockSize;
        var remaining = size;
        var currentBlockStart = startBlock;
        while (remaining > 0)
        {
            var partBlocks = Math.Min((uint)((remaining + blockSize - 1) / blockSize), 0x00FFFFFFu);

            AddChunkSorted(new SparseChunk(new ChunkHeader
            {
                ChunkType = (ushort)ChunkType.DontCare,
                ChunkSize = partBlocks,
                TotalSize = ChunkHelper.GetExpectedTotalSize((ushort)ChunkType.DontCare, partBlocks, Header.ChunkHeaderSize, blockSize)
            })
            {
                StartBlock = currentBlockStart
            });

            currentBlockStart += partBlocks;
            remaining -= (long)partBlocks * blockSize;
        }
    }

    /// <summary>
    /// Inserts a chunk into the sorted chunk list, maintaining block order.
    /// Uses binary search for efficient insertion when not appending.
    /// <para>将数据块插入排序的数据块列表，保持块顺序。非追加时使用二分查找进行高效插入。</para>
    /// </summary>
    /// <param name="chunk">The chunk to insert. <para>要插入的数据块。</para></param>
    private void AddChunkSorted(SparseChunk chunk)
    {
        int count = _chunks.Count;
        if (count == 0 || chunk.StartBlock >= _chunks[count - 1].StartBlock)
        {
            _chunks.Add(chunk);
            return;
        }

        int left = 0, right = count - 1;
        while (left <= right)
        {
            int mid = left + ((right - left) >> 1);
            uint midBlock = _chunks[mid].StartBlock;
            if (midBlock == chunk.StartBlock)
            {
                _chunks.Insert(mid, chunk);
                return;
            }
            if (midBlock < chunk.StartBlock)
                left = mid + 1;
            else
                right = mid - 1;
        }
        _chunks.Insert(left, chunk);
    }

    /// <summary>
    /// Comparer for sorting <see cref="SparseChunk"/> instances by their StartBlock.
    /// <para>按 StartBlock 排序 SparseChunk 实例的比较器。</para>
    /// </summary>
    private class SparseChunkComparer : IComparer<SparseChunk>
    {
        public static readonly SparseChunkComparer Instance = new SparseChunkComparer();
        public int Compare(SparseChunk? x, SparseChunk? y) => x!.StartBlock.CompareTo(y!.StartBlock);
    }
}
