using FirmwareKit.Sparse.Core;
using FirmwareKit.Sparse.IO;
using FirmwareKit.Sparse.Models;
using FirmwareKit.Sparse.DataProviders;
using System;
using System.Diagnostics;
using System.IO;

namespace LargeFileTest
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Large File Resparse Memory Test");
            Console.WriteLine("=================================");
            Console.WriteLine();

            var testSizes = new[] { 100, 500, 1000, 5000, 10000 };
            var chunkSize = 100; // 100 blocks per chunk = 400KB
            var maxFileSize = 2 * 1024 * 1024; // 2MB

            foreach (var chunkCount in testSizes)
            {
                Console.WriteLine($"Testing with {chunkCount} chunks...");
                TestWithChunkCount(chunkCount, chunkSize, maxFileSize);
                Console.WriteLine();
            }
        }

        static void TestWithChunkCount(int chunkCount, int chunkSize, long maxFileSize)
        {
            try
            {
                var blockSize = 4096u;
                var totalSize = (long)chunkCount * chunkSize * blockSize;

                Console.WriteLine($"  Creating test file: {totalSize / (1024.0 * 1024.0):F2} MB");

                var sparseFile = CreateTestSparseFile(chunkCount, chunkSize, blockSize);
                var stopwatch = Stopwatch.StartNew();

                PrintMemoryUsage("Before resparse");

                var parts = sparseFile.Resparse(maxFileSize).ToList();

                stopwatch.Stop();

                PrintMemoryUsage("After resparse");

                Console.WriteLine($"  Resparse completed: {parts.Count} parts created");
                Console.WriteLine($"  Execution time: {stopwatch.ElapsedMilliseconds} ms");
                Console.WriteLine($"  Peak memory: {Process.GetCurrentProcess().PeakWorkingSet64 / (1024.0 * 1024.0):F2} MB");
                Console.WriteLine($"  Success: ✓");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Error: {ex.Message}");
                Console.WriteLine($"  Success: ✗");
            }
        }

        static SparseFile CreateTestSparseFile(int chunkCount, int chunkSize, uint blockSize)
        {
            var totalSize = (long)chunkCount * chunkSize * blockSize;
            var sparseFile = new SparseFile(blockSize, totalSize);

            for (int i = 0; i < chunkCount; i++)
            {
                var data = new byte[chunkSize * blockSize];
                for (int j = 0; j < data.Length; j++)
                {
                    data[j] = (byte)(i % 256);
                }

                sparseFile.AddRawChunk(data, (uint)(i * chunkSize));
            }

            return sparseFile;
        }

        static void PrintMemoryUsage(string label)
        {
            var process = Process.GetCurrentProcess();
            var workingSet = process.WorkingSet64 / (1024.0 * 1024.0);
            var privateMemory = process.PrivateMemorySize64 / (1024.0 * 1024.0);

            Console.WriteLine($"  {label}:");
            Console.WriteLine($"    Working Set: {workingSet:F2} MB");
            Console.WriteLine($"    Private Memory: {privateMemory:F2} MB");
        }
    }
}