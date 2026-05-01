namespace FirmwareKit.Sparse.Streams;

/// <summary>
/// A read-only <see cref="Stream"/> that wraps a <see cref="SparseFile"/> to allow random access to its uncompressed data.
/// <para>包装 SparseFile 的只读流，允许对其未压缩数据进行随机访问。</para>
/// </summary>
public class SparseStream : Stream
{
    private readonly SparseFile _sparseFile;
    private readonly long _length;
    private long _position;
    private readonly (uint StartBlock, uint EndBlock, int ChunkIndex)[] _chunkLookup;

    /// <summary>
    /// Initializes a new instance of the <see cref="SparseStream"/> class.
    /// <para>初始化 SparseStream 类的新实例。</para>
    /// </summary>
    /// <param name="sparseFile">The sparse file instance. <para>稀疏文件实例。</para></param>
    public SparseStream(SparseFile sparseFile)
    {
        _sparseFile = sparseFile;
        _length = (long)sparseFile.Header.TotalBlocks * sparseFile.Header.BlockSize;

        _chunkLookup = new (uint, uint, int)[sparseFile.Chunks.Count];
        uint currentBlock = 0;
        for (var i = 0; i < sparseFile.Chunks.Count; i++)
        {
            var numBlocks = sparseFile.Chunks[i].Header.ChunkSize;
            _chunkLookup[i] = (currentBlock, currentBlock + numBlocks, i);
            currentBlock += numBlocks;
        }
    }

    /// <summary>
    /// Indicates whether this stream supports reading (always true).
    /// <para>指示此流是否支持读取（始终为 true）。</para>
    /// </summary>
    public override bool CanRead => true;

    /// <summary>
    /// Indicates whether this stream supports seeking (always true).
    /// <para>指示此流是否支持查找（始终为 true）。</para>
    /// </summary>
    public override bool CanSeek => true;

    /// <summary>
    /// Indicates whether this stream supports writing (always false).
    /// <para>指示此流是否支持写入（始终为 false）。</para>
    /// </summary>
    public override bool CanWrite => false;

    /// <summary>
    /// Gets the total logical length, in bytes, of the underlying sparse data.
    /// <para>获取底层稀疏数据的逻辑总长度（字节数）。</para>
    /// </summary>
    public override long Length => _length;

    /// <summary>
    /// Gets or sets the current read position within the stream.
    /// <para>获取或设置流中的当前读取位置。</para>
    /// </summary>
    public override long Position
    {
        get => _position;
        set => _position = value < 0 ? 0 : (value > _length ? _length : value);
    }

    /// <summary>
    /// Flush has no effect on this read-only stream.
    /// <para>Flush 对此只读流无效。</para>
    /// </summary>
    public override void Flush() { }

#if NET6_0_OR_GREATER || NETSTANDARD2_1_OR_GREATER
    /// <summary>
    /// Reads bytes from the sparse stream into a byte array.
    /// <para>从稀疏流中读取字节到字节数组。</para>
    /// </summary>
    /// <param name="buffer">Destination buffer to receive bytes. <para>接收字节的目标缓冲区。</para></param>
    /// <param name="offset">Offset in the destination buffer to start writing. <para>目标缓冲区中开始写入的偏移量。</para></param>
    /// <param name="count">Maximum number of bytes to read. <para>要读取的最大字节数。</para></param>
    /// <returns>The number of bytes read. <para>读取的字节数。</para></returns>
    public override int Read(byte[] buffer, int offset, int count)
    {
        return Read(buffer.AsSpan(offset, count));
    }
#endif

#if NET6_0_OR_GREATER || NETSTANDARD2_1_OR_GREATER
    /// <summary>
    /// Reads bytes from the sparse stream into the provided span.
    /// <para>从稀疏流中读取字节到提供的跨度。</para>
    /// </summary>
    /// <param name="buffer">Destination span to receive bytes. <para>接收字节的目标跨度。</para></param>
    /// <returns>The number of bytes read. <para>读取的字节数。</para></returns>
    public override int Read(Span<byte> buffer)
    {
        if (_position >= _length)
        {
            return 0;
        }

        var toRead = (int)Math.Min(buffer.Length, _length - _position);
        var totalRead = 0;
        Span<byte> fillValue = stackalloc byte[4];

        while (totalRead < toRead)
        {
            (SparseChunk? chunk, uint startBlock) = FindChunkAtOffset(_position);
            int currentReadSize;

            if (chunk == null)
            {
                var nextChunkBlock = GetNextChunkBlock(_position);
                var endOfGap = Math.Min(_length, (long)nextChunkBlock * _sparseFile.Header.BlockSize);
                currentReadSize = (int)Math.Min(toRead - totalRead, endOfGap - _position);

                if (currentReadSize <= 0)
                {
                    break;
                }

                buffer.Slice(totalRead, currentReadSize).Clear();

                _position += currentReadSize;
                totalRead += currentReadSize;
                continue;
            }

            var chunkStartOffset = (long)startBlock * _sparseFile.Header.BlockSize;
            var offsetInChunk = _position - chunkStartOffset;
            var chunkRemaining = ((long)chunk.Header.ChunkSize * _sparseFile.Header.BlockSize) - offsetInChunk;
            currentReadSize = (int)Math.Min(toRead - totalRead, chunkRemaining);

            ProcessChunkData(chunk, offsetInChunk, buffer.Slice(totalRead, currentReadSize), fillValue);

            _position += currentReadSize;
            totalRead += currentReadSize;
        }

        return totalRead;
    }
#else
    /// <summary>
    /// Reads bytes from the sparse stream into the provided buffer.
    /// This overload is used on platforms that do not support <see cref="Span{T}"/>-based APIs.
    /// <para>从稀疏流中读取字节到提供的缓冲区。此重载用于不支持基于 Span{T} API 的平台。</para>
    /// </summary>
    /// <param name="buffer">Destination buffer to receive bytes. <para>接收字节的目标缓冲区。</para></param>
    /// <param name="offset">Offset in the destination buffer to start writing. <para>目标缓冲区中开始写入的偏移量。</para></param>
    /// <param name="count">Maximum number of bytes to read. <para>要读取的最大字节数。</para></param>
    /// <returns>The number of bytes actually read. <para>实际读取的字节数。</para></returns>
    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_position >= _length)
        {
            return 0;
        }

        var toRead = (int)Math.Min(count, _length - _position);
        var totalRead = 0;
        byte[] fillValueArr = new byte[4];
        Span<byte> fillValue = fillValueArr;

        while (totalRead < toRead)
        {
            (SparseChunk? chunk, uint startBlock) = FindChunkAtOffset(_position);
            int currentReadSize;

            if (chunk == null)
            {
                var nextChunkBlock = GetNextChunkBlock(_position);
                var endOfGap = Math.Min(_length, (long)nextChunkBlock * _sparseFile.Header.BlockSize);
                currentReadSize = (int)Math.Min(toRead - totalRead, endOfGap - _position);

                if (currentReadSize <= 0)
                {
                    break;
                }

                Array.Clear(buffer, offset + totalRead, currentReadSize);

                _position += currentReadSize;
                totalRead += currentReadSize;
                continue;
            }

            var chunkStartOffset = (long)startBlock * _sparseFile.Header.BlockSize;
            var offsetInChunk = _position - chunkStartOffset;
            var chunkRemaining = ((long)chunk.Header.ChunkSize * _sparseFile.Header.BlockSize) - offsetInChunk;
            currentReadSize = (int)Math.Min(toRead - totalRead, chunkRemaining);

            ProcessChunkData(chunk, offsetInChunk, buffer.AsSpan(offset + totalRead, currentReadSize), fillValue);

            _position += currentReadSize;
            totalRead += currentReadSize;
        }

        return totalRead;
    }
#endif

    /// <summary>
    /// Fills <paramref name="destSpan"/> with data from the specified <paramref name="chunk"/>
    /// starting at the given <paramref name="offsetInChunk"/>. Handles RAW, FILL and other chunk types.
    /// <para>从指定数据块的给定偏移量开始，用数据填充目标跨度。处理 RAW、FILL 及其他数据块类型。</para>
    /// </summary>
    /// <param name="chunk">Chunk to read data from. <para>要读取数据的数据块。</para></param>
    /// <param name="offsetInChunk">Byte offset inside the chunk to start reading from. <para>数据块内开始读取的字节偏移。</para></param>
    /// <param name="destSpan">Destination span to receive chunk bytes. <para>接收数据块字节的目标跨度。</para></param>
    /// <param name="fillValue">Temporary buffer used for fill pattern generation. <para>用于生成填充模式的临时缓冲区。</para></param>
    private void ProcessChunkData(SparseChunk chunk, long offsetInChunk, Span<byte> destSpan, Span<byte> fillValue)
    {
        switch (chunk.Header.ChunkType)
        {
            case (ushort)ChunkType.Raw:
                if (chunk.DataProvider != null)
                {
                    var read = chunk.DataProvider.Read(offsetInChunk, destSpan);
                    if (read < destSpan.Length)
                    {
                        destSpan.Slice(read).Clear();
                    }
                }
                else
                {
                    destSpan.Clear();
                }
                break;

            case (ushort)ChunkType.Fill:
                BinaryPrimitives.WriteUInt32LittleEndian(fillValue, chunk.FillValue);
                var count = destSpan.Length;
                var firstFillSize = (int)(4 - (offsetInChunk % 4));
                if (firstFillSize > 0 && firstFillSize < 4)
                {
                    var toCopy = Math.Min(firstFillSize, count);
                    fillValue.Slice((int)(offsetInChunk % 4), toCopy).CopyTo(destSpan);
                    destSpan = destSpan.Slice(toCopy);
                }

                while (destSpan.Length >= 4)
                {
                    BinaryPrimitives.WriteUInt32LittleEndian(destSpan, chunk.FillValue);
                    destSpan = destSpan.Slice(4);
                }

                if (destSpan.Length > 0)
                {
                    fillValue.Slice(0, destSpan.Length).CopyTo(destSpan);
                }
                break;

            default:
                destSpan.Clear();
                break;
        }
    }

    /// <summary>
    /// Finds the chunk that contains the given logical byte offset and returns it
    /// along with the chunk's starting block index, using binary search.
    /// <para>使用二分查找找到包含给定逻辑字节偏移的数据块，并返回该数据块及其起始块索引。</para>
    /// </summary>
    /// <param name="offset">Logical byte offset within the sparse data. <para>稀疏数据内的逻辑字节偏移。</para></param>
    /// <returns>A tuple of the chunk (or null) and its starting block index. <para>包含数据块（或 null）及其起始块索引的元组。</para></returns>
    private (SparseChunk? chunk, uint startBlock) FindChunkAtOffset(long offset)
    {
        var targetBlock = (uint)(offset / _sparseFile.Header.BlockSize);

        var low = 0;
        var high = _chunkLookup.Length - 1;

        while (low <= high)
        {
            var mid = low + ((high - low) / 2);
            (uint startBlock, uint endBlock, int chunkIndex) = _chunkLookup[mid];

            if (targetBlock >= startBlock && targetBlock < endBlock)
            {
                return (_sparseFile.Chunks[chunkIndex], startBlock);
            }

            if (targetBlock < startBlock)
            {
                high = mid - 1;
            }
            else
            {
                low = mid + 1;
            }
        }

        return (null, 0);
    }

    /// <summary>
    /// Returns the block index of the next chunk following the given byte offset.
    /// <para>返回给定字节偏移之后下一个数据块的块索引。</para>
    /// </summary>
    /// <param name="offset">Logical byte offset within the sparse data. <para>稀疏数据内的逻辑字节偏移。</para></param>
    /// <returns>Block index of the next chunk. <para>下一个数据块的块索引。</para></returns>
    private uint GetNextChunkBlock(long offset)
    {
        var targetBlock = (uint)(offset / _sparseFile.Header.BlockSize);
        for (int i = 0; i < _chunkLookup.Length; i++)
        {
            if (_chunkLookup[i].StartBlock > targetBlock)
            {
                return _chunkLookup[i].StartBlock;
            }
        }

        return _sparseFile.Header.TotalBlocks;
    }

    /// <summary>
    /// Seeks to a specific position within the stream.
    /// <para>在流中查找指定位置。</para>
    /// </summary>
    /// <param name="offset">Offset to seek to relative to <paramref name="origin"/>. <para>相对于 origin 的查找偏移量。</para></param>
    /// <param name="origin">Specifies the reference point used to obtain the new position. <para>指定用于获取新位置的参考点。</para></param>
    /// <returns>The new position within the stream. <para>流中的新位置。</para></returns>
    public override long Seek(long offset, SeekOrigin origin)
    {
        switch (origin)
        {
            case SeekOrigin.Begin: Position = offset; break;
            case SeekOrigin.Current: Position += offset; break;
            case SeekOrigin.End: Position = _length + offset; break;
        }
        return Position;
    }

    /// <summary>
    /// Setting the length is not supported for this read-only stream.
    /// <para>此只读流不支持设置长度。</para>
    /// </summary>
    /// <param name="value">Not used. <para>未使用。</para></param>
    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    /// <summary>
    /// Writing is not supported for this read-only stream.
    /// <para>此只读流不支持写入。</para>
    /// </summary>
    /// <param name="buffer">Not used. <para>未使用。</para></param>
    /// <param name="offset">Not used. <para>未使用。</para></param>
    /// <param name="count">Not used. <para>未使用。</para></param>
    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }
}
