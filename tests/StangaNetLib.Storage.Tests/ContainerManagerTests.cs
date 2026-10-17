namespace StangaNetLib.Storage.Tests;

public sealed class ContainerManagerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly LocalStorageProvider _provider;

    public ContainerManagerTests()
    {
        _tempDir  = Path.Combine(Path.GetTempPath(), $"storage-container-{Guid.NewGuid():N}");
        _provider = new LocalStorageProvider(Options.Create(new StorageSettings
        {
            LocalBasePath = _tempDir,
        }));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task CreateContainerAsync_NewContainer_ReturnsSuccess()
    {
        var result = await _provider.CreateContainerAsync("new-container");

        result.IsSuccess.Should().BeTrue();
        (await _provider.ContainerExistsAsync("new-container")).Should().BeTrue();
    }

    [Fact]
    public async Task CreateContainerAsync_ExistingContainer_ReturnsSuccessIdempotent()
    {
        await _provider.CreateContainerAsync("existing");

        var result = await _provider.CreateContainerAsync("existing");

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteContainerAsync_ExistingContainer_RemovesContainerAndBlobs()
    {
        await _provider.CreateContainerAsync("to-delete");
        using var stream = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes("data"));
        await _provider.UploadAsync("to-delete", "file.txt", stream, "text/plain");

        var result = await _provider.DeleteContainerAsync("to-delete");

        result.IsSuccess.Should().BeTrue();
        (await _provider.ContainerExistsAsync("to-delete")).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteContainerAsync_NonExistingContainer_ReturnsSuccessIdempotent()
    {
        var result = await _provider.DeleteContainerAsync("does-not-exist");

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ContainerExistsAsync_ExistingContainer_ReturnsTrue()
    {
        await _provider.CreateContainerAsync("present");

        var exists = await _provider.ContainerExistsAsync("present");

        exists.Should().BeTrue();
    }

    [Fact]
    public async Task ContainerExistsAsync_NonExistingContainer_ReturnsFalse()
    {
        var exists = await _provider.ContainerExistsAsync("absent");

        exists.Should().BeFalse();
    }

    // ── ListContainersAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task ListContainersAsync_NoContainers_ReturnsEmptyList()
    {
        var result = await _provider.ListContainersAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().BeEmpty();
    }

    [Fact]
    public async Task ListContainersAsync_MultipleContainers_ReturnsAllNames()
    {
        await _provider.CreateContainerAsync("alpha");
        await _provider.CreateContainerAsync("beta");
        await _provider.CreateContainerAsync("gamma");

        var result = await _provider.ListContainersAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().Contain("alpha").And.Contain("beta").And.Contain("gamma");
    }
}
