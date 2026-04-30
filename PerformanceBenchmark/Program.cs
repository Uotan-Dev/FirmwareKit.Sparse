using FirmwareKit.Sparse.Core;
using FirmwareKit.Sparse.IO;
using FirmwareKit.Sparse.Models;
using FirmwareKit.Sparse.DataProviders;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace PerformanceBenchmark
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("FirmwareKit.Sparse Performance Benchmark");
            Console.WriteLine("========================================");
            Console.WriteLine();
            Console.WriteLine($"Process: {Process.GetCurrentProcess().Id}");
            Console.WriteLine($"64-bit: {Environment.Is64BitProcess}");
            Console.WriteLine($"Date: {DateTime.Now}");
            Console.WriteLine();

            // Test configurations
            var testConfigs = new[]
            {
                new { Name = "Small", ChunkCount = 100, ChunkSizeKB = 100 },
                new { Name = "Medium", ChunkCount = 1000, ChunkSizeKB = 100 },
                new { Name = "Large", ChunkCount = 10000, ChunkSizeKB = 100 },
                new { Name = "XLarge", ChunkCount = 50000, ChunkSizeKB = 100 },
            };

            // Run all benchmarks
            Console.WriteLine("1. File Creation Benchmark");
            Console.WriteLine("--------------------------");
            foreach (var config in testConfigs)
            {
                RunCreationBenchmark(config.Name, config.ChunkCount, config.ChunkSizeKB);
            }

            Console.WriteLine();
            Console.WriteLine("2. File Serialization Benchmark");
            Console.WriteLine("-------------------------------");
            foreach (var config in testConfigs)
            {
                RunSerializationBenchmark(config.Name, config.ChunkCount, config.ChunkSizeKB);
            }

            Console.WriteLine();
            Console.WriteLine("3. Resparse Benchmark");
            Console.WriteLine("---------------------");
            foreach (var config in testConfigs)
            {
                RunResparseBenchmark(config.Name, config.ChunkCount, config.ChunkSizeKB);
            }

            Console.WriteLine();
            Console.WriteLine("4. Raw Export Benchmark");
            Console.WriteLine("-----------------------");
            foreach (var config in testConfigs)
            {
                RunRawExportBenchmark(config.Name, config.ChunkCount, config.ChunkSizeKB);
            }

            Console.WriteLine();
            Console.WriteLine("Benchmark completed!");
        }

        static void RunCreationBenchmark(string name, int chunkCount, int chunkSizeKB)
        {
            var blockSize = 4096u;
            var chunkBytes = chunkSizeKB * 1024;
            var chunkBlocks = (uint)(chunkBytes / blockSize);

            Console.Write($"  {name} ({chunkCount:N0} chunks): ");

            var sw = Stopwatch.StartNew();
            var memBefore = GC.GetTotalMemory(true);

            var sparseFile = new SparseFile(blockSize, (long)chunkCount * chunkBytes);
            for (int i = 0; i < chunkCount; i++)
            {
                var data = new byte[chunkBytes];
                for (int j = 0; j < data.Length; j++)
                    data[j] = (byte)(i % 256);
                sparseFile.AddRawChunk(data, (uint)(i * chunkBlocks));
            }

            sw.Stop();
            var memAfter = GC.GetTotalMemory(true);

            var cpuTime = Process.GetCurrentProcess().TotalProcessorTime;

            Console.WriteLine($"Time: {sw.ElapsedMilliseconds,6} ms | Memory: {(memAfter - memBefore) / 1024 / 1024,6:F2} MB");

            sparseFile.Dispose();
        }

        static void RunSerializationBenchmark(string name, int chunkCount, int chunkSizeKB)
        {
            var blockSize = 4096u;
            var chunkBytes = chunkSizeKB * 1024;
            var chunkBlocks = (uint)(chunkBytes / blockSize);

            Console.Write($"  {name} ({chunkCount:N0} chunks): ");

            // Create test file
            var sparseFile = new SparseFile(blockSize, (long)chunkCount * chunkBytes);
            for (int i = 0; i < chunkCount; i++)
            {
                var data = new byte[chunkBytes];
                sparseFile.AddRawChunk(data, (uint)(i * chunkBlocks));
            }

            var tempFile = Path.GetTempFileName();
            var sw = Stopwatch.StartNew();
            using (var fs = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.SequentialScan))
            {
                sparseFile.WriteToStream(fs, sparse: true);
            }
            sw.Stop();

            File.Delete(tempFile);
            Console.WriteLine($"Time: {sw.ElapsedMilliseconds,6} ms");

            sparseFile.Dispose();
        }

        static void RunResparseBenchmark(string name, int chunkCount, int chunkSizeKB)
        {
            var blockSize = 4096u;
            var chunkBytes = chunkSizeKB * 1024;
            var chunkBlocks = (uint)(chunkBytes / blockSize);

            Console.Write($"  {name} ({chunkCount:N0} chunks): ");

            // Create test file
            var sparseFile = new SparseFile(blockSize, (long)chunkCount * chunkBytes);
            for (int i = 0; i < chunkCount; i++)
            {
                var data = new byte[chunkBytes];
                sparseFile.AddRawChunk(data, (uint)(i * chunkBlocks));
            }

            var sw = Stopwatch.StartNew();
            var parts = sparseFile.Resparse(2 * 1024 * 1024).ToList();
            sw.Stop();

            Console.WriteLine($"Time: {sw.ElapsedMilliseconds,6} ms | Parts: {parts.Count,3}");

            sparseFile.Dispose();
            foreach (var part in parts) part.Dispose();
        }

        static void RunRawExportBenchmark(string name, int chunkCount, int chunkSizeKB)
        {
            var blockSize = 4096u;
            var chunkBytes = chunkSizeKB * 1024;
            var chunkBlocks = (uint)(chunkBytes / blockSize);

            Console.Write($"  {name} ({chunkCount:N0} chunks): ");

            // Create test file
            var sparseFile = new SparseFile(blockSize, (long)chunkCount * chunkBytes);
            for (int i = 0; i < chunkCount; i++)
            {
                var data = new byte[chunkBytes];
                sparseFile.AddRawChunk(data, (uint)(i * chunkBlocks));
            }

            var tempFile = Path.GetTempFileName();
            var sw = Stopwatch.StartNew();
            using (var fs = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.SequentialScan))
            {
                sparseFile.WriteRawToStream(fs);
            }
            sw.Stop();

            File.Delete(tempFile);
            Console.WriteLine($"Time: {sw.ElapsedMilliseconds,6} ms");

            sparseFile.Dispose();
        }
    }
}