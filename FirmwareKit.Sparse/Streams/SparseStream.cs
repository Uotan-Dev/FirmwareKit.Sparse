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

    /// <inheritdoc/>
    public override bool CanRead => true;
    /// <inheritdoc/>
    public override bool CanSeek => true;
    /// <inheritdoc/>
    public override bool CanWrite => false;
    /// <inheritdoc/>
    public override long Length => _length;

    /// <inheritdoc/>
    public override long Position
    {
        get => _position;
        set => _position = value < 0 ? 0 : (value > _length ? _length : value);
    }

    /// <inheritdoc/>
    public override void Flush() { }

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_position >= _length)
        {
            return 0;
        }

        var toRead = (int)Math.Min(count, _length - _position);
        var totalRead = 0;
        Span<byte> fillValue = stackalloc byte[4];

        while (totalRead < toRead)
        {
            var (chunk, startBlock) = FindChunkAtOffset(_position);
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

            ProcessChunkData(chunk, offsetInChunk, buffer, offset + totalRead, currentReadSize, fillValue);

            _position += currentReadSize;
            totalRead += currentReadSize;
        }

        return totalRead;
    }

    private uint GetNextChunkBlock(long position)
    {
        var targetBlock = (uint)(position / _sparseFile.Header.BlockSize);

        var low = 0;
        var high = _chunkLookup.Length - 1;
        var nextBlock = (uint)(_length / _sparseFile.Header.BlockSize);

        while (low <= high)
        {
            var mid = low + ((high - low) / 2);
            if (_chunkLookup[mid].StartBlock > targetBlock)
            {
                nextBlock = _chunkLookup[mid].StartBlock;
                high = mid - 1;
            }
            else
            {
                low = mid + 1;
            }
        }
        return nextBlock;
    }

    private void ProcessChunkData(SparseChunk chunk, long offsetInChunk, byte[] buffer, int bufferOffset, int count, Span<byte> fillValue)
    {
        switch (chunk.Header.ChunkType)
        {
            case (ushort)ChunkType.Raw:
                if (chunk.DataProvider != null)
                {
                    var read = chunk.DataProvider.Read(offsetInChunk, buffer, bufferOffset, count);
                    if (read < count)
                    {
                        Array.Clear(buffer, bufferOffset + read, count - read);
                    }
                }
                else
                {
                    Array.Clear(buffer, bufferOffset, count);
                }
                break;
            case (ushort)ChunkType.Fill:
                BinaryPrimitives.WriteUInt32LittleEndian(fillValue, chunk.FillValue);
                var destSpan = buffer.AsSpan(bufferOffset, count);
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
                Array.Clear(buffer, bufferOffset, count);
                break;
        }
    }

    private (SparseChunk? chunk, uint startBlock) FindChunkAtOffset(long offset)
    {
        var targetBlock = (uint)(offset / _sparseFile.Header.BlockSize);

        var low = 0;
        var high = _chunkLookup.Length - 1;

        while (low <= high)
        {
            var mid = low + ((high - low) / 2);
            var (startBlock, endBlock, chunkIndex) = _chunkLookup[mid];

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

    /// <inheritdoc/>
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

    /// <inheritdoc/>
    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }
}
