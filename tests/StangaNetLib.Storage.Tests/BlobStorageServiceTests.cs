namespace StangaNetLib.Storage.Tests;

public sealed class BlobStorageServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly LocalStorageProvider _provider;

    public BlobStorageServiceTests()
    {
        _tempDir  = Path.Combine(Path.GetTempPath(), $"storage-tests-{Guid.NewGuid():N}");
        _provider = new LocalStorageProvider(Options.Create(new StorageSettings
        {
            LocalBasePath = _tempDir,
            LocalBaseUrl  = "http://localhost/storage",
        }));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ── UploadAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task UploadAsync_NewBlob_ReturnsSuccessWithCorrectKeys()
    {
        using var stream = ToStream("hello world");

        var result = await _provider.UploadAsync("container1", "test.txt", stream, "text/plain");

        result.IsSuccess.Should().BeTrue();
        result.Value!.ContainerName.Should().Be("container1");
        result.Value!.BlobKey.Should().Be("test.txt");
        result.Value!.SizeInBytes.Should().Be(11);
        result.Value!.ETag.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task UploadAsync_ExistingBlob_OverwritesSuccessfully()
    {
        using var first  = ToStream("original");
        using var second = ToStream("overwritten");

        await _provider.UploadAsync("c", "key.txt", first, "text/plain");
        var result = await _provider.UploadAsync("c", "key.txt", second, "text/plain");

        result.IsSuccess.Should().BeTrue();
        result.Value!.SizeInBytes.Should().Be(11);
    }

    [Fact]
    public async Task UploadAsync_NestedBlobKey_CreatesIntermediateDirectories()
    {
        using var stream = ToStream("data");

        var result = await _provider.UploadAsync("bucket", "images/2024/photo.jpg", stream, "image/jpeg");

        result.IsSuccess.Should().BeTrue();
        result.Value!.BlobKey.Should().Be("images/2024/photo.jpg");
    }

    // ── DownloadAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task DownloadAsync_ExistingBlob_ReturnsContent()
    {
        using var uploadStream = ToStream("download-me");
        await _provider.UploadAsync("c", "file.txt", uploadStream, "text/plain");

        var result = await _provider.DownloadAsync("c", "file.txt");

        result.IsSuccess.Should().BeTrue();
        await using var content = result.Value!.Content;
        var text = await new StreamReader(content).ReadToEndAsync();
        text.Should().Be("download-me");
    }

    [Fact]
    public async Task DownloadAsync_MissingBlob_ReturnsNotFoundError()
    {
        var result = await _provider.DownloadAsync("c", "does-not-exist.txt");

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(StangaNetLib.Core.Common.ErrorType.NotFound);
    }

    // ── DeleteAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_ExistingBlob_RemovesBlob()
    {
        using var stream = ToStream("delete-me");
        await _provider.UploadAsync("c", "to-delete.txt", stream, "text/plain");

        var result = await _provider.DeleteAsync("c", "to-delete.txt");

        result.IsSuccess.Should().BeTrue();
        var exists = await _provider.ExistsAsync("c", "to-delete.txt");
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_NonExistingBlob_ReturnsSuccess()
    {
        var result = await _provider.DeleteAsync("c", "ghost.txt");

        result.IsSuccess.Should().BeTrue();
    }

    // ── ExistsAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ExistsAsync_ExistingBlob_ReturnsTrue()
    {
        using var stream = ToStream("x");
        await _provider.UploadAsync("c", "exists.txt", stream, "text/plain");

        var exists = await _provider.ExistsAsync("c", "exists.txt");

        exists.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsync_MissingBlob_ReturnsFalse()
    {
        var exists = await _provider.ExistsAsync("c", "nope.txt");

        exists.Should().BeFalse();
    }

    // ── GetMetadataAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetMetadataAsync_ExistingBlob_ReturnsCorrectSize()
    {
        using var stream = ToStream("metadata-test");
        await _provider.UploadAsync("c", "meta.txt", stream, "text/plain");

        var result = await _provider.GetMetadataAsync("c", "meta.txt");

        result.IsSuccess.Should().BeTrue();
        result.Value!.SizeInBytes.Should().Be(13);
        result.Value!.ContainerName.Should().Be("c");
        result.Value!.BlobKey.Should().Be("meta.txt");
    }

    [Fact]
    public async Task GetMetadataAsync_MissingBlob_ReturnsNotFoundError()
    {
        var result = await _provider.GetMetadataAsync("c", "missing.txt");

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(StangaNetLib.Core.Common.ErrorType.NotFound);
    }

    // ── ListBlobsAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task ListBlobsAsync_EmptyContainer_ReturnsEmptyList()
    {
        var result = await _provider.ListBlobsAsync("empty-container");

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().BeEmpty();
    }

    [Fact]
    public async Task ListBlobsAsync_ContainerWithBlobs_ReturnsAllBlobs()
    {
        using var s1 = ToStream("a");
        using var s2 = ToStream("b");
        await _provider.UploadAsync("mycontainer", "file1.txt", s1, "text/plain");
        await _provider.UploadAsync("mycontainer", "file2.txt", s2, "text/plain");

        var result = await _provider.ListBlobsAsync("mycontainer");

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().HaveCount(2);
        result.Value!.Select(b => b.BlobKey).Should().Contain("file1.txt");
        result.Value!.Select(b => b.BlobKey).Should().Contain("file2.txt");
    }

    [Fact]
    public async Task ListBlobsAsync_WithPrefix_ReturnsFilteredBlobs()
    {
        using var s1 = ToStream("a");
        using var s2 = ToStream("b");
        using var s3 = ToStream("c");
        await _provider.UploadAsync("bucket", "images/photo1.jpg", s1, "image/jpeg");
        await _provider.UploadAsync("bucket", "images/photo2.jpg", s2, "image/jpeg");
        await _provider.UploadAsync("bucket", "docs/readme.txt",   s3, "text/plain");

        var result = await _provider.ListBlobsAsync("bucket", prefix: "images/");

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().HaveCount(2);
        result.Value!.All(b => b.BlobKey.StartsWith("images/")).Should().BeTrue();
    }

    // ── CopyAsync ────────────────────────────────────────────────────────────

    [Fact]
    public async Task CopyAsync_ExistingBlob_CreatesCopyAndKeepsSource()
    {
        using var stream = ToStream("copy-me");
        await _provider.UploadAsync("src", "original.txt", stream, "text/plain");

        var result = await _provider.CopyAsync("src", "original.txt", "dst", "copy.txt");

        result.IsSuccess.Should().BeTrue();
        (await _provider.ExistsAsync("src", "original.txt")).Should().BeTrue();
        (await _provider.ExistsAsync("dst", "copy.txt")).Should().BeTrue();
    }

    [Fact]
    public async Task CopyAsync_MissingSourceBlob_ReturnsNotFoundError()
    {
        var result = await _provider.CopyAsync("src", "ghost.txt", "dst", "copy.txt");

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(StangaNetLib.Core.Common.ErrorType.NotFound);
    }

    // ── MoveAsync ────────────────────────────────────────────────────────────

    [Fact]
    public async Task MoveAsync_ExistingBlob_DeletesSourceAndCreatesDestination()
    {
        using var stream = ToStream("move-me");
        await _provider.UploadAsync("src", "original.txt", stream, "text/plain");

        var result = await _provider.MoveAsync("src", "original.txt", "dst", "moved.txt");

        result.IsSuccess.Should().BeTrue();
        (await _provider.ExistsAsync("src", "original.txt")).Should().BeFalse();
        (await _provider.ExistsAsync("dst", "moved.txt")).Should().BeTrue();
    }

    // ── ContentType ──────────────────────────────────────────────────────────

    [Fact]
    public async Task UploadAsync_ContentTypeIsPreservedOnUploadResult()
    {
        using var stream = ToStream("data");

        var result = await _provider.UploadAsync("c", "file.jpg", stream, "image/jpeg");

        result.IsSuccess.Should().BeTrue();
        result.Value!.ContentType.Should().Be("image/jpeg");
    }

    [Fact]
    public async Task DownloadAsync_ContentTypeMatchesUploadedContentType()
    {
        using var uploadStream = ToStream("image data");
        await _provider.UploadAsync("c", "photo.jpg", uploadStream, "image/jpeg");

        var result = await _provider.DownloadAsync("c", "photo.jpg");

        result.IsSuccess.Should().BeTrue();
        result.Value!.ContentType.Should().Be("image/jpeg");
        await result.Value!.Content.DisposeAsync();
    }

    [Fact]
    public async Task GetMetadataAsync_ContentTypeMatchesUploadedContentType()
    {
        using var stream = ToStream("pdf data");
        await _provider.UploadAsync("c", "doc.pdf", stream, "application/pdf");

        var result = await _provider.GetMetadataAsync("c", "doc.pdf");

        result.IsSuccess.Should().BeTrue();
        result.Value!.ContentType.Should().Be("application/pdf");
    }

    // ── Custom Metadata ──────────────────────────────────────────────────────

    [Fact]
    public async Task UploadAsync_WithMetadata_MetadataIsRoundTripped()
    {
        using var stream   = ToStream("tagged content");
        var meta = new Dictionary<string, string> { ["author"] = "alice", ["version"] = "2" };

        await _provider.UploadAsync("c", "tagged.txt", stream, "text/plain", metadata: meta);

        var result = await _provider.GetMetadataAsync("c", "tagged.txt");
        result.IsSuccess.Should().BeTrue();
        result.Value!.Tags.Should().ContainKey("author").WhoseValue.Should().Be("alice");
        result.Value!.Tags.Should().ContainKey("version").WhoseValue.Should().Be("2");
    }

    [Fact]
    public async Task ListBlobsAsync_SidecarFiles_AreNotIncludedInListing()
    {
        using var s1 = ToStream("a");
        using var s2 = ToStream("b");
        await _provider.UploadAsync("bucket", "file1.txt", s1, "text/plain");
        await _provider.UploadAsync("bucket", "file2.txt", s2, "text/plain");

        var result = await _provider.ListBlobsAsync("bucket");

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().HaveCount(2);
        result.Value!.All(b => !b.BlobKey.EndsWith(".storagemeta")).Should().BeTrue();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static MemoryStream ToStream(string text)
        => new(System.Text.Encoding.UTF8.GetBytes(text));
}
