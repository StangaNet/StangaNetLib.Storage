using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Microsoft.Extensions.Options;
using StangaNetLib.Core.Common;
using StangaNetLib.Core.Guards;
using StangaNetLib.Resilience.Execution;
using StangaNetLib.Storage.Abstractions;
using StangaNetLib.Storage.Settings;
// Explicit aliases to resolve BlobItem naming conflict with Azure.Storage.Blobs.Models.BlobItem
using StorageBlobDownload = StangaNetLib.Storage.Models.BlobDownloadResult;
using StorageBlobItem     = StangaNetLib.Storage.Models.BlobItem;
using StorageBlobMetadata = StangaNetLib.Storage.Models.BlobMetadata;
using StorageBlobUpload   = StangaNetLib.Storage.Models.BlobUploadResult;
using StoragePresignedUrl = StangaNetLib.Storage.Models.PresignedUrl;

namespace StangaNetLib.Storage.Implementations.AzureBlob;

/// <summary>
/// Azure Blob Storage provider implementing <see cref="IBlobStorageService"/>, <see cref="IPresignedUrlService"/>,
/// and <see cref="IContainerManager"/> against an Azure Blob Storage account.
/// </summary>
/// <remarks>
/// When an <see cref="IResilientExecutor"/> is registered in the DI container it is injected
/// automatically and used to wrap all Azure Blob network calls (retry, circuit-breaker, and timeout
/// are driven by the <c>"Storage"</c> pipeline in <c>ResilienceSettings.Pipelines</c>).
/// When no executor is present the library falls back to direct SDK calls with its own
/// exception-to-Result mapping.
/// <para>
/// Note: when the resilience executor is active, non-retriable HTTP errors such as 404 are
/// subject to the pipeline's retry policy. Configure the <c>"Storage"</c> pipeline to skip
/// retries on <see cref="RequestFailedException"/> with status 404 to preserve
/// <c>Error.NotFound</c> responses.
/// </para>
/// </remarks>
internal sealed class AzureBlobStorageProvider : IBlobStorageService, IPresignedUrlService, IContainerManager
{
    /// <summary>Resilience pipeline name consumed from <c>ResilienceSettings.Pipelines</c> when <see cref="IResilientExecutor"/> is active.</summary>
    internal const string PipelineName = "Storage";

    private readonly BlobServiceClient  _client;
    private readonly IResilientExecutor? _resilience;

    public AzureBlobStorageProvider(IOptions<AzureBlobStorageSettings> options, IResilientExecutor? resilience = null)
    {
        var s = Guard.Against.Null(options, nameof(options)).Value;
        Guard.Against.NullOrWhiteSpace(s.ConnectionString, nameof(s.ConnectionString));
        _client     = new BlobServiceClient(s.ConnectionString);
        _resilience = resilience;
    }

    // ── IBlobStorageService ──────────────────────────────────────────────────

    public async Task<Result<StorageBlobUpload>> UploadAsync(
        string containerName, string blobKey, Stream content, string contentType,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken ct = default)
    {
        Guard.Against.NullOrWhiteSpace(containerName, nameof(containerName));
        Guard.Against.NullOrWhiteSpace(blobKey, nameof(blobKey));
        Guard.Against.Null(content, nameof(content));
        Guard.Against.NullOrWhiteSpace(contentType, nameof(contentType));

        var opts = new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
            Metadata    = metadata?.ToDictionary(kv => kv.Key, kv => kv.Value),
        };

        if (_resilience is not null)
            return await _resilience.ExecuteAsync<StorageBlobUpload>(PipelineName, async innerCt =>
            {
                var blobClient = GetBlobClient(containerName, blobKey);
                var response   = await blobClient.UploadAsync(content, opts, innerCt);
                var etag       = response.Value.ETag.ToString().Trim('"');
                var size       = content.CanSeek ? content.Length : -1L;
                return new StorageBlobUpload(containerName, blobKey, contentType, etag, size);
            }, ct);

        try
        {
            var blobClient = GetBlobClient(containerName, blobKey);
            var response   = await blobClient.UploadAsync(content, opts, ct);
            var etag       = response.Value.ETag.ToString().Trim('"');
            var size       = content.CanSeek ? content.Length : -1L;
            return Result<StorageBlobUpload>.Success(new StorageBlobUpload(containerName, blobKey, contentType, etag, size));
        }
        catch (Exception ex)
        {
            return Result<StorageBlobUpload>.Failure(Error.Internal($"Azure upload failed: {ex.Message}"));
        }
    }

    public async Task<Result<StorageBlobDownload>> DownloadAsync(
        string containerName, string blobKey, CancellationToken ct = default)
    {
        Guard.Against.NullOrWhiteSpace(containerName, nameof(containerName));
        Guard.Against.NullOrWhiteSpace(blobKey, nameof(blobKey));

        if (_resilience is not null)
            return await _resilience.ExecuteAsync<StorageBlobDownload>(PipelineName, async innerCt =>
            {
                var blobClient = GetBlobClient(containerName, blobKey);
                var props      = (await blobClient.GetPropertiesAsync(cancellationToken: innerCt)).Value;
                var stream     = await blobClient.OpenReadAsync(cancellationToken: innerCt);
                var meta       = props.Metadata.ToDictionary(kv => kv.Key, kv => kv.Value);
                return new StorageBlobDownload(stream, props.ContentType ?? "application/octet-stream",
                    props.ContentLength, (IReadOnlyDictionary<string, string>)meta);
            }, ct);

        try
        {
            var blobClient = GetBlobClient(containerName, blobKey);
            var props      = (await blobClient.GetPropertiesAsync(cancellationToken: ct)).Value;
            var stream     = await blobClient.OpenReadAsync(cancellationToken: ct);
            var meta       = props.Metadata.ToDictionary(kv => kv.Key, kv => kv.Value);

            return Result<StorageBlobDownload>.Success(new StorageBlobDownload(
                stream,
                props.ContentType ?? "application/octet-stream",
                props.ContentLength,
                (IReadOnlyDictionary<string, string>)meta));
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return Result<StorageBlobDownload>.Failure(Error.NotFound("Blob", $"{containerName}/{blobKey}"));
        }
        catch (Exception ex)
        {
            return Result<StorageBlobDownload>.Failure(Error.Internal($"Azure download failed: {ex.Message}"));
        }
    }

    public async Task<Result> DeleteAsync(string containerName, string blobKey, CancellationToken ct = default)
    {
        Guard.Against.NullOrWhiteSpace(containerName, nameof(containerName));
        Guard.Against.NullOrWhiteSpace(blobKey, nameof(blobKey));

        if (_resilience is not null)
            return await _resilience.ExecuteAsync(PipelineName,
                async innerCt => { await GetBlobClient(containerName, blobKey).DeleteIfExistsAsync(cancellationToken: innerCt); }, ct);

        try
        {
            await GetBlobClient(containerName, blobKey).DeleteIfExistsAsync(cancellationToken: ct);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(Error.Internal($"Azure delete failed: {ex.Message}"));
        }
    }

    public async Task<bool> ExistsAsync(string containerName, string blobKey, CancellationToken ct = default)
    {
        Guard.Against.NullOrWhiteSpace(containerName, nameof(containerName));
        Guard.Against.NullOrWhiteSpace(blobKey, nameof(blobKey));

        try
        {
            var response = await GetBlobClient(containerName, blobKey).ExistsAsync(ct);
            return response.Value;
        }
        catch
        {
            // Treat any transient or auth error as "not found" — ExistsAsync must not throw.
            return false;
        }
    }

    public async Task<Result<StorageBlobMetadata>> GetMetadataAsync(string containerName, string blobKey, CancellationToken ct = default)
    {
        Guard.Against.NullOrWhiteSpace(containerName, nameof(containerName));
        Guard.Against.NullOrWhiteSpace(blobKey, nameof(blobKey));

        if (_resilience is not null)
            return await _resilience.ExecuteAsync<StorageBlobMetadata>(PipelineName, async innerCt =>
            {
                var props = (await GetBlobClient(containerName, blobKey).GetPropertiesAsync(cancellationToken: innerCt)).Value;
                var tags  = props.Metadata.ToDictionary(kv => kv.Key, kv => kv.Value);
                return new StorageBlobMetadata(containerName, blobKey,
                    props.ContentType ?? "application/octet-stream", props.ContentLength,
                    props.LastModified, (IReadOnlyDictionary<string, string>)tags);
            }, ct);

        try
        {
            var props = (await GetBlobClient(containerName, blobKey).GetPropertiesAsync(cancellationToken: ct)).Value;
            var tags  = props.Metadata.ToDictionary(kv => kv.Key, kv => kv.Value);

            return Result<StorageBlobMetadata>.Success(new StorageBlobMetadata(
                containerName, blobKey,
                props.ContentType ?? "application/octet-stream", props.ContentLength,
                props.LastModified, (IReadOnlyDictionary<string, string>)tags));
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return Result<StorageBlobMetadata>.Failure(Error.NotFound("Blob", $"{containerName}/{blobKey}"));
        }
        catch (Exception ex)
        {
            return Result<StorageBlobMetadata>.Failure(Error.Internal($"Azure get metadata failed: {ex.Message}"));
        }
    }

    public async Task<Result<IReadOnlyList<StorageBlobItem>>> ListBlobsAsync(
        string containerName, string? prefix = null, CancellationToken ct = default)
    {
        Guard.Against.NullOrWhiteSpace(containerName, nameof(containerName));

        if (_resilience is not null)
            return await _resilience.ExecuteAsync<IReadOnlyList<StorageBlobItem>>(PipelineName, async innerCt =>
            {
                var container = _client.GetBlobContainerClient(containerName);
                var items     = new List<StorageBlobItem>();
                await foreach (var blob in container.GetBlobsAsync(BlobTraits.None, BlobStates.None, prefix, innerCt))
                    items.Add(new StorageBlobItem(containerName, blob.Name,
                        blob.Properties.ContentLength ?? 0L,
                        blob.Properties.LastModified ?? DateTimeOffset.UtcNow));
                return items.AsReadOnly();
            }, ct);

        try
        {
            var container = _client.GetBlobContainerClient(containerName);
            var items     = new List<StorageBlobItem>();

            await foreach (var blob in container.GetBlobsAsync(BlobTraits.None, BlobStates.None, prefix, ct))
            {
                items.Add(new StorageBlobItem(
                    containerName,
                    blob.Name,
                    blob.Properties.ContentLength ?? 0L,
                    blob.Properties.LastModified ?? DateTimeOffset.UtcNow));
            }

            return Result<IReadOnlyList<StorageBlobItem>>.Success(items.AsReadOnly());
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<StorageBlobItem>>.Failure(Error.Internal($"Azure list failed: {ex.Message}"));
        }
    }

    public async Task<Result> CopyAsync(
        string sourceContainer, string sourceBlobKey, string destContainer, string destBlobKey, CancellationToken ct = default)
    {
        Guard.Against.NullOrWhiteSpace(sourceContainer, nameof(sourceContainer));
        Guard.Against.NullOrWhiteSpace(sourceBlobKey, nameof(sourceBlobKey));
        Guard.Against.NullOrWhiteSpace(destContainer, nameof(destContainer));
        Guard.Against.NullOrWhiteSpace(destBlobKey, nameof(destBlobKey));

        // CopyAsync delegates to DownloadAsync/UploadAsync which are already resilience-wrapped;
        // avoid nesting an outer resilience layer here.
        try
        {
            var sourceBlobClient = GetBlobClient(sourceContainer, sourceBlobKey);
            var destBlobClient   = GetBlobClient(destContainer, destBlobKey);

            // Attempt server-side copy using a short-lived SAS URL (requires SharedKey credentials).
            // Falls back to download+reupload when SAS generation is not available.
            try
            {
                var sasUri = sourceBlobClient.GenerateSasUri(new BlobSasBuilder(BlobSasPermissions.Read, DateTimeOffset.UtcNow.AddMinutes(5))
                {
                    BlobContainerName = sourceContainer,
                    BlobName          = sourceBlobKey,
                    Resource          = "b",
                });
                var copyOp = await destBlobClient.StartCopyFromUriAsync(sasUri, cancellationToken: ct);
                await copyOp.WaitForCompletionAsync(cancellationToken: ct);
                return Result.Success();
            }
            catch (InvalidOperationException)
            {
                // SAS generation not available (e.g. SAS-based connection string); fall through.
            }

            var download = await DownloadAsync(sourceContainer, sourceBlobKey, ct);
            if (download.IsFailure) return Result.Failure(download.Error);

            await using var stream = download.Value!.Content;
            var upload = await UploadAsync(destContainer, destBlobKey, stream, download.Value!.ContentType, ct: ct);
            return upload.IsFailure ? Result.Failure(upload.Error) : Result.Success();
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return Result.Failure(Error.NotFound("Blob", $"{sourceContainer}/{sourceBlobKey}"));
        }
        catch (Exception ex)
        {
            return Result.Failure(Error.Internal($"Azure copy failed: {ex.Message}"));
        }
    }

    public async Task<Result> MoveAsync(
        string sourceContainer, string sourceBlobKey, string destContainer, string destBlobKey, CancellationToken ct = default)
    {
        var copy = await CopyAsync(sourceContainer, sourceBlobKey, destContainer, destBlobKey, ct);
        if (copy.IsFailure) return copy;
        return await DeleteAsync(sourceContainer, sourceBlobKey, ct);
    }

    // ── IPresignedUrlService ─────────────────────────────────────────────────

    public Task<Result<StoragePresignedUrl>> GenerateUploadUrlAsync(
        string containerName, string blobKey, TimeSpan expiry, string? contentType = null, CancellationToken ct = default)
        => GenerateSasUri(containerName, blobKey, expiry, BlobSasPermissions.Write | BlobSasPermissions.Create);

    public Task<Result<StoragePresignedUrl>> GenerateDownloadUrlAsync(
        string containerName, string blobKey, TimeSpan expiry, CancellationToken ct = default)
        => GenerateSasUri(containerName, blobKey, expiry, BlobSasPermissions.Read);

    private Task<Result<StoragePresignedUrl>> GenerateSasUri(
        string containerName, string blobKey, TimeSpan expiry, BlobSasPermissions permissions)
    {
        Guard.Against.NullOrWhiteSpace(containerName, nameof(containerName));
        Guard.Against.NullOrWhiteSpace(blobKey, nameof(blobKey));

        try
        {
            var expiresOn  = DateTimeOffset.UtcNow.Add(expiry);
            var sasBuilder = new BlobSasBuilder(permissions, expiresOn)
            {
                BlobContainerName = containerName,
                BlobName          = blobKey,
                Resource          = "b",
            };

            var sasUri = GetBlobClient(containerName, blobKey).GenerateSasUri(sasBuilder);
            return Task.FromResult(Result<StoragePresignedUrl>.Success(new StoragePresignedUrl(sasUri.ToString(), expiresOn)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(Result<StoragePresignedUrl>.Failure(new Error(
                "Storage.PresignedUrlNotSupported",
                $"Azure SAS generation failed — ensure the client was initialized with account-key credentials: {ex.Message}",
                ErrorType.Validation)));
        }
    }

    // ── IContainerManager ────────────────────────────────────────────────────

    public async Task<Result> CreateContainerAsync(string containerName, CancellationToken ct = default)
    {
        Guard.Against.NullOrWhiteSpace(containerName, nameof(containerName));

        if (_resilience is not null)
            return await _resilience.ExecuteAsync(PipelineName,
                async innerCt => { await _client.GetBlobContainerClient(containerName).CreateIfNotExistsAsync(cancellationToken: innerCt); }, ct);

        try
        {
            await _client.GetBlobContainerClient(containerName).CreateIfNotExistsAsync(cancellationToken: ct);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(Error.Internal($"Azure create container failed: {ex.Message}"));
        }
    }

    public async Task<Result> DeleteContainerAsync(string containerName, CancellationToken ct = default)
    {
        Guard.Against.NullOrWhiteSpace(containerName, nameof(containerName));

        if (_resilience is not null)
            return await _resilience.ExecuteAsync(PipelineName,
                async innerCt => { await _client.GetBlobContainerClient(containerName).DeleteIfExistsAsync(cancellationToken: innerCt); }, ct);

        try
        {
            await _client.GetBlobContainerClient(containerName).DeleteIfExistsAsync(cancellationToken: ct);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(Error.Internal($"Azure delete container failed: {ex.Message}"));
        }
    }

    public async Task<bool> ContainerExistsAsync(string containerName, CancellationToken ct = default)
    {
        Guard.Against.NullOrWhiteSpace(containerName, nameof(containerName));

        var response = await _client.GetBlobContainerClient(containerName).ExistsAsync(ct);
        return response.Value;
    }

    public async Task<Result<IReadOnlyList<string>>> ListContainersAsync(CancellationToken ct = default)
    {
        if (_resilience is not null)
            return await _resilience.ExecuteAsync<IReadOnlyList<string>>(PipelineName, async innerCt =>
            {
                var names = new List<string>();
                await foreach (var item in _client.GetBlobContainersAsync(cancellationToken: innerCt))
                    names.Add(item.Name);
                return names.AsReadOnly();
            }, ct);

        try
        {
            var names = new List<string>();
            await foreach (var item in _client.GetBlobContainersAsync(cancellationToken: ct))
                names.Add(item.Name);

            return Result<IReadOnlyList<string>>.Success(names.AsReadOnly());
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<string>>.Failure(Error.Internal($"Azure list containers failed: {ex.Message}"));
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private BlobClient GetBlobClient(string containerName, string blobKey)
        => _client.GetBlobContainerClient(containerName).GetBlobClient(blobKey);
}
