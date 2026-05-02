using FirmwareKit.Sparse.Core;
using Xunit;

namespace FirmwareKit.Sparse.Tests;

public class ResparseSplitFilesTests
{
    [Fact]
    public void Resparse_Matches_Simg2Simg_Splits()
    {
        var baseDir = AppContext.BaseDirectory;
        var full = Path.Combine(baseDir, "simg.simg");
        Assert.True(File.Exists(full), $"Full sample not found: {full}");

        using var sparseFile = SparseFile.ImportAuto(full);
        var originalDesc = string.Join(", ", sparseFile.Chunks.Select(c => $"{((Models.ChunkType)c.Header.ChunkType)}:{c.Header.ChunkSize}@{c.StartBlock}"));
        var parts = sparseFile.Resparse(2048000).ToList();

        var splitFiles = Directory.GetFiles(baseDir, "simg-split.simg.*")
                                 .OrderBy(f => int.Parse(Path.GetFileName(f).Split('.').Last()))
                                 .ToList();

        // Try to match each produced part against any of the expected split files (order may differ)
        var remaining = splitFiles.ToList();
        var producedAllDesc = parts.Select(p => string.Join(", ", p.Chunks.Select(c => $"{((Models.ChunkType)c.Header.ChunkType)}:{c.Header.ChunkSize}@{c.StartBlock}"))).ToList();
        var expectedAllDesc = splitFiles.Select(f => string.Join(", ", SparseFile.FromImageFile(f).Chunks.Select(c => $"{((Models.ChunkType)c.Header.ChunkType)}:{c.Header.ChunkSize}@{c.StartBlock}"))).ToList();

        for (int i = 0; i < parts.Count; i++)
        {
            using var ms = new MemoryStream();
            parts[i].WriteToStream(ms, sparse: true, includeCrc: false);
            var produced = ms.ToArray();

            var matchIndex = -1;
            for (int j = 0; j < remaining.Count; j++)
            {
                var expected = File.ReadAllBytes(remaining[j]);
                if (expected.SequenceEqual(produced))
                {
                    matchIndex = j;
                    break;
                }
            }

            if (matchIndex >= 0)
            {
                // remove matched file from the remaining list
                remaining.RemoveAt(matchIndex);
                continue;
            }

            // strict compare only: no fallback to raw comparison
            // no match found -> diagnostic information
            var expectedHeader = remaining.Count > 0 ? SparseFile.PeekHeader(remaining[0]) : default;
            var producedHeader = FirmwareKit.Sparse.Models.SparseHeader.FromBytes(produced.AsSpan(0, FirmwareKit.Sparse.Models.SparseFormat.SparseHeaderSize));
            var expectedDesc = remaining.Count > 0 ? string.Join(", ", SparseFile.FromImageFile(remaining[0]).Chunks.Select(c => $"{((Models.ChunkType)c.Header.ChunkType)}:{c.Header.ChunkSize}@{c.StartBlock}")) : "(none)";
            var producedDesc = string.Join(", ", parts[i].Chunks.Select(c => $"{((Models.ChunkType)c.Header.ChunkType)}:{c.Header.ChunkSize}@{c.StartBlock}"));

            var producedListText = string.Join("\n", producedAllDesc.Select((d, idx) => $"part[{idx}]: {d}"));
            var expectedListText = string.Join("\n", expectedAllDesc.Select((d, idx) => $"file[{idx}]: {d}"));
            Assert.Fail($"Produced part #{i} did not match any expected split file.\nOriginal chunks: {originalDesc}\nProduced header: Blocks={producedHeader.TotalBlocks}, Chunks={producedHeader.TotalChunks}, Checksum=0x{producedHeader.ImageChecksum:X8}\nProduced chunks: {producedDesc}\nSample expected header (first remaining): Blocks={expectedHeader.TotalBlocks}, Chunks={expectedHeader.TotalChunks}, Checksum=0x{expectedHeader.ImageChecksum:X8}\nSample expected chunks: {expectedDesc}\nAll produced parts:\n{producedListText}\nAll expected files:\n{expectedListText}");
        }

        Assert.Empty(remaining);
    }
}
