namespace StangaNetLib.Storage.Tests;

public sealed class PresignedUrlServiceTests : IDisposable
{
    private readonly string _tempDir;

    public PresignedUrlServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"storage-presigned-{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task GenerateDownloadUrlAsync_WithBaseUrlConfigured_ReturnsPresignedUrl()
    {
        var provider = MakeProvider("http://localhost/storage");

        var result = await provider.GenerateDownloadUrlAsync("mycontainer", "images/photo.jpg", TimeSpan.FromHours(1));

        result.IsSuccess.Should().BeTrue();
        result.Value!.Url.Should().StartWith("http://localhost/storage/mycontainer/images/photo.jpg");
        result.Value!.ExpiresAt.Should().BeCloseTo(DateTimeOffset.UtcNow.AddHours(1), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task GenerateUploadUrlAsync_WithBaseUrlConfigured_ReturnsPresignedUrl()
    {
        var provider = MakeProvider("https://cdn.example.com/storage");

        var result = await provider.GenerateUploadUrlAsync("uploads", "new-file.bin", TimeSpan.FromMinutes(30));

        result.IsSuccess.Should().BeTrue();
        result.Value!.Url.Should().Contain("uploads/new-file.bin");
        result.Value!.ExpiresAt.Should().BeCloseTo(DateTimeOffset.UtcNow.AddMinutes(30), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task GenerateDownloadUrlAsync_WithEmptyBaseUrl_ReturnsUnsupportedError()
    {
        var provider = MakeProvider(baseUrl: "");

        var result = await provider.GenerateDownloadUrlAsync("c", "file.txt", TimeSpan.FromHours(1));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Storage.PresignedUrlNotSupported");
    }

    [Fact]
    public async Task GenerateUploadUrlAsync_WithEmptyBaseUrl_ReturnsUnsupportedError()
    {
        var provider = MakeProvider(baseUrl: "");

        var result = await provider.GenerateUploadUrlAsync("c", "file.txt", TimeSpan.FromHours(1));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Storage.PresignedUrlNotSupported");
    }

    private LocalStorageProvider MakeProvider(string baseUrl) =>
        new(Options.Create(new StorageSettings
        {
            LocalBasePath = _tempDir,
            LocalBaseUrl  = baseUrl,
        }));
}
