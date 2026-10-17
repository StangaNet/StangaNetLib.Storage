namespace StangaNetLib.Storage.Models;

/// <summary>
/// Result returned after a successful blob download.
/// The caller is responsible for disposing <see cref="Content"/>.
/// </summary>
/// <param name="Content">Stream of the blob's bytes. Must be disposed by the caller.</param>
/// <param name="ContentType">MIME type reported by the backend.</param>
/// <param name="SizeInBytes">Length of the content stream in bytes.</param>
/// <param name="Metadata">Backend-specific key/value metadata attached to the blob.</param>
public sealed record BlobDownloadResult(
    Stream                                Content,
    string                                ContentType,
    long                                  SizeInBytes,
    IReadOnlyDictionary<string, string>   Metadata);
