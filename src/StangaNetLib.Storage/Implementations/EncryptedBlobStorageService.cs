using StangaNetLib.Core.Common;
using StangaNetLib.Core.Guards;
using StangaNetLib.Cryptography.Abstractions;
using StangaNetLib.Storage.Abstractions;
using StangaNetLib.Storage.Models;
using StangaNetLib.Storage.Settings;

namespace StangaNetLib.Storage.Implementations;

/// <summary>
/// Decorator that encrypts blob payloads at rest via <see cref="IAtRestEncryptionService"/>
/// before delegating to an inner <see cref="IBlobStorageService"/>.
/// </summary>
internal sealed class EncryptedBlobStorageService : IBlobStorageService
{
    internal const string EncryptedMetadataKey = "x-sn-encrypted";
    internal const string OriginalContentTypeKey = "x-sn-original-content-type";
    private const string EncryptedContentType = "application/octet-stream";

    private readonly IBlobStorageService _inner;
    private readonly IAtRestEncryptionService _encryption;
    private readonly string _passphrase;

    public EncryptedBlobStorageService(
        IBlobStorageService inner,
        IAtRestEncryptionService encryption,
        StorageEncryptionSettings settings)
    {
        _inner = Guard.Against.Null(inner, nameof(inner));
        _encryption = Guard.Against.Null(encryption, nameof(encryption));
        Guard.Against.Null(settings, nameof(settings));
        _passphrase = Guard.Against.NullOrEmpty(settings.Passphrase, nameof(settings.Passphrase));
    }

    public async Task<Result<BlobUploadResult>> UploadAsync(
        string containerName,
        string blobKey,
        Stream content,
        string contentType,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken ct = default)
    {
        await using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct).ConfigureAwait(false);
        var plaintext = buffer.ToArray();

        var encrypted = _encryption.EncryptBlob(plaintext, _passphrase);
        if (encrypted.IsFailure)
            return Result<BlobUploadResult>.Failure(encrypted.Error);

        var meta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (metadata is not null)
        {
            foreach (var kv in metadata)
                meta[kv.Key] = kv.Value;
        }

        meta[EncryptedMetadataKey] = "1";
        meta[OriginalContentTypeKey] = contentType;

        await using var cipherStream = new MemoryStream(encrypted.Value!);
        return await _inner.UploadAsync(
            containerName, blobKey, cipherStream, EncryptedContentType, meta, ct).ConfigureAwait(false);
    }

    public async Task<Result<BlobDownloadResult>> DownloadAsync(
        string containerName,
        string blobKey,
        CancellationToken ct = default)
    {
        var download = await _inner.DownloadAsync(containerName, blobKey, ct).ConfigureAwait(false);
        if (download.IsFailure)
            return download;

        var value = download.Value!;
        await using (value.Content)
        {
            await using var buffer = new MemoryStream();
            await value.Content.CopyToAsync(buffer, ct).ConfigureAwait(false);
            var envelope = buffer.ToArray();

            var decrypted = _encryption.DecryptBlob(envelope, _passphrase);
            if (decrypted.IsFailure)
                return Result<BlobDownloadResult>.Failure(decrypted.Error);

            var originalType = value.Metadata.TryGetValue(OriginalContentTypeKey, out var ctMeta)
                ? ctMeta
                : "application/octet-stream";

            var plainStream = new MemoryStream(decrypted.Value!, writable: false);
            return Result<BlobDownloadResult>.Success(new BlobDownloadResult(
                plainStream,
                originalType,
                plainStream.Length,
                value.Metadata));
        }
    }

    public Task<Result> DeleteAsync(string containerName, string blobKey, CancellationToken ct = default)
        => _inner.DeleteAsync(containerName, blobKey, ct);

    public Task<bool> ExistsAsync(string containerName, string blobKey, CancellationToken ct = default)
        => _inner.ExistsAsync(containerName, blobKey, ct);

    public Task<Result<BlobMetadata>> GetMetadataAsync(
        string containerName, string blobKey, CancellationToken ct = default)
        => _inner.GetMetadataAsync(containerName, blobKey, ct);

    public Task<Result<IReadOnlyList<BlobItem>>> ListBlobsAsync(
        string containerName, string? prefix = null, CancellationToken ct = default)
        => _inner.ListBlobsAsync(containerName, prefix, ct);

    public Task<Result> CopyAsync(
        string sourceContainer,
        string sourceBlobKey,
        string destContainer,
        string destBlobKey,
        CancellationToken ct = default)
        => _inner.CopyAsync(sourceContainer, sourceBlobKey, destContainer, destBlobKey, ct);

    public Task<Result> MoveAsync(
        string sourceContainer,
        string sourceBlobKey,
        string destContainer,
        string destBlobKey,
        CancellationToken ct = default)
        => _inner.MoveAsync(sourceContainer, sourceBlobKey, destContainer, destBlobKey, ct);
}
