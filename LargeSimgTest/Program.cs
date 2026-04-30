using FirmwareKit.Sparse.Core;
using FirmwareKit.Sparse.IO;
using FirmwareKit.Sparse.Models;
using FirmwareKit.Sparse.DataProviders;
using System;
using System.Diagnostics;
using System.IO;

namespace LargeSimgTest
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Large SIMG File Resparse Test (32-bit AOT Optimized)");
            Console.WriteLine("==================================================");
            Console.WriteLine();
            Console.WriteLine($"Process ID: {Process.GetCurrentProcess().Id}");
            Console.WriteLine($"Is 64-bit process: {Environment.Is64BitProcess}");
            Console.WriteLine();

            // Test parameters simulating large files
            var testScenarios = new[]
            {
                new { ChunkCount = 10000, ChunkSizeKB = 100, MaxFileSizeMB = 2 },
                new { ChunkCount = 50000, ChunkSizeKB = 100, MaxFileSizeMB = 2 },
                new { ChunkCount = 100000, ChunkSizeKB = 100, MaxFileSizeMB = 2 },
            };

            foreach (var scenario in testScenarios)
            {
                Console.WriteLine($"Testing {scenario.ChunkCount:N0} chunks ({scenario.ChunkSizeKB} KB each)");
                Console.WriteLine($"Max output file size: {scenario.MaxFileSizeMB} MB");
                TestResparseOptimized(scenario.ChunkCount, scenario.ChunkSizeKB, scenario.MaxFileSizeMB * 1024 * 1024L);
                Console.WriteLine();
            }

            Console.WriteLine("Test completed!");
        }

        static void TestResparseOptimized(int chunkCount, int chunkSizeKB, long maxFileSize)
        {
            try
            {
                var blockSize = 4096u;
                var chunkBytes = chunkSizeKB * 1024;
                var chunksPerBlock = (int)(chunkBytes / blockSize);
                
                // Create test sparse file
                var testFile = CreateTestSparseFile(chunkCount, chunksPerBlock, blockSize);
                Console.WriteLine($"Created test file with {chunkCount:N0} chunks");
                
                PrintMemoryUsage("After file creation");

                // Test streaming resparse
                Console.WriteLine("\nTesting Streamed Resparse...");
                using (var stream = new MemoryStream())
                {
                    testFile.WriteToStream(stream, sparse: true);
                    stream.Position = 0;
                    
                    var stopwatch = Stopwatch.StartNew();
                    var parts = SparseFile.ResparseStreamed(stream, maxFileSize).ToList();
                    stopwatch.Stop();
                    
                    PrintMemoryUsage("After streamed resparse");
                    Console.WriteLine($"  Parts created: {parts.Count:N0}");
                    Console.WriteLine($"  Execution time: {stopwatch.ElapsedMilliseconds} ms");
                    Console.WriteLine($"  Status: ✓ Success");
                }

                // Cleanup
                testFile.Dispose();
            }
            catch (OutOfMemoryException)
            {
                Console.WriteLine("  Status: ✗ OutOfMemoryException");
                PrintMemoryUsage("At failure");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Status: ✗ {ex.GetType().Name}: {ex.Message}");
            }
        }

        static SparseFile CreateTestSparseFile(int chunkCount, int chunksPerBlock, uint blockSize)
        {
            var totalBlocks = (uint)(chunkCount * chunksPerBlock);
            var sparseFile = new SparseFile(blockSize, (long)totalBlocks * blockSize);

            // Use memory-efficient data provider
            for (int i = 0; i < chunkCount; i++)
            {
                uint startBlock = (uint)(i * chunksPerBlock);
                
                // Create fill chunk instead of raw to save memory
                sparseFile.AddFillChunk((uint)i, (long)chunksPerBlock * blockSize, startBlock);
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