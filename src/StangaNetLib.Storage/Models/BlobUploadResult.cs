namespace StangaNetLib.Storage.Models;

/// <summary>Result returned after a successful blob upload.</summary>
/// <param name="ContainerName">Container (bucket / directory) the blob was stored in.</param>
/// <param name="BlobKey">Key under which the blob is stored.</param>
/// <param name="ContentType">MIME type that was stored with the blob.</param>
/// <param name="ETag">Entity tag (MD5 / version identifier) assigned by the backend.</param>
/// <param name="SizeInBytes">Number of bytes stored.</param>
public sealed record BlobUploadResult(
    string ContainerName,
    string BlobKey,
    string ContentType,
    string ETag,
    long   SizeInBytes);
