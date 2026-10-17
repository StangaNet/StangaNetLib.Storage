namespace StangaNetLib.Storage.Models;

/// <summary>A single entry returned by a blob listing operation.</summary>
/// <param name="ContainerName">Container the blob belongs to.</param>
/// <param name="BlobKey">Key of the blob within its container.</param>
/// <param name="SizeInBytes">Size of the blob in bytes.</param>
/// <param name="LastModified">Timestamp of the last write operation.</param>
public sealed record BlobItem(
    string         ContainerName,
    string         BlobKey,
    long           SizeInBytes,
    DateTimeOffset LastModified);
