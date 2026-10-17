# Changelog

All notable changes to **StangaNetLib.Storage** are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.0.0] - 2026-10-17

### Added
- Unified blob storage abstraction: Local filesystem, AWS S3 / S3-compatible, Azure Blob.
- Streaming upload/download, metadata, list/copy/move, container lifecycle, pre-signed URLs.
- DI registration (`AddStangaNetLibStorage` / `AddStangaNetLibStorageLocal`) with options validation.
- Optional resilience via `StangaNetLib.Resilience` pipeline `"Storage"` on S3 and Azure providers.
- Dual targeting `net8.0` / `net9.0`, XML docs, symbols (`snupkg`), package icon, and MSBuild consumer metadata stamp.
- Depends on `StangaNetLib.Resilience` 1.0.0 (brings `StangaNetLib.Core`); Microsoft.Extensions.* pinned to 8.x.
- CI via shared StangaNet reusable workflow (push / PR / manual; publish outside PRs).

[Unreleased]: https://github.com/StangaNet/StangaNetLib.Storage/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/StangaNet/StangaNetLib.Storage/releases/tag/v1.0.0
