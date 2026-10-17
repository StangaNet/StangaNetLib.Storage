using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Options;
using StangaNetLib.Core.Common;
using StangaNetLib.Core.Guards;
using StangaNetLib.Storage.Abstractions;
using StangaNetLib.Storage.Models;
using StangaNetLib.Storage.Settings;

namespace StangaNetLib.Storage.Implementations.Local;

/// <summary>
/// Local filesystem storage provider implementing <see cref="IBlobStorageService"/>,
/// <see cref="IPresignedUrlService"/>, and <see cref="IContainerManager"/>.
/// Intended for development and integration testing; not suitable for production multi-instance deployments.
/// Blob metadata is stored in companion <c>.storagemeta</c> JSON sidecar files adjacent to each blob.
/// </summary>
internal sealed class LocalStorageProvider : IBlobStorageService, IPresignedUrlService, IContainerManager
{
    private const string MetaSuffix = ".storagemeta";

    private readonly StorageSettings _settings;

    public LocalStorageProvider(IOptions<StorageSettings> options)
    {
        _settings = Guard.Against.Null(options, nameof(options)).Value;
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

        try
        {
            var filePath = GetFilePath(containerName, blobKey);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

            await using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await content.CopyToAsync(fs, ct);
            }

            var sidecar = new BlobSidecar(contentType, metadata?.ToDictionary(kv => kv.Key, kv => kv.Value) ?? []);
            await File.WriteAllTextAsync(GetMetaFilePath(filePath), JsonSerializer.Serialize(sidecar), ct);

            var fileInfo = new FileInfo(filePath);
            var etag     = ComputeETag(filePath);

            return Result<BlobUploadResult>.Success(new BlobUploadResult(containerName, blobKey, contentType, etag, fileInfo.Length));
        }
        catch (Exception ex)
        {
            return Result<BlobUploadResult>.Failure(Error.Internal($"Upload failed: {ex.Message}"));
        }
    }

    public Task<Result<BlobDownloadResult>> DownloadAsync(
        string containerName, string blobKey, CancellationToken ct = default)
    {
        Guard.Against.NullOrWhiteSpace(containerName, nameof(containerName));
        Guard.Against.NullOrWhiteSpace(blobKey, nameof(blobKey));

        var filePath = GetFilePath(containerName, blobKey);
        if (!File.Exists(filePath))
            return Task.FromResult(Result<BlobDownloadResult>.Failure(Error.NotFound("Blob", $"{containerName}/{blobKey}")));

        try
        {
            var sidecar     = ReadSidecar(filePath);
            var stream      = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
            var fileInfo    = new FileInfo(filePath);
            var result      = new BlobDownloadResult(
                stream,
                sidecar?.ContentType ?? "application/octet-stream",
                fileInfo.Length,
                (IReadOnlyDictionary<string, string>)(sidecar?.Metadata ?? new Dictionary<string, string>()));

            return Task.FromResult(Result<BlobDownloadResult>.Success(result));
        }
        catch (Exception ex)
        {
            return Task.FromResult(Result<BlobDownloadResult>.Failure(Error.Internal($"Download failed: {ex.Message}")));
        }
    }

    public Task<Result> DeleteAsync(string containerName, string blobKey, CancellationToken ct = default)
    {
        Guard.Against.NullOrWhiteSpace(containerName, nameof(containerName));
        Guard.Against.NullOrWhiteSpace(blobKey, nameof(blobKey));

        try
        {
            var filePath = GetFilePath(containerName, blobKey);
            if (File.Exists(filePath))
                File.Delete(filePath);

            var metaPath = GetMetaFilePath(filePath);
            if (File.Exists(metaPath))
                File.Delete(metaPath);

            return Task.FromResult(Result.Success());
        }
        catch (Exception ex)
        {
            return Task.FromResult(Result.Failure(Error.Internal($"Delete failed: {ex.Message}")));
        }
    }

    public Task<bool> ExistsAsync(string containerName, string blobKey, CancellationToken ct = default)
    {
        Guard.Against.NullOrWhiteSpace(containerName, nameof(containerName));
        Guard.Against.NullOrWhiteSpace(blobKey, nameof(blobKey));

        return Task.FromResult(File.Exists(GetFilePath(containerName, blobKey)));
    }

    public Task<Result<BlobMetadata>> GetMetadataAsync(string containerName, string blobKey, CancellationToken ct = default)
    {
        Guard.Against.NullOrWhiteSpace(containerName, nameof(containerName));
        Guard.Against.NullOrWhiteSpace(blobKey, nameof(blobKey));

        var filePath = GetFilePath(containerName, blobKey);
        if (!File.Exists(filePath))
            return Task.FromResult(Result<BlobMetadata>.Failure(Error.NotFound("Blob", $"{containerName}/{blobKey}")));

        var fileInfo = new FileInfo(filePath);
        var sidecar  = ReadSidecar(filePath);
        var metadata = new BlobMetadata(
            containerName,
            blobKey,
            sidecar?.ContentType ?? "application/octet-stream",
            fileInfo.Length,
            new DateTimeOffset(fileInfo.LastWriteTimeUtc, TimeSpan.Zero),
            (IReadOnlyDictionary<string, string>)(sidecar?.Metadata ?? new Dictionary<string, string>()));

        return Task.FromResult(Result<BlobMetadata>.Success(metadata));
    }

    public Task<Result<IReadOnlyList<BlobItem>>> ListBlobsAsync(
        string containerName, string? prefix = null, CancellationToken ct = default)
    {
        Guard.Against.NullOrWhiteSpace(containerName, nameof(containerName));

        var containerPath = GetContainerPath(containerName);
        if (!Directory.Exists(containerPath))
            return Task.FromResult(Result<IReadOnlyList<BlobItem>>.Success(
                (IReadOnlyList<BlobItem>)Array.Empty<BlobItem>()));

        try
        {
            var files = Directory.GetFiles(containerPath, "*", SearchOption.AllDirectories);
            IReadOnlyList<BlobItem> items = files
                .Select(f => Path.GetRelativePath(containerPath, f).Replace('\\', '/'))
                .Where(rel => !rel.EndsWith(MetaSuffix, StringComparison.Ordinal))
                .Where(rel => prefix is null || rel.StartsWith(prefix, StringComparison.Ordinal))
                .Select(rel =>
                {
                    var info = new FileInfo(Path.Combine(containerPath, rel));
                    return new BlobItem(containerName, rel, info.Length,
                        new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero));
                })
                .ToList()
                .AsReadOnly();

            return Task.FromResult(Result<IReadOnlyList<BlobItem>>.Success(items));
        }
        catch (Exception ex)
        {
            return Task.FromResult(Result<IReadOnlyList<BlobItem>>.Failure(
                Error.Internal($"List failed: {ex.Message}")));
        }
    }

    public Task<Result> CopyAsync(
        string sourceContainer, string sourceBlobKey, string destContainer, string destBlobKey, CancellationToken ct = default)
    {
        Guard.Against.NullOrWhiteSpace(sourceContainer, nameof(sourceContainer));
        Guard.Against.NullOrWhiteSpace(sourceBlobKey, nameof(sourceBlobKey));
        Guard.Against.NullOrWhiteSpace(destContainer, nameof(destContainer));
        Guard.Against.NullOrWhiteSpace(destBlobKey, nameof(destBlobKey));

        var sourcePath = GetFilePath(sourceContainer, sourceBlobKey);
        if (!File.Exists(sourcePath))
            return Task.FromResult(Result.Failure(Error.NotFound("Blob", $"{sourceContainer}/{sourceBlobKey}")));

        try
        {
            var destPath = GetFilePath(destContainer, destBlobKey);
            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
            File.Copy(sourcePath, destPath, overwrite: true);

            var sourceMetaPath = GetMetaFilePath(sourcePath);
            if (File.Exists(sourceMetaPath))
                File.Copy(sourceMetaPath, GetMetaFilePath(destPath), overwrite: true);

            return Task.FromResult(Result.Success());
        }
        catch (Exception ex)
        {
            return Task.FromResult(Result.Failure(Error.Internal($"Copy failed: {ex.Message}")));
        }
    }

    public async Task<Result> MoveAsync(
        string sourceContainer, string sourceBlobKey, string destContainer, string destBlobKey, CancellationToken ct = default)
    {
        var copyResult = await CopyAsync(sourceContainer, sourceBlobKey, destContainer, destBlobKey, ct);
        if (copyResult.IsFailure) return copyResult;
        return await DeleteAsync(sourceContainer, sourceBlobKey, ct);
    }

    // ── IPresignedUrlService ─────────────────────────────────────────────────

    public Task<Result<PresignedUrl>> GenerateUploadUrlAsync(
        string containerName, string blobKey, TimeSpan expiry, string? contentType = null, CancellationToken ct = default)
        => BuildLocalPresignedUrl(containerName, blobKey, expiry);

    public Task<Result<PresignedUrl>> GenerateDownloadUrlAsync(
        string containerName, string blobKey, TimeSpan expiry, CancellationToken ct = default)
        => BuildLocalPresignedUrl(containerName, blobKey, expiry);

    private Task<Result<PresignedUrl>> BuildLocalPresignedUrl(string containerName, string blobKey, TimeSpan expiry)
    {
        Guard.Against.NullOrWhiteSpace(containerName, nameof(containerName));
        Guard.Against.NullOrWhiteSpace(blobKey, nameof(blobKey));

        if (string.IsNullOrWhiteSpace(_settings.LocalBaseUrl))
            return Task.FromResult(Result<PresignedUrl>.Failure(new Error(
                "Storage.PresignedUrlNotSupported",
                "Pre-signed URLs require StorageSettings:LocalBaseUrl to be configured.",
                ErrorType.Validation)));

        var expiresAt = DateTimeOffset.UtcNow.Add(expiry);
        var url       = $"{_settings.LocalBaseUrl.TrimEnd('/')}/{containerName}/{blobKey}?expires={Uri.EscapeDataString(expiresAt.ToString("O"))}";
        return Task.FromResult(Result<PresignedUrl>.Success(new PresignedUrl(url, expiresAt)));
    }

    // ── IContainerManager ────────────────────────────────────────────────────

    public Task<Result> CreateContainerAsync(string containerName, CancellationToken ct = default)
    {
        Guard.Against.NullOrWhiteSpace(containerName, nameof(containerName));

        try
        {
            Directory.CreateDirectory(GetContainerPath(containerName));
            return Task.FromResult(Result.Success());
        }
        catch (Exception ex)
        {
            return Task.FromResult(Result.Failure(Error.Internal($"Create container failed: {ex.Message}")));
        }
    }

    public Task<Result> DeleteContainerAsync(string containerName, CancellationToken ct = default)
    {
        Guard.Against.NullOrWhiteSpace(containerName, nameof(containerName));

        try
        {
            var path = GetContainerPath(containerName);
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
            return Task.FromResult(Result.Success());
        }
        catch (Exception ex)
        {
            return Task.FromResult(Result.Failure(Error.Internal($"Delete container failed: {ex.Message}")));
        }
    }

    public Task<bool> ContainerExistsAsync(string containerName, CancellationToken ct = default)
    {
        Guard.Against.NullOrWhiteSpace(containerName, nameof(containerName));
        return Task.FromResult(Directory.Exists(GetContainerPath(containerName)));
    }

    public Task<Result<IReadOnlyList<string>>> ListContainersAsync(CancellationToken ct = default)
    {
        try
        {
            if (!Directory.Exists(_settings.LocalBasePath))
                return Task.FromResult(Result<IReadOnlyList<string>>.Success(
                    (IReadOnlyList<string>)Array.Empty<string>()));

            IReadOnlyList<string> names = Directory
                .GetDirectories(_settings.LocalBasePath)
                .Select(Path.GetFileName)
                .Where(n => n is not null)
                .Select(n => n!)
                .ToList()
                .AsReadOnly();

            return Task.FromResult(Result<IReadOnlyList<string>>.Success(names));
        }
        catch (Exception ex)
        {
            return Task.FromResult(Result<IReadOnlyList<string>>.Failure(
                Error.Internal($"List containers failed: {ex.Message}")));
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private string GetContainerPath(string containerName)
        => Path.Combine(_settings.LocalBasePath, containerName);

    private string GetFilePath(string containerName, string blobKey)
        => Path.Combine(_settings.LocalBasePath, containerName,
            blobKey.Replace('/', Path.DirectorySeparatorChar));

    private static string GetMetaFilePath(string filePath)
        => filePath + MetaSuffix;

    private static BlobSidecar? ReadSidecar(string filePath)
    {
        var metaPath = GetMetaFilePath(filePath);
        if (!File.Exists(metaPath)) return null;

        try
        {
            return JsonSerializer.Deserialize<BlobSidecar>(File.ReadAllText(metaPath));
        }
        catch
        {
            // Corrupt sidecar: fall back to defaults rather than failing the operation.
            return null;
        }
    }

    private static string ComputeETag(string filePath)
    {
        using var md5    = MD5.Create();
        using var stream = File.OpenRead(filePath);
        return Convert.ToHexString(md5.ComputeHash(stream)).ToLowerInvariant();
    }

    private sealed record BlobSidecar(string ContentType, Dictionary<string, string> Metadata);
}
