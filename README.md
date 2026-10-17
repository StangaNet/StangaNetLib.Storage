# StangaNetLib.Storage

[![.NET CI](https://github.com/StangaNet/StangaNetLib.Storage/actions/workflows/main.yml/badge.svg)](https://github.com/StangaNet/StangaNetLib.Storage/actions/workflows/main.yml)
![NuGet](https://img.shields.io/badge/nuget-1.0.0-blue)
![.NET](https://img.shields.io/badge/.NET-8.0%20%7C%209.0-512BD4)

StangaNetLib.Storage is a unified blob storage abstraction for .NET with Local filesystem, AWS S3, and Azure Blob providers. It supports streaming upload/download, pre-signed URLs, container lifecycle, and optional resilience via `StangaNetLib.Resilience`. All operations return `Result` / `Result<T>` from `StangaNetLib.Core`.

## Design Philosophy

- **Domain-Centric**: Storage concerns organized by capability (blob operations, pre-signed URLs, containers) rather than provider internals.
- **Functional Error Handling**: All I/O surfaces use `Result` / `Result<T>` from `StangaNetLib.Core`.
- **Defensive Programming**: Uses `Guard.Against` from Core for input validation.
- **Modern Standards**: Optimized for .NET 8.0 and 9.0 with optional Polly-based resilience pipelines.

## Installation

The package is hosted on **GitHub Packages**.

```xml
<!-- NuGet.config — add the GitHub Packages source -->
<configuration>
  <packageSources>
    <add key="github" value="https://nuget.pkg.github.com/StangaNet/index.json" />
  </packageSources>
</configuration>
```

```xml
<!-- .csproj -->
<PackageReference Include="StangaNetLib.Storage" Version="1.0.0" />
```

**Dependencies**: `StangaNetLib.Core`, `StangaNetLib.Resilience` (optional at runtime), `AWSSDK.S3`, `Azure.Storage.Blobs`, `Microsoft.Extensions.Options*`.

---

## Core Components

*The library is organized into functional namespaces. For implementation details, refer directly to the source code.*

| Namespace | Core Components | Purpose |
| :--- | :--- | :--- |
| `StangaNetLib.Storage.Abstractions` | `IBlobStorageService`, `IPresignedUrlService`, `IContainerManager` | Public contracts. |
| `StangaNetLib.Storage.Models` | `BlobUploadResult`, `BlobDownloadResult`, `BlobMetadata`, `BlobItem`, `PresignedUrl` | Domain models. |
| `StangaNetLib.Storage.Settings` | `StorageSettings`, `S3StorageSettings`, `AzureBlobStorageSettings` | Configuration. |
| `StangaNetLib.Storage.Implementations.Local` | `LocalStorageProvider` | Filesystem provider (dev/test). |
| `StangaNetLib.Storage.Implementations.S3` | `S3StorageProvider` | AWS S3 / S3-compatible. |
| `StangaNetLib.Storage.Implementations.AzureBlob` | `AzureBlobStorageProvider` | Azure Blob Storage. |
| `StangaNetLib.Storage.Extensions` | `ServiceCollectionExtensions` | DI registration. |

---

## Project Structure

```
StangaNetLib.Storage/
├── src/
│   └── StangaNetLib.Storage/
│       ├── Abstractions/       # Public interfaces
│       ├── Extensions/         # DI registration
│       ├── Implementations/    # Local, S3, AzureBlob
│       ├── Models/             # Upload/download results, metadata
│       └── Settings/           # Configuration models
└── tests/
    └── StangaNetLib.Storage.Tests/
```

---

## Quick Start

### Local (development)

```csharp
builder.Services.AddStangaNetLibStorageLocal(basePath: "wwwroot/storage");
```

### From configuration

```json
{
  "StorageSettings": {
    "Provider": "Local",
    "LocalBasePath": "wwwroot/storage",
    "LocalBaseUrl": "https://myapp.com/storage"
  }
}
```

```csharp
builder.Services.AddStangaNetLibStorage(builder.Configuration);
// Optional: builder.Services.AddStangaNetLibResilience(...) for pipeline "Storage"
```

### Upload example

```csharp
public class MediaService(IBlobStorageService storage)
{
    public async Task<Result<BlobUploadResult>> SaveAsync(Stream stream, string key, CancellationToken ct)
        => await storage.UploadAsync("media", key, stream, "image/jpeg", ct: ct);
}
```

## About

Unified blob storage abstraction for S3, Azure Blob, and local filesystem with streaming, pre-signed URLs, and object lifecycle management.
