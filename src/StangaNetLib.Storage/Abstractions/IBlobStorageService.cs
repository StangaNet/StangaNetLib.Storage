using StangaNetLib.Core.Common;
using StangaNetLib.Storage.Models;

namespace StangaNetLib.Storage.Abstractions;

/// <summary>
/// Provides blob storage operations (upload, download, delete, list, copy, move) against a
/// configurable backend (Local filesystem, AWS S3, or Azure Blob Storage).
/// Implementations should be registered as <c>Singleton</c>.
/// </summary>
public interface IBlobStorageService
{
    /// <summary>
    /// Uploads a blob to the specified container.
    /// If a blob with the same key already exists it is overwritten.
    /// </summary>
    /// <param name="containerName">Destination container (bucket / directory).</param>
    /// <param name="blobKey">Unique key within the container (e.g. "images/photo.jpg").</param>
    /// <param name="content">Stream of bytes to store. The caller retains ownership and must dispose it.</param>
    /// <param name="contentType">MIME type of the content (e.g. "image/jpeg").</param>
    /// <param name="metadata">Optional key/value pairs stored alongside the blob (backend-specific limits apply).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// <see cref="Result{T}"/> containing a <see cref="BlobUploadResult"/> on success, or an
    /// <see cref="Error"/> with type <see cref="ErrorType.Internal"/> on failure.
    /// </returns>
    Task<Result<BlobUploadResult>> UploadAsync(
        string containerName,
        string blobKey,
        Stream content,
        string contentType,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken ct = default);

    /// <summary>
    /// Downloads a blob from the specified container.
    /// The caller is responsible for disposing <see cref="BlobDownloadResult.Content"/>.
    /// </summary>
    /// <param name="containerName">Source container.</param>
    /// <param name="blobKey">Key of the blob to download.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// <see cref="Result{T}"/> containing a <see cref="BlobDownloadResult"/> on success,
    /// <see cref="ErrorType.NotFound"/> if the blob does not exist, or
    /// <see cref="ErrorType.Internal"/> on failure.
    /// </returns>
    Task<Result<BlobDownloadResult>> DownloadAsync(
        string containerName,
        string blobKey,
        CancellationToken ct = default);

    /// <summary>
    /// Deletes a blob. Safe to call when the blob does not exist — returns success.
    /// </summary>
    /// <param name="containerName">Container holding the blob.</param>
    /// <param name="blobKey">Key of the blob to delete.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><see cref="Result"/> indicating success or an <see cref="ErrorType.Internal"/> failure.</returns>
    Task<Result> DeleteAsync(
        string containerName,
        string blobKey,
        CancellationToken ct = default);

    /// <summary>
    /// Returns <see langword="true"/> if a blob with the given key exists in the container.
    /// </summary>
    /// <param name="containerName">Container to check.</param>
    /// <param name="blobKey">Key to check for existence.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><see langword="true"/> if the blob exists; otherwise <see langword="false"/>.</returns>
    Task<bool> ExistsAsync(
        string containerName,
        string blobKey,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieves metadata for a blob without downloading its content.
    /// </summary>
    /// <param name="containerName">Container holding the blob.</param>
    /// <param name="blobKey">Key of the blob.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// <see cref="Result{T}"/> containing <see cref="BlobMetadata"/> on success,
    /// <see cref="ErrorType.NotFound"/> if the blob does not exist, or
    /// <see cref="ErrorType.Internal"/> on failure.
    /// </returns>
    Task<Result<BlobMetadata>> GetMetadataAsync(
        string containerName,
        string blobKey,
        CancellationToken ct = default);

    /// <summary>
    /// Lists all blobs in a container, optionally filtered by a key prefix.
    /// </summary>
    /// <param name="containerName">Container to list.</param>
    /// <param name="prefix">Optional key prefix filter (e.g. "images/").</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// <see cref="Result{T}"/> containing all matching <see cref="BlobItem"/> entries on success,
    /// or an <see cref="ErrorType.Internal"/> failure.
    /// </returns>
    Task<Result<IReadOnlyList<BlobItem>>> ListBlobsAsync(
        string containerName,
        string? prefix = null,
        CancellationToken ct = default);

    /// <summary>
    /// Copies a blob to a new location. The source blob is preserved.
    /// </summary>
    /// <param name="sourceContainer">Container of the source blob.</param>
    /// <param name="sourceBlobKey">Key of the source blob.</param>
    /// <param name="destContainer">Destination container.</param>
    /// <param name="destBlobKey">Destination key.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// <see cref="Result"/> indicating success,
    /// <see cref="ErrorType.NotFound"/> if the source blob does not exist, or
    /// <see cref="ErrorType.Internal"/> on failure.
    /// </returns>
    Task<Result> CopyAsync(
        string sourceContainer,
        string sourceBlobKey,
        string destContainer,
        string destBlobKey,
        CancellationToken ct = default);

    /// <summary>
    /// Moves a blob to a new location. The source blob is deleted after a successful copy.
    /// </summary>
    /// <param name="sourceContainer">Container of the source blob.</param>
    /// <param name="sourceBlobKey">Key of the source blob.</param>
    /// <param name="destContainer">Destination container.</param>
    /// <param name="destBlobKey">Destination key.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// <see cref="Result"/> indicating success,
    /// <see cref="ErrorType.NotFound"/> if the source blob does not exist, or
    /// <see cref="ErrorType.Internal"/> on failure.
    /// </returns>
    Task<Result> MoveAsync(
        string sourceContainer,
        string sourceBlobKey,
        string destContainer,
        string destBlobKey,
        CancellationToken ct = default);
}
