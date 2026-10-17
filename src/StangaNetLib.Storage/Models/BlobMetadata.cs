namespace StangaNetLib.Storage.Models;

/// <summary>Lightweight metadata for a blob, returned without downloading its content.</summary>
/// <param name="ContainerName">Container the blob belongs to.</param>
/// <param name="BlobKey">Key of the blob within its container.</param>
/// <param name="ContentType">MIME type reported by the backend.</param>
/// <param name="SizeInBytes">Size of the blob in bytes.</param>
/// <param name="LastModified">Timestamp of the last write operation.</param>
/// <param name="Tags">Backend-specific key/value tags attached to the blob.</param>
public sealed record BlobMetadata(
    string                              ContainerName,
    string                              BlobKey,
    string                              ContentType,
    long                                SizeInBytes,
    DateTimeOffset                      LastModified,
    IReadOnlyDictionary<string, string> Tags);
