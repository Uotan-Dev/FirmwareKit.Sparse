using System.Buffers;

namespace FirmwareKit.Sparse.Streams;

/// <summary>
/// A stream that maps sparse chunks back into a complete sparse image format.
/// <para>将稀疏数据块映射回完整稀疏镜像格式的流。</para>
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
    /// <para>初始化 SparseImageStream 类的新实例。</para>
    /// </summary>
    /// <param name="source">The source sparse file. <para>源稀疏文件。</para></param>
    /// <param name="startBlock">The absolute starting block offset. <para>绝对起始块偏移。</para></param>
    /// <param name="blockCount">The number of blocks in the stream. <para>流中的块数量。</para></param>
    /// <param name="includeCrc">Whether to append a CRC32 checksum chunk. <para>是否追加 CRC32 校验和数据块。</para></param>
    /// <param name="fullRange">Whether to maintain the original file's TotalBlocks and pad with "skip" chunks. <para>是否保持原始文件的 TotalBlocks 并用跳过块填充。</para></param>
    /// <param name="disposeSource">Whether to dispose the source file when this stream is disposed. <para>释放此流时是否释放源文件。</para></param>
    public SparseImageStream(SparseFile source, uint startBlock, uint blockCount, bool includeCrc = false, bool fullRange = true, bool disposeSource = false)
    {
        _blockSize = source.Header.BlockSize;
        _ownedFile = disposeSource ? source : null;

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

        for (int i = 0; i < _mappedChunks.Count; i++)
        {
            var c = _mappedChunks[i];
            long expected = ChunkHelper.GetDiskSize(c.Header.ChunkType, c.Header.ChunkSize, (ushort)_chunkHeaderSize, _blockSize);
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

    /// <summary>
    /// Calculates the CRC32 checksum over all mapped chunks.
    /// <para>计算所有映射数据块的 CRC32 校验和。</para>
    /// </summary>
    /// <returns>The finished CRC32 checksum value. <para>完成的 CRC32 校验和值。</para></returns>
    private uint CalculateChecksum()
    {
        var checksum = Crc32.Begin();
        var buffer = ArrayPool<byte>.Shared.Rent(1024 * 1024);

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
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return Crc32.Finish(checksum);
    }

    /// <summary>
    /// Maps chunks from the source sparse file into the specified block range.
    /// <para>将源稀疏文件中的数据块映射到指定的块范围。</para>
    /// </summary>
    /// <param name="source">The source sparse file. <para>源稀疏文件。</para></param>
    /// <param name="startBlock">The starting block offset. <para>起始块偏移。</para></param>
    /// <param name="blockCount">The number of blocks to map. <para>要映射的块数量。</para></param>
    /// <param name="fullRange">Whether to pad with DontCare chunks for the full range. <para>是否用 DontCare 块填充完整范围。</para></param>
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

                long srcExpected = ChunkHelper.GetDiskSize(chunk.Header.ChunkType, chunk.Header.ChunkSize, source.Header.ChunkHeaderSize, source.Header.BlockSize);
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

    /// <summary>
    /// Clones a slice of a chunk for the specified block range.
    /// <para>克隆指定块范围的数据块切片。</para>
    /// </summary>
    /// <param name="original">The original chunk to slice. <para>要切片的原始数据块。</para></param>
    /// <param name="offsetInBlocks">Block offset within the original chunk. <para>原始数据块内的块偏移。</para></param>
    /// <param name="count">Number of blocks in the slice. <para>切片中的块数量。</para></param>
    /// <returns>A new <see cref="SparseChunk"/> representing the slice. <para>表示切片的新 SparseChunk。</para></returns>
    private SparseChunk CloneChunkSlice(SparseChunk original, uint offsetInBlocks, uint count)
    {
        ChunkHeader header = original.Header with
        {
            ChunkSize = count,
            TotalSize = ChunkHelper.GetExpectedTotalSize(original.Header.ChunkType, count, (ushort)_chunkHeaderSize, _blockSize)
        };

        var newChunk = new SparseChunk(header) { FillValue = original.FillValue };

        if (original.DataProvider != null && header.ChunkType == (ushort)ChunkType.Raw)
        {
            newChunk.DataProvider = new SubDataProvider(original.DataProvider, (long)offsetInBlocks * _blockSize, (long)count * _blockSize);
        }

        return newChunk;
    }

    /// <summary>
    /// Reads bytes from the sparse image stream into the provided buffer.
    /// <para>从稀疏镜像流读取字节到提供的缓冲区。</para>
    /// </summary>
    /// <param name="buffer">Destination buffer to receive data. <para>接收数据的目标缓冲区。</para></param>
    /// <param name="offset">Offset in the buffer to start writing. <para>缓冲区中的起始写入偏移。</para></param>
    /// <param name="count">Maximum number of bytes to read. <para>最大读取字节数。</para></param>
    /// <returns>The number of bytes actually read. <para>实际读取的字节数。</para></returns>
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

    /// <summary>
    /// Finds the section that contains the specified byte offset using binary search.
    /// <para>使用二分查找定位包含指定字节偏移的区段。</para>
    /// </summary>
    /// <param name="pos">The byte offset to locate. <para>要定位的字节偏移。</para></param>
    /// <returns>The <see cref="Section"/> containing the offset. <para>包含该偏移的 Section。</para></returns>
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

    /// <summary>
    /// Seeks to a specific position within the generated sparse image stream.
    /// <para>在生成的稀疏镜像流中定位到指定位置。</para>
    /// </summary>
    /// <param name="offset">Offset to seek to relative to <paramref name="origin"/>. <para>相对于 origin 的定位偏移。</para></param>
    /// <param name="origin">Reference point used to obtain the new position. <para>用于获取新位置的参考点。</para></param>
    /// <returns>The new position within the stream. <para>流中的新位置。</para></returns>
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

    /// <summary>Indicates whether this stream supports reading. Always true. <para>指示此流是否支持读取。始终为 true。</para></summary>
    public override bool CanRead => true;

    /// <summary>Indicates whether this stream supports seeking. Always true. <para>指示此流是否支持定位。始终为 true。</para></summary>
    public override bool CanSeek => true;

    /// <summary>Indicates whether this stream supports writing. Always false. <para>指示此流是否支持写入。始终为 false。</para></summary>
    public override bool CanWrite => false;

    /// <summary>Gets the total length, in bytes, of the generated sparse image stream. <para>获取生成的稀疏镜像流的总字节长度。</para></summary>
    public override long Length => _totalByteLength;

    /// <summary>Gets or sets the current position within the generated sparse image stream. <para>获取或设置生成的稀疏镜像流中的当前位置。</para></summary>
    public override long Position { get => _position; set => Seek(value, SeekOrigin.Begin); }

    /// <summary>Flush has no effect for read-only stream. <para>刷新对只读流无效。</para></summary>
    public override void Flush() { }

    /// <summary>Setting length is not supported for this read-only stream. <para>此只读流不支持设置长度。</para></summary>
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <summary>Writing is not supported for this read-only stream. <para>此只读流不支持写入。</para></summary>
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /// <summary>
    /// Disposes managed resources used by the stream instance.
    /// If this stream owns the underlying <see cref="SparseFile"/>, it will be disposed.
    /// <para>释放流实例使用的托管资源。
    /// 如果此流拥有底层 SparseFile，则一并释放。</para>
    /// </summary>
    /// <param name="disposing">True when called from <see cref="Dispose"/>. <para>从 Dispose 调用时为 true。</para></param>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _ownedFile?.Dispose();
        }
        base.Dispose(disposing);
    }
}
