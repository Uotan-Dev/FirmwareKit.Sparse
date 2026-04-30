using FirmwareKit.Sparse.Core;
using FirmwareKit.Sparse.IO;
using FirmwareKit.Sparse.Models;
using FirmwareKit.Sparse.DataProviders;
using System;
using System.Diagnostics;

namespace ResparseMemoryTest
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Resparse Method Memory Test (32-bit vs 64-bit)");
            Console.WriteLine("===============================================");
            Console.WriteLine();
            Console.WriteLine($"Process ID: {Process.GetCurrentProcess().Id}");
            Console.WriteLine($"Is 64-bit process: {Environment.Is64BitProcess}");
            Console.WriteLine();

            var chunkCounts = new[] { 100, 500, 1000, 2000, 3000 };
            var chunkSize = 50; // 50 blocks per chunk = 200KB
            var maxFileSize = 2 * 1024 * 1024; // 2MB

            Console.WriteLine($"Chunk size: {chunkSize} blocks ({chunkSize * 4096 / 1024} KB)");
            Console.WriteLine($"Max file size: {maxFileSize / (1024.0 * 1024.0):F2} MB");
            Console.WriteLine();

            foreach (var chunkCount in chunkCounts)
            {
                Console.WriteLine($"Testing with {chunkCount} chunks...");
                TestResparseMemory(chunkCount, chunkSize, maxFileSize);
                Console.WriteLine();
            }

            Console.WriteLine("Test completed successfully!");
        }

        static void TestResparseMemory(int chunkCount, int chunkSize, long maxFileSize)
        {
            try
            {
                var blockSize = 4096u;
                var totalSize = (long)chunkCount * chunkSize * blockSize;

                Console.WriteLine($"  Total file size: {totalSize / (1024.0 * 1024.0):F2} MB");

                var sparseFile = CreateSparseFile(chunkCount, chunkSize, blockSize);

                PrintMemoryUsage("After file creation");

                var stopwatch = Stopwatch.StartNew();

                var parts = sparseFile.Resparse(maxFileSize).ToList();

                stopwatch.Stop();

                PrintMemoryUsage("After resparse");

                Console.WriteLine($"  Parts created: {parts.Count}");
                Console.WriteLine($"  Execution time: {stopwatch.ElapsedMilliseconds} ms");
                Console.WriteLine($"  Peak memory: {Process.GetCurrentProcess().PeakWorkingSet64 / (1024.0 * 1024.0):F2} MB");
                Console.WriteLine($"  Status: ✓ Success");
            }
            catch (OutOfMemoryException)
            {
                Console.WriteLine($"  Status: ✗ OutOfMemoryException");
                Console.WriteLine($"  Peak memory: {Process.GetCurrentProcess().PeakWorkingSet64 / (1024.0 * 1024.0):F2} MB");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Status: ✗ {ex.GetType().Name}: {ex.Message}");
            }
        }

        static SparseFile CreateSparseFile(int chunkCount, int chunkSize, uint blockSize)
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
            var virtualMemory = process.VirtualMemorySize64 / (1024.0 * 1024.0);

            Console.WriteLine($"  {label}:");
            Console.WriteLine($"    Working Set: {workingSet:F2} MB");
            Console.WriteLine($"    Private Memory: {privateMemory:F2} MB");
            Console.WriteLine($"    Virtual Memory: {virtualMemory:F2} MB");
        }
    }
}