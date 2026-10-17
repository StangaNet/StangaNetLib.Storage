using StangaNetLib.Core.Common;
using StangaNetLib.Storage.Models;

namespace StangaNetLib.Storage.Abstractions;

/// <summary>
/// Generates time-limited pre-signed URLs that allow unauthenticated upload or download
/// of a single blob. Supported by S3 and Azure Blob backends; the local backend returns
/// simple URLs based on <c>StorageSettings.LocalBaseUrl</c>.
/// Implementations should be registered as <c>Singleton</c>.
/// </summary>
public interface IPresignedUrlService
{
    /// <summary>
    /// Generates a pre-signed URL that allows an HTTP PUT to upload a blob without credentials.
    /// </summary>
    /// <param name="containerName">Target container.</param>
    /// <param name="blobKey">Key the blob will be stored under.</param>
    /// <param name="expiry">How long the URL remains valid.</param>
    /// <param name="contentType">Optional content-type constraint embedded in the signature.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// <see cref="Result{T}"/> containing a <see cref="PresignedUrl"/> on success, or an
    /// <see cref="Error"/> with code <c>Storage.PresignedUrlNotSupported</c> when the backend
    /// requires additional configuration (e.g. <c>LocalBaseUrl</c> not set), or
    /// <see cref="ErrorType.Internal"/> on failure.
    /// </returns>
    Task<Result<PresignedUrl>> GenerateUploadUrlAsync(
        string containerName,
        string blobKey,
        TimeSpan expiry,
        string? contentType = null,
        CancellationToken ct = default);

    /// <summary>
    /// Generates a pre-signed URL that allows an HTTP GET to download a blob without credentials.
    /// </summary>
    /// <param name="containerName">Source container.</param>
    /// <param name="blobKey">Key of the blob to download.</param>
    /// <param name="expiry">How long the URL remains valid.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// <see cref="Result{T}"/> containing a <see cref="PresignedUrl"/> on success, or an
    /// <see cref="Error"/> with code <c>Storage.PresignedUrlNotSupported</c> when the backend
    /// requires additional configuration, or <see cref="ErrorType.Internal"/> on failure.
    /// </returns>
    Task<Result<PresignedUrl>> GenerateDownloadUrlAsync(
        string containerName,
        string blobKey,
        TimeSpan expiry,
        CancellationToken ct = default);
}
