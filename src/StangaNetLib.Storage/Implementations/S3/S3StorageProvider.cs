using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using StangaNetLib.Core.Common;
using StangaNetLib.Core.Guards;
using StangaNetLib.Resilience.Execution;
using StangaNetLib.Storage.Abstractions;
using StangaNetLib.Storage.Models;
using StangaNetLib.Storage.Settings;

namespace StangaNetLib.Storage.Implementations.S3;

/// <summary>
/// AWS S3 storage provider implementing <see cref="IBlobStorageService"/>, <see cref="IPresignedUrlService"/>,
/// and <see cref="IContainerManager"/> against an Amazon S3 bucket (or any S3-compatible backend such as MinIO or LocalStack).
/// </summary>
/// <remarks>
/// When an <see cref="IResilientExecutor"/> is registered in the DI container it is injected
/// automatically and used to wrap all S3 network calls (retry, circuit-breaker, and timeout are
/// driven by the <c>"Storage"</c> pipeline in <c>ResilienceSettings.Pipelines</c>).
/// When no executor is present the library falls back to direct SDK calls with its own
/// exception-to-Result mapping.
/// <para>
/// Note: when the resilience executor is active, non-retriable HTTP errors such as 404 are
/// subject to the pipeline's retry policy. Configure the <c>"Storage"</c> pipeline to skip
/// retries on <see cref="AmazonS3Exception"/> with <see cref="System.Net.HttpStatusCode.NotFound"/>
/// to preserve <c>Error.NotFound</c> responses.
/// </para>
/// </remarks>
internal sealed class S3StorageProvider : IBlobStorageService, IPresignedUrlService, IContainerManager, IDisposable
{
    /// <summary>Resilience pipeline name consumed from <c>ResilienceSettings.Pipelines</c> when <see cref="IResilientExecutor"/> is active.</summary>
    internal const string PipelineName = "Storage";

    private static DateTimeOffset ToOffset(DateTime? value)
        => value is { } dt
            ? new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc))
            : default;



    private readonly AmazonS3Client _client;
    private readonly IResilientExecutor? _resilience;

    public S3StorageProvider(IOptions<S3StorageSettings> options, IResilientExecutor? resilience = null)
    {
        var s = Guard.Against.Null(options, nameof(options)).Value;

        var config = new AmazonS3Config
        {
            RegionEndpoint = RegionEndpoint.GetBySystemName(s.Region),
        };

        if (!string.IsNullOrWhiteSpace(s.ServiceUrl))
        {
            config.ServiceURL = s.ServiceUrl;
            config.ForcePathStyle = s.ForcePathStyle;
        }

        _client = new AmazonS3Client(s.AccessKey, s.SecretKey, config);
        _resilience = resilience;
    }

    // ── IBlobStorageService ──────────────────────────────────────────────────

    public async Task<Result<BlobUploadResult>> UploadAsync(
        string containerName, string blobKey, Stream content, string contentType,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken ct = default)
    {
        Guard.Against.NullOrWhiteSpace(containerName, nameof(containerName));
        Guard.Against.NullOrWhiteSpace(blobKey, nameof(blobKey));
        Guard.Against.Null(content, nameof(content));
        Guard.Against.NullOrWhiteSpace(contentType, nameof(contentType));

        var request = new PutObjectRequest
        {
            BucketName = containerName,
            Key = blobKey,
            InputStream = content,
            ContentType = contentType,
        };

        if (metadata != null)
            foreach (var kv in metadata)
                request.Metadata[kv.Key] = kv.Value;

        if (_resilience is not null)
            return await _resilience.ExecuteAsync<BlobUploadResult>(PipelineName, async innerCt =>
            {
                var response = await _client.PutObjectAsync(request, innerCt);
                var etag = response.ETag?.Trim('"') ?? string.Empty;
                var size = content.CanSeek ? content.Length : -1L;
                return new BlobUploadResult(containerName, blobKey, contentType, etag, size);
            }, ct);

        try
        {
            var response = await _client.PutObjectAsync(request, ct);
            var etag = response.ETag?.Trim('"') ?? string.Empty;
            var size = content.CanSeek ? content.Length : -1L;
            return Result<BlobUploadResult>.Success(new BlobUploadResult(containerName, blobKey, contentType, etag, size));
        }
        catch (Exception ex)
        {
            return Result<BlobUploadResult>.Failure(Error.Internal($"S3 upload failed: {ex.Message}"));
        }
    }

    public async Task<Result<BlobDownloadResult>> DownloadAsync(
        string containerName, string blobKey, CancellationToken ct = default)
    {
        Guard.Against.NullOrWhiteSpace(containerName, nameof(containerName));
        Guard.Against.NullOrWhiteSpace(blobKey, nameof(blobKey));

        if (_resilience is not null)
            return await _resilience.ExecuteAsync<BlobDownloadResult>(PipelineName, async innerCt =>
            {
                var response = await _client.GetObjectAsync(containerName, blobKey, innerCt);
                var contentType = response.Headers.ContentType ?? "application/octet-stream";
                var size = response.ContentLength;
                var meta = response.Metadata.Keys.ToDictionary(k => k, k => response.Metadata[k]);
                return new BlobDownloadResult(response.ResponseStream, contentType, size,
                    (IReadOnlyDictionary<string, string>)meta);
            }, ct);

        try
        {
            var response = await _client.GetObjectAsync(containerName, blobKey, ct);
            var contentType = response.Headers.ContentType ?? "application/octet-stream";
            var size = response.ContentLength;
            var metadata = response.Metadata.Keys.ToDictionary(k => k, k => response.Metadata[k]);

            return Result<BlobDownloadResult>.Success(new BlobDownloadResult(
                response.ResponseStream, contentType, size,
                (IReadOnlyDictionary<string, string>)metadata));
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return Result<BlobDownloadResult>.Failure(Error.NotFound("Blob", $"{containerName}/{blobKey}"));
        }
        catch (Exception ex)
        {
            return Result<BlobDownloadResult>.Failure(Error.Internal($"S3 download failed: {ex.Message}"));
        }
    }

    public async Task<Result> DeleteAsync(string containerName, string blobKey, CancellationToken ct = default)
    {
        Guard.Against.NullOrWhiteSpace(containerName, nameof(containerName));
        Guard.Against.NullOrWhiteSpace(blobKey, nameof(blobKey));

        if (_resilience is not null)
            return await _resilience.ExecuteAsync(PipelineName,
                async innerCt => { await _client.DeleteObjectAsync(containerName, blobKey, innerCt); }, ct);

        try
        {
            await _client.DeleteObjectAsync(containerName, blobKey, ct);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(Error.Internal($"S3 delete failed: {ex.Message}"));
        }
    }

    public async Task<bool> ExistsAsync(string containerName, string blobKey, CancellationToken ct = default)
    {
        Guard.Against.NullOrWhiteSpace(containerName, nameof(containerName));
        Guard.Against.NullOrWhiteSpace(blobKey, nameof(blobKey));

        try
        {
            await _client.GetObjectMetadataAsync(containerName, blobKey, ct);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async Task<Result<BlobMetadata>> GetMetadataAsync(string containerName, string blobKey, CancellationToken ct = default)
    {
        Guard.Against.NullOrWhiteSpace(containerName, nameof(containerName));
        Guard.Against.NullOrWhiteSpace(blobKey, nameof(blobKey));

        if (_resilience is not null)
            return await _resilience.ExecuteAsync<BlobMetadata>(PipelineName, async innerCt =>
            {
                var response = await _client.GetObjectMetadataAsync(containerName, blobKey, innerCt);
                var contentType = response.Headers.ContentType ?? "application/octet-stream";
                var tags = response.Metadata.Keys.ToDictionary(k => k, k => response.Metadata[k]);
                return new BlobMetadata(containerName, blobKey, contentType, response.ContentLength,
                    ToOffset(response.LastModified), (IReadOnlyDictionary<string, string>)tags);
            }, ct);

        try
        {
            var response = await _client.GetObjectMetadataAsync(containerName, blobKey, ct);
            var contentType = response.Headers.ContentType ?? "application/octet-stream";
            var tags = response.Metadata.Keys.ToDictionary(k => k, k => response.Metadata[k]);

            return Result<BlobMetadata>.Success(new BlobMetadata(
                containerName, blobKey, contentType, response.ContentLength,
                ToOffset(response.LastModified), (IReadOnlyDictionary<string, string>)tags));
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return Result<BlobMetadata>.Failure(Error.NotFound("Blob", $"{containerName}/{blobKey}"));
        }
        catch (Exception ex)
        {
            return Result<BlobMetadata>.Failure(Error.Internal($"S3 get metadata failed: {ex.Message}"));
        }
    }

    public async Task<Result<IReadOnlyList<BlobItem>>> ListBlobsAsync(
        string containerName, string? prefix = null, CancellationToken ct = default)
    {
        Guard.Against.NullOrWhiteSpace(containerName, nameof(containerName));

        if (_resilience is not null)
            return await _resilience.ExecuteAsync<IReadOnlyList<BlobItem>>(PipelineName, async innerCt =>
            {
                var items = new List<BlobItem>();
                var request = new ListObjectsV2Request { BucketName = containerName, Prefix = prefix };
                ListObjectsV2Response response;
                do
                {
                    response = await _client.ListObjectsV2Async(request, innerCt);
                    foreach (var obj in response.S3Objects)
                        items.Add(new BlobItem(containerName, obj.Key, obj.Size ?? 0L, ToOffset(obj.LastModified)));
                    request.ContinuationToken = response.NextContinuationToken;
                }
                while (response.IsTruncated == true);
                return items.AsReadOnly();
            }, ct);

        try
        {
            var items = new List<BlobItem>();
            var request = new ListObjectsV2Request { BucketName = containerName, Prefix = prefix };

            ListObjectsV2Response response;
            do
            {
                response = await _client.ListObjectsV2Async(request, ct);
                foreach (var obj in response.S3Objects)
                    items.Add(new BlobItem(containerName, obj.Key, obj.Size ?? 0L, ToOffset(obj.LastModified)));
                request.ContinuationToken = response.NextContinuationToken;
            }
            while (response.IsTruncated == true);

            return Result<IReadOnlyList<BlobItem>>.Success(items.AsReadOnly());
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<BlobItem>>.Failure(Error.Internal($"S3 list failed: {ex.Message}"));
        }
    }

    public async Task<Result> CopyAsync(
        string sourceContainer, string sourceBlobKey, string destContainer, string destBlobKey, CancellationToken ct = default)
    {
        Guard.Against.NullOrWhiteSpace(sourceContainer, nameof(sourceContainer));
        Guard.Against.NullOrWhiteSpace(sourceBlobKey, nameof(sourceBlobKey));
        Guard.Against.NullOrWhiteSpace(destContainer, nameof(destContainer));
        Guard.Against.NullOrWhiteSpace(destBlobKey, nameof(destBlobKey));

        var copyRequest = new CopyObjectRequest
        {
            SourceBucket = sourceContainer,
            SourceKey = sourceBlobKey,
            DestinationBucket = destContainer,
            DestinationKey = destBlobKey,
        };

        if (_resilience is not null)
            return await _resilience.ExecuteAsync(PipelineName,
                async innerCt => { await _client.CopyObjectAsync(copyRequest, innerCt); }, ct);

        try
        {
            await _client.CopyObjectAsync(copyRequest, ct);
            return Result.Success();
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return Result.Failure(Error.NotFound("Blob", $"{sourceContainer}/{sourceBlobKey}"));
        }
        catch (Exception ex)
        {
            return Result.Failure(Error.Internal($"S3 copy failed: {ex.Message}"));
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

    public Task<Result<PresignedUrl>> GenerateUploadUrlAsync(
        string containerName, string blobKey, TimeSpan expiry, string? contentType = null, CancellationToken ct = default)
    {
        Guard.Against.NullOrWhiteSpace(containerName, nameof(containerName));
        Guard.Against.NullOrWhiteSpace(blobKey, nameof(blobKey));

        try
        {
            var expiresAt = DateTime.UtcNow.Add(expiry);
            var request = new GetPreSignedUrlRequest
            {
                BucketName = containerName,
                Key = blobKey,
                Expires = expiresAt,
                Verb = HttpVerb.PUT,
                ContentType = contentType,
            };
            var url = _client.GetPreSignedURL(request);
            return Task.FromResult(Result<PresignedUrl>.Success(new PresignedUrl(url, new DateTimeOffset(expiresAt, TimeSpan.Zero))));
        }
        catch (Exception ex)
        {
            return Task.FromResult(Result<PresignedUrl>.Failure(Error.Internal($"S3 presigned upload URL failed: {ex.Message}")));
        }
    }

    public Task<Result<PresignedUrl>> GenerateDownloadUrlAsync(
        string containerName, string blobKey, TimeSpan expiry, CancellationToken ct = default)
    {
        Guard.Against.NullOrWhiteSpace(containerName, nameof(containerName));
        Guard.Against.NullOrWhiteSpace(blobKey, nameof(blobKey));

        try
        {
            var expiresAt = DateTime.UtcNow.Add(expiry);
            var request = new GetPreSignedUrlRequest
            {
                BucketName = containerName,
                Key = blobKey,
                Expires = expiresAt,
                Verb = HttpVerb.GET,
            };
            var url = _client.GetPreSignedURL(request);
            return Task.FromResult(Result<PresignedUrl>.Success(new PresignedUrl(url, new DateTimeOffset(expiresAt, TimeSpan.Zero))));
        }
        catch (Exception ex)
        {
            return Task.FromResult(Result<PresignedUrl>.Failure(Error.Internal($"S3 presigned download URL failed: {ex.Message}")));
        }
    }

    // ── IContainerManager ────────────────────────────────────────────────────

    public async Task<Result> CreateContainerAsync(string containerName, CancellationToken ct = default)
    {
        Guard.Against.NullOrWhiteSpace(containerName, nameof(containerName));

        if (_resilience is not null)
            return await _resilience.ExecuteAsync(PipelineName,
                async innerCt => { await _client.PutBucketAsync(containerName, innerCt); }, ct);

        try
        {
            await _client.PutBucketAsync(containerName, ct);
            return Result.Success();
        }
        catch (AmazonS3Exception ex) when (
            ex.ErrorCode == "BucketAlreadyOwnedByYou" ||
            ex.ErrorCode == "BucketAlreadyExists")
        {
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(Error.Internal($"S3 create bucket failed: {ex.Message}"));
        }
    }

    public async Task<Result> DeleteContainerAsync(string containerName, CancellationToken ct = default)
    {
        Guard.Against.NullOrWhiteSpace(containerName, nameof(containerName));

        if (_resilience is not null)
            return await _resilience.ExecuteAsync(PipelineName,
                async innerCt => { await _client.DeleteBucketAsync(containerName, innerCt); }, ct);

        try
        {
            await _client.DeleteBucketAsync(containerName, ct);
            return Result.Success();
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(Error.Internal($"S3 delete bucket failed: {ex.Message}"));
        }
    }

    public async Task<bool> ContainerExistsAsync(string containerName, CancellationToken ct = default)
    {
        Guard.Against.NullOrWhiteSpace(containerName, nameof(containerName));

        try
        {
            await _client.GetBucketLocationAsync(containerName, ct);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async Task<Result<IReadOnlyList<string>>> ListContainersAsync(CancellationToken ct = default)
    {
        if (_resilience is not null)
            return await _resilience.ExecuteAsync<IReadOnlyList<string>>(PipelineName, async innerCt =>
            {
                var response = await _client.ListBucketsAsync(innerCt);
                return response.Buckets.Select(b => b.BucketName).ToList().AsReadOnly();
            }, ct);

        try
        {
            var response = await _client.ListBucketsAsync(ct);
            IReadOnlyList<string> names = response.Buckets
                .Select(b => b.BucketName)
                .ToList()
                .AsReadOnly();
            return Result<IReadOnlyList<string>>.Success(names);
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<string>>.Failure(Error.Internal($"S3 list buckets failed: {ex.Message}"));
        }
    }

    public void Dispose() => _client.Dispose();
}
