using FirmwareKit.Sparse.Core;
using FirmwareKit.Sparse.IO;
using System;
using System.Diagnostics;
using System.IO;

namespace MemoryTest
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: MemoryTest <simg_file> <max_file_size_in_bytes>");
                return;
            }

            string filePath = args[0];
            long maxFileSize = long.Parse(args[1]);

            Console.WriteLine($"Testing resparse on file: {filePath}");
            Console.WriteLine($"Max file size: {maxFileSize} bytes");
            Console.WriteLine($"Process ID: {Process.GetCurrentProcess().Id}");
            Console.WriteLine($"Is 64-bit process: {Environment.Is64BitProcess}");
            Console.WriteLine();

            try
            {
                var stopwatch = Stopwatch.StartNew();

                Console.WriteLine("Loading sparse file...");
                using var sparseFile = SparseFile.ImportAuto(filePath);
                Console.WriteLine($"Loaded sparse file with {sparseFile.Chunks.Count} chunks");
                Console.WriteLine($"Total blocks: {sparseFile.Header.TotalBlocks}");
                Console.WriteLine($"Block size: {sparseFile.Header.BlockSize} bytes");
                Console.WriteLine($"Total size: {(long)sparseFile.Header.TotalBlocks * sparseFile.Header.BlockSize / (1024.0 * 1024.0):F2} MB");
                Console.WriteLine();

                PrintMemoryUsage("After loading file");

                Console.WriteLine("Starting resparse operation...");
                var parts = sparseFile.Resparse(maxFileSize).ToList();
                Console.WriteLine($"Resparse completed: {parts.Count} parts created");
                Console.WriteLine();

                PrintMemoryUsage("After resparse");

                stopwatch.Stop();
                Console.WriteLine($"Total time: {stopwatch.ElapsedMilliseconds} ms");
                Console.WriteLine($"Peak memory usage: {Process.GetCurrentProcess().PeakWorkingSet64 / (1024.0 * 1024.0):F2} MB");

                Console.WriteLine("Test completed successfully!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                Environment.Exit(1);
            }
        }

        static void PrintMemoryUsage(string label)
        {
            var process = Process.GetCurrentProcess();
            var workingSet = process.WorkingSet64 / (1024.0 * 1024.0);
            var privateMemory = process.PrivateMemorySize64 / (1024.0 * 1024.0);
            var virtualMemory = process.VirtualMemorySize64 / (1024.0 * 1024.0);

            Console.WriteLine($"{label}:");
            Console.WriteLine($"  Working Set: {workingSet:F2} MB");
            Console.WriteLine($"  Private Memory: {privateMemory:F2} MB");
            Console.WriteLine($"  Virtual Memory: {virtualMemory:F2} MB");
            Console.WriteLine();
        }
    }
}