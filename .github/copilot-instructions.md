# StangaNetLib.Storage — Copilot instructions

- Unified blob storage: Local filesystem, AWS S3 / S3-compatible, Azure Blob.
- Prefer `Result` / `Result<T>` from `StangaNetLib.Core` for expected failures; reserve exceptions for invalid arguments / configuration.
- Keep public APIs documented (`///`) and provider-agnostic at the call site via `IBlobStorageService` / `IPresignedUrlService` / `IContainerManager`.
- DI entry points are `AddStangaNetLibStorage` and `AddStangaNetLibStorageLocal` in `StangaNetLib.Storage.Extensions`.
- Optional: inject `IResilientExecutor` from `StangaNetLib.Resilience` for the `"Storage"` pipeline on S3/Azure providers.
- Match existing naming, file-scoped namespaces, and EditorConfig / Roslyn conventions.
- Tests use xUnit v3 + FluentAssertions; cover `net8.0`, `net9.0`, and `net10.0` when changing shared behavior.
