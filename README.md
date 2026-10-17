# StangaNetLib.Storage

[![.NET CI](https://github.com/StangaNet/StangaNetLib.Storage/actions/workflows/main.yml/badge.svg)](https://github.com/StangaNet/StangaNetLib.Storage/actions/workflows/main.yml)
![NuGet](https://img.shields.io/badge/nuget-1.0.0-blue)
![.NET](https://img.shields.io/badge/.NET-8.0%20%7C%209.0-512BD4)

Unified blob storage abstraction for Local filesystem, AWS S3 (and S3-compatible), and Azure Blob. Supports streaming upload/download, pre-signed URLs, container lifecycle, and optional resilience via [`StangaNetLib.Resilience`](https://github.com/StangaNet/StangaNetLib.Resilience). Operations return `Result` / `Result<T>` from [`StangaNetLib.Core`](https://github.com/StangaNet/StangaNetLib.Core).

## Design Philosophy

- **Domain-centric**: blob ops, pre-signed URLs, and containers as capabilities.
- **Functional failures**: expected outcomes map to `Result` / `Result<T>`.
- **Pluggable backends**: Local, S3, Azure Blob behind the same contracts.
- **Modern .NET**: `net8.0` / `net9.0` with optional Polly-based resilience.

## Installation

The package is hosted on **GitHub Packages** (pulls Resilience / Core from the same feed).

```xml
<!-- NuGet.config — source key must match your packageSourceCredentials -->
<configuration>
  <packageSources>
    <add key="StangaNet" value="https://nuget.pkg.github.com/StangaNet/index.json" />
  </packageSources>
</configuration>
```

```xml
<!-- .csproj -->
<PackageReference Include="StangaNetLib.Storage" Version="1.0.1" />
```

See [CHANGELOG.md](./CHANGELOG.md) for release history.

## Core Components

| Namespace | Components | Purpose |
|---|---|---|
| `StangaNetLib.Storage.Abstractions` | `IBlobStorageService`, `IPresignedUrlService`, `IContainerManager` | Public contracts |
| `StangaNetLib.Storage.Models` | `BlobUploadResult`, `BlobDownloadResult`, `BlobMetadata`, … | Domain models |
| `StangaNetLib.Storage.Settings` | `StorageSettings`, `S3StorageSettings`, `AzureBlobStorageSettings` | Configuration |
| `StangaNetLib.Storage.Implementations.*` | Local / S3 / AzureBlob | Providers |
| `StangaNetLib.Storage.Extensions` | `AddStangaNetLibStorage`, `AddStangaNetLibStorageLocal` | DI |

## Project Structure

```
StangaNetLib.Storage/
├── assets/
│   └── package-icon.png
├── build/
│   └── StangaNetLib.Storage.props
├── src/
│   └── StangaNetLib.Storage/
│       ├── Abstractions/
│       ├── Extensions/
│       ├── Implementations/
│       ├── Models/
│       └── Settings/
└── tests/
    └── StangaNetLib.Storage.Tests/
```

## Quick Start

```csharp
builder.Services.AddStangaNetLibStorageLocal(basePath: "wwwroot/storage");
// or: builder.Services.AddStangaNetLibStorage(builder.Configuration);
// optional: builder.Services.AddStangaNetLibResilience(...) for pipeline "Storage"
```

```csharp
public class MediaService(IBlobStorageService storage)
{
    public Task<Result<BlobUploadResult>> SaveAsync(Stream stream, string key, CancellationToken ct)
        => storage.UploadAsync("media", key, stream, "image/jpeg", ct: ct);
}
```

## CI/CD

On push/PR to `main` and `workflow_dispatch`, the shared StangaNet workflow:

1. Restores, builds, and tests on .NET 8 and .NET 9
2. Packs the NuGet package (with symbols)
3. Publishes to GitHub Packages outside pull requests
4. Uploads the `.nupkg` as a build artifact
