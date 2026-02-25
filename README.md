# FirmwareKit.Sparse

A high-performance .NET library for parsing, editing, and converting Android Sparse images.

[![NuGet version](https://img.shields.io/nuget/v/FirmwareKit.Sparse.svg)](https://www.nuget.org/packages/FirmwareKit.Sparse)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

## Installation

```bash
dotnet add package FirmwareKit.Sparse
```

## Basic Usage

### Reading a Sparse Image

```csharp
using FirmwareKit.Sparse;

// Parse a sparse image file
using var sparseFile = SparseFile.FromImageFile("system.simg");

// Access as a read-only Stream
using var stream = new SparseStream(sparseFile);
var buffer = new byte[4096];
stream.Read(buffer, 0, buffer.Length);
```

### Converting Sparse to Raw

```csharp
using FirmwareKit.Sparse;

SparseImageConverter.ConvertSparseToRaw(new[] { "system.img" }, "system.raw.img");
```

### Resparsing (Splitting)

```csharp
using FirmwareKit.Sparse;

using var sparseFile = SparseFile.FromImageFile("massive_system.simg");
// Split into multiple sparse files, each max 512MB
var smallerFiles = sparseFile.Resparse(512 * 1024 * 1024);

foreach (var file in smallerFiles)
{
    using var fs = new FileStream($"part_{file.Header.ImageChecksum}.img", FileMode.Create);
    file.WriteToStream(fs);
}
```

## License

This project is licensed under the MIT License.
