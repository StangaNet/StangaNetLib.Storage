# Changelog

All notable changes to **StangaNetLib.Storage** are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]


## [1.0.2] - 2026-10-23

### Changed
- Add `net10.0` target framework alongside `net8.0` and `net9.0`.
- Require .NET SDK 10 (global.json) to multi-target `net8.0` / `net9.0` / `net10.0`; opt in Microsoft.Testing.Platform as the `dotnet test` runner.

## [1.0.1] - 2026-10-18

### Fixed
- Republish with Microsoft.Extensions.* 8.x and Azure.Storage.Blobs 12.22.2 dependency graph. GitHub Packages retained a prior `1.0.0` nuspec that pulled ME 10.x / Blobs 12.29.x / Resilience 1.0.1, causing NU1605 downgrades in consumers (e.g. Media).

## [1.0.0] - 2026-10-17

### Added
- Unified blob storage abstraction: Local filesystem, AWS S3 / S3-compatible, Azure Blob.
- Streaming upload/download, metadata, list/copy/move, container lifecycle, pre-signed URLs.
- DI registration (`AddStangaNetLibStorage` / `AddStangaNetLibStorageLocal`) with options validation.
- Optional resilience via `StangaNetLib.Resilience` pipeline `"Storage"` on S3 and Azure providers.
- Dual targeting `net8.0` / `net9.0`, XML docs, symbols (`snupkg`), package icon, and MSBuild consumer metadata stamp.
- Depends on `StangaNetLib.Resilience` 1.0.0 (brings `StangaNetLib.Core`); Microsoft.Extensions.* pinned to 8.x.
- CI via shared StangaNet reusable workflow (push / PR / manual; publish outside PRs).

[Unreleased]: https://github.com/StangaNet/StangaNetLib.Storage/compare/v1.0.2...HEAD
[1.0.2]: https://github.com/StangaNet/StangaNetLib.Storage/compare/v1.0.1...v1.0.2
[1.0.1]: https://github.com/StangaNet/StangaNetLib.Storage/compare/v1.0.0...v1.0.1
[1.0.0]: https://github.com/StangaNet/StangaNetLib.Storage/releases/tag/v1.0.0
