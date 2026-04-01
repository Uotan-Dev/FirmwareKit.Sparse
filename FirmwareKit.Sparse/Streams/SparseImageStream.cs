namespace FirmwareKit.Sparse.Streams;

/// <summary>
/// A stream that maps sparse chunks back into a complete sparse image format.
/// </summary>
public class SparseImageStream : Stream
{
    private readonly uint _blockSize;
    private readonly List<SparseChunk> _mappedChunks = new List<SparseChunk>();
    private readonly List<Section> _sections = new List<Section>();
    private readonly long _totalByteLength;
    private readonly SparseFile? _ownedFile;
    private readonly int _chunkHeaderSize;
    private long _position;

    private struct Section
    {
        public long StartByteOffset;
        public long Length;
        public SectionType Type;
        public int ChunkIndex;
        public byte[]? StaticData;
    }

    private enum SectionType
    {
        SparseHeader,
        ChunkHeader,
        ChunkData,
        CrcHeader,
        CrcData
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SparseImageStream"/> class.
    /// </summary>
    /// <param name="source">The source sparse file.</param>
    /// <param name="startBlock">The absolute starting block offset.</param>
    /// <param name="blockCount">The number of blocks in the stream.</param>
    /// <param name="includeCrc">Whether to append a CRC32 checksum chunk.</param>
    /// <param name="fullRange">Whether to maintain the original file's TotalBlocks and pad with "skip" chunks.</param>
    /// <param name="disposeSource">Whether to dispose the source file when this stream is disposed.</param>
    public SparseImageStream(SparseFile source, uint startBlock, uint blockCount, bool includeCrc = false, bool fullRange = true, bool disposeSource = false)
    {
        _blockSize = source.Header.BlockSize;
        _ownedFile = disposeSource ? source : null;

        // Ensure chunk header size is available for CloneChunkSlice during mapping
        _chunkHeaderSize = (int)source.Header.ChunkHeaderSize;

        MapChunks(source, startBlock, blockCount, fullRange);

        long currentByteOffset = 0;
        var totalChunks = (uint)_mappedChunks.Count;
        uint imageChecksum = 0;

        if (includeCrc)
        {
            totalChunks++;
            imageChecksum = CalculateChecksum();
        }

        var header = new SparseHeader
        {
            Magic = SparseFormat.SparseHeaderMagic,
            MajorVersion = source.Header.MajorVersion,
            MinorVersion = source.Header.MinorVersion,
            FileHeaderSize = source.Header.FileHeaderSize,
            ChunkHeaderSize = source.Header.ChunkHeaderSize,
            BlockSize = _blockSize,
            TotalBlocks = fullRange ? source.Header.TotalBlocks : blockCount,
            TotalChunks = totalChunks,
            ImageChecksum = imageChecksum
        };
        // _chunkHeaderSize was initialized from source.Header above

        // Sanity-check: first print mapped chunk info for debugging
        // sanity checks omitted in release

        for (int i = 0; i < _mappedChunks.Count; i++)
        {
            var c = _mappedChunks[i];
            long expected = c.Header.ChunkType == (ushort)ChunkType.Raw
                ? (long)_chunkHeaderSize + ((long)c.Header.ChunkSize * _blockSize)
                : c.Header.ChunkType == (ushort)ChunkType.Fill ? (long)_chunkHeaderSize + 4 : (long)_chunkHeaderSize;
            if (c.Header.TotalSize != (uint)expected)
            {
                throw new InvalidOperationException($"Mapped chunk {i} TotalSize mismatch: actual={c.Header.TotalSize}, expected={expected}");
            }
        }
        var headerBytes = header.ToBytes();
        _sections.Add(new Section
        {
            StartByteOffset = 0,
            Length = headerBytes.Length,
            Type = SectionType.SparseHeader,
            StaticData = headerBytes
        });
        currentByteOffset += headerBytes.Length;
        if (header.FileHeaderSize > headerBytes.Length)
        {
            var padLen = header.FileHeaderSize - headerBytes.Length;
            _sections.Add(new Section { StartByteOffset = currentByteOffset, Length = padLen, Type = SectionType.SparseHeader, StaticData = new byte[padLen] });
            currentByteOffset += padLen;
        }

        for (var i = 0; i < _mappedChunks.Count; i++)
        {
            SparseChunk chunk = _mappedChunks[i];
            var chunkHeaderBytes = chunk.Header.ToBytes();
            var chunkHeaderLen = header.ChunkHeaderSize;
            var chunkHeaderBuf = new byte[chunkHeaderLen];
            Array.Copy(chunkHeaderBytes, 0, chunkHeaderBuf, 0, chunkHeaderBytes.Length);

            _sections.Add(new Section
            {
                StartByteOffset = currentByteOffset,
                Length = chunkHeaderLen,
                Type = SectionType.ChunkHeader,
                ChunkIndex = i,
                StaticData = chunkHeaderBuf
            });
            currentByteOffset += chunkHeaderLen;

            var dataSize = (long)chunk.Header.TotalSize - header.ChunkHeaderSize;
            if (dataSize > 0)
            {
                _sections.Add(new Section
                {
                    StartByteOffset = currentByteOffset,
                    Length = dataSize,
                    Type = SectionType.ChunkData,
                    ChunkIndex = i
                });
                currentByteOffset += dataSize;
            }
        }

        if (includeCrc)
        {
            var crcHeader = new ChunkHeader
            {
                ChunkType = (ushort)ChunkType.Crc32,
                Reserved = 0,
                ChunkSize = 0,
                TotalSize = (uint)(header.ChunkHeaderSize + 4)
            };
            var crcHeaderBytes = crcHeader.ToBytes();
            var crcHeaderBuf = new byte[header.ChunkHeaderSize];
            Array.Copy(crcHeaderBytes, 0, crcHeaderBuf, 0, crcHeaderBytes.Length);
            _sections.Add(new Section
            {
                StartByteOffset = currentByteOffset,
                Length = crcHeaderBuf.Length,
                Type = SectionType.CrcHeader,
                StaticData = crcHeaderBuf
            });
            currentByteOffset += crcHeaderBuf.Length;

            var crcBytes = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(crcBytes, imageChecksum);
            _sections.Add(new Section
            {
                StartByteOffset = currentByteOffset,
                Length = crcBytes.Length,
                Type = SectionType.CrcData,
                StaticData = crcBytes
            });
            currentByteOffset += crcBytes.Length;
        }

        _totalByteLength = currentByteOffset;
    }

    private uint CalculateChecksum()
    {
        var checksum = Crc32.Begin();
        var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(1024 * 1024);

        try
        {
            foreach (SparseChunk chunk in _mappedChunks)
            {
                var totalBytes = (long)chunk.Header.ChunkSize * _blockSize;
                switch (chunk.Header.ChunkType)
                {
                    case (ushort)ChunkType.Raw:
                        if (chunk.DataProvider != null)
                        {
                            long offset = 0;
                            while (offset < totalBytes)
                            {
                                var toProcess = (int)Math.Min(buffer.Length, totalBytes - offset);
                                var read = chunk.DataProvider.Read(offset, buffer, 0, toProcess);
                                if (read <= 0) break;
                                checksum = Crc32.Update(checksum, buffer, 0, read);
                                offset += read;
                            }
                        }
                        else
                        {
                            checksum = Crc32.UpdateZero(checksum, totalBytes);
                        }
                        break;

                    case (ushort)ChunkType.Fill:
                        checksum = Crc32.UpdateRepeated(checksum, chunk.FillValue, totalBytes);
                        break;

                    case (ushort)ChunkType.DontCare:
                        checksum = Crc32.UpdateZero(checksum, totalBytes);
                        break;
                }
            }
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
        }

        return Crc32.Finish(checksum);
    }

    private void MapChunks(SparseFile source, uint startBlock, uint blockCount, bool fullRange)
    {
        if (fullRange && startBlock > 0)
        {
            _mappedChunks.Add(new SparseChunk(new ChunkHeader
            {
                ChunkType = (ushort)ChunkType.DontCare,
                ChunkSize = startBlock,
                TotalSize = source.Header.ChunkHeaderSize
            }));
        }

        uint currentSrcBlock = 0;
        var endBlock = startBlock + blockCount;

        foreach (SparseChunk chunk in source.Chunks)
        {
            var chunkEnd = currentSrcBlock + chunk.Header.ChunkSize;

            if (chunkEnd > startBlock && currentSrcBlock < endBlock)
            {
                var intersectStart = Math.Max(startBlock, currentSrcBlock);
                var intersectEnd = Math.Min(endBlock, chunkEnd);
                var intersectCount = intersectEnd - intersectStart;

                // Validate source chunk header TotalSize before cloning
                long srcExpected = chunk.Header.ChunkType == (ushort)ChunkType.Raw
                    ? source.Header.ChunkHeaderSize + ((long)chunk.Header.ChunkSize * source.Header.BlockSize)
                    : chunk.Header.ChunkType == (ushort)ChunkType.Fill ? source.Header.ChunkHeaderSize + 4 : source.Header.ChunkHeaderSize;
                // debug: validated source chunk header matches expected
                if (chunk.Header.TotalSize != (uint)srcExpected)
                {
                    throw new InvalidOperationException($"Source chunk TotalSize mismatch: Type=0x{chunk.Header.ChunkType:X4}, ChunkSize={chunk.Header.ChunkSize}, HeaderChunkSize={source.Header.ChunkHeaderSize}, BlockSize={source.Header.BlockSize}, TotalSize(actual)={chunk.Header.TotalSize}, expected={srcExpected}");
                }

                SparseChunk mappedChunk = CloneChunkSlice(chunk, intersectStart - currentSrcBlock, intersectCount);
                _mappedChunks.Add(mappedChunk);
            }

            currentSrcBlock = chunkEnd;
            if (currentSrcBlock >= endBlock) break;
        }

        if (fullRange && endBlock < source.Header.TotalBlocks)
        {
            _mappedChunks.Add(new SparseChunk(new ChunkHeader
            {
                ChunkType = (ushort)ChunkType.DontCare,
                ChunkSize = source.Header.TotalBlocks - endBlock,
                TotalSize = source.Header.ChunkHeaderSize
            }));
        }
    }

    private SparseChunk CloneChunkSlice(SparseChunk original, uint offsetInBlocks, uint count)
    {
        ChunkHeader header = original.Header with
        {
            ChunkSize = count,
            TotalSize = original.Header.ChunkType == (ushort)ChunkType.Raw
                ? (uint)(_chunkHeaderSize + (count * _blockSize))
                : original.Header.ChunkType == (ushort)ChunkType.Fill ? (uint)(_chunkHeaderSize + 4) : (uint)_chunkHeaderSize
        };

        var newChunk = new SparseChunk(header) { FillValue = original.FillValue };

        if (original.DataProvider != null && header.ChunkType == (ushort)ChunkType.Raw)
        {
            newChunk.DataProvider = new SubDataProvider(original.DataProvider, (long)offsetInBlocks * _blockSize, (long)count * _blockSize);
        }

        return newChunk;
    }

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_position >= _totalByteLength) return 0;

        var totalRead = 0;
        while (totalRead < count && _position < _totalByteLength)
        {
            Section section = FindSectionAtOffset(_position);
            var offsetInSection = _position - section.StartByteOffset;
            var toRead = (int)Math.Min(count - totalRead, section.Length - offsetInSection);

            switch (section.Type)
            {
                case SectionType.SparseHeader:
                case SectionType.ChunkHeader:
                case SectionType.CrcHeader:
                case SectionType.CrcData:
                    Buffer.BlockCopy(section.StaticData!, (int)offsetInSection, buffer, offset + totalRead, toRead);
                    break;

                case SectionType.ChunkData:
                    SparseChunk chunk = _mappedChunks[section.ChunkIndex];
                    if (chunk.Header.ChunkType == (ushort)ChunkType.Raw)
                    {
                        if (chunk.DataProvider == null)
                        {
                            Array.Clear(buffer, offset + totalRead, toRead);
                            break;
                        }

                        var readTotal = 0;
                        while (readTotal < toRead)
                        {
                            var read = chunk.DataProvider.Read(offsetInSection + readTotal, buffer, offset + totalRead + readTotal, toRead - readTotal);
                            if (read <= 0)
                            {
                                throw new EndOfStreamException($"Sparse RAW chunk short read at image offset {_position}: {readTotal}/{toRead}");
                            }

                            readTotal += read;
                        }
                    }
                    else if (chunk.Header.ChunkType == (ushort)ChunkType.Fill)
                    {
                        var fillValue = chunk.FillValue;
                        for (var i = 0; i < toRead; i++)
                        {
                            var byteIdx = (int)((offsetInSection + i) % 4);
                            buffer[offset + totalRead + i] = (byte)(fillValue >> (byteIdx * 8));
                        }
                    }
                    else
                    {
                        Array.Clear(buffer, offset + totalRead, toRead);
                    }
                    break;
            }

            totalRead += toRead;
            _position += toRead;
        }

        return totalRead;
    }

    private Section FindSectionAtOffset(long pos)
    {
        int low = 0, high = _sections.Count - 1;
        while (low <= high)
        {
            var mid = low + ((high - low) / 2);
            Section sec = _sections[mid];
            if (pos >= sec.StartByteOffset && pos < sec.StartByteOffset + sec.Length)
            {
                return sec;
            }

            if (pos < sec.StartByteOffset)
            {
                high = mid - 1;
            }
            else
            {
                low = mid + 1;
            }
        }
        return _sections.Last();
    }

    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin)
    {
        switch (origin)
        {
            case SeekOrigin.Begin: _position = offset; break;
            case SeekOrigin.Current: _position += offset; break;
            case SeekOrigin.End: _position = _totalByteLength + offset; break;
        }
        _position = Math.Max(0, Math.Min(_totalByteLength, _position));
        return _position;
    }

    /// <inheritdoc/>
    public override bool CanRead => true;
    /// <inheritdoc/>
    public override bool CanSeek => true;
    /// <inheritdoc/>
    public override bool CanWrite => false;
    /// <inheritdoc/>
    public override long Length => _totalByteLength;
    /// <inheritdoc/>
    public override long Position { get => _position; set => Seek(value, SeekOrigin.Begin); }
    /// <inheritdoc/>
    public override void Flush() { }
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

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _ownedFile?.Dispose();
        }
        base.Dispose(disposing);
    }

    private class SubDataProvider : ISparseDataProvider
    {
        private readonly ISparseDataProvider parent;
        private readonly long offset;
        private readonly long length;

        public SubDataProvider(ISparseDataProvider parent, long offset, long length)
        {
            this.parent = parent;
            this.offset = offset;
            this.length = length;
        }

        public long Length => length;
        public int Read(long inOffset, byte[] buffer, int bufferOffset, int count)
        {
            return parent.Read(offset + inOffset, buffer, bufferOffset, (int)Math.Min(count, length - inOffset));
        }

        public int Read(long inOffset, Span<byte> buffer)
        {
            return parent.Read(offset + inOffset, buffer.Slice(0, (int)Math.Min(buffer.Length, length - inOffset)));
        }

        public void WriteTo(Stream stream)
        {
            throw new NotSupportedException();
        }

        public Task WriteToAsync(Stream stream, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public void Dispose() { }
        public ISparseDataProvider GetSubProvider(long subOffset, long subLength)
        {
            return new SubDataProvider(parent, offset + subOffset, subLength);
        }
    }
}
