namespace FirmwareKit.Sparse.Streams;
/// <summary>
/// A read-only <see cref="Stream"/> that wraps a <see cref="SparseFile"/> to allow random access to its uncompressed data.
/// </summary>
public class SparseStream : Stream
{
    private readonly SparseFile _sparseFile;
    private readonly long _length;
    private long _position;
    private readonly (uint StartBlock, uint EndBlock, int ChunkIndex)[] _chunkLookup;

    /// <summary>
    /// Initializes a new instance of the <see cref="SparseStream"/> class.
    /// </summary>
    /// <param name="sparseFile">The sparse file instance.</param>
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

    /// <summary>Indicates whether this stream supports reading (true).</summary>
    public override bool CanRead => true;

    /// <summary>Indicates whether this stream supports seeking (true).</summary>
    public override bool CanSeek => true;

    /// <summary>Indicates whether this stream supports writing (false).</summary>
    public override bool CanWrite => false;

    /// <summary>Gets the total logical length, in bytes, of the underlying sparse data.</summary>
    public override long Length => _length;

    /// <summary>Gets or sets the current read position within the stream.</summary>
    public override long Position
    {
        get => _position;
        set => _position = value < 0 ? 0 : (value > _length ? _length : value);
    }

    /// <summary>Flush has no effect on this read-only stream.</summary>
    public override void Flush() { }


#if NET6_0_OR_GREATER || NETSTANDARD2_1_OR_GREATER
    /// <summary>
    /// Read bytes from the sparse stream into a byte array.
    /// </summary>
    /// <param name="buffer">Destination buffer to receive bytes.</param>
    /// <param name="offset">Offset in the destination buffer to start writing (int).</param>
    /// <param name="count">Maximum number of bytes to read (int).</param>
    /// <returns>The number of bytes read.</returns>
    public override int Read(byte[] buffer, int offset, int count)
    {
        return Read(buffer.AsSpan(offset, count));
    }
#endif

#if NET6_0_OR_GREATER || NETSTANDARD2_1_OR_GREATER
    /// <summary>
    /// Read bytes from the sparse stream into the provided span.
    /// This method will read up to <paramref name="buffer"/>.Length bytes from the current position.
    /// </summary>
    /// <param name="buffer">Destination span to receive bytes.</param>
    /// <returns>The number of bytes read.</returns>
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
    /// Read bytes from the sparse stream into the provided buffer.
    /// This overload is used on platforms that do not support <see cref="Span{T}"/>-based APIs.
    /// </summary>
    /// <param name="buffer">Destination buffer to receive bytes.</param>
    /// <param name="offset">Offset in the destination buffer to start writing (int).</param>
    /// <param name="count">Maximum number of bytes to read (int).</param>
    /// <returns>The number of bytes actually read (int).</returns>
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
    /// Fill <paramref name="destSpan"/> with data from the specified <paramref name="chunk"/>
    /// starting at the given <paramref name="offsetInChunk"/>. Handles RAW, FILL and other chunk types.
    /// </summary>
    /// <param name="chunk">Chunk to read data from.</param>
    /// <param name="offsetInChunk">Byte offset inside the chunk to start reading from.</param>
    /// <param name="destSpan">Destination span to receive chunk bytes.</param>
    /// <param name="fillValue">Temporary buffer used for fill pattern generation.</param>
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
    /// Find the chunk that contains the given logical byte offset and return it
    /// along with the chunk's starting block index.
    /// </summary>
    /// <param name="offset">Logical byte offset within the sparse data.</param>
    /// <returns>Tuple of the chunk (or null) and its starting block index.</returns>
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
    /// Return the block index of the next chunk following the given byte offset.
    /// </summary>
    /// <param name="offset">Logical byte offset within the sparse data.</param>
    /// <returns>Block index of the next chunk.</returns>
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
    /// Seek to a specific position within the stream.
    /// </summary>
    /// <param name="offset">Offset to seek to relative to <paramref name="origin"/>.</param>
    /// <param name="origin">Specifies the reference point used to obtain the new position.</param>
    /// <returns>The new position within the stream.</returns>
    public override long Seek(long offset, SeekOrigin origin)
    {
        switch (origin)
        {
            case SeekOrigin.Begin: Position = offset; break;
            case SeekOrigin.Current: Position += offset; break;
            case SeekOrigin.End: Position = _length + offset; break;
            default:
                break;
        }
        return Position;
    }

    /// <summary>
    /// Setting the length is not supported for this read-only stream.
    /// </summary>
    /// <param name="value">Not used.</param>
    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    /// <summary>
    /// Writing is not supported for this read-only stream.
    /// </summary>
    /// <param name="buffer">Not used.</param>
    /// <param name="offset">Not used.</param>
    /// <param name="count">Not used.</param>
    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }
}
