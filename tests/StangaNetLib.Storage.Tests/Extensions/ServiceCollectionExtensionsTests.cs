using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StangaNetLib.Storage.Abstractions;
using StangaNetLib.Storage.Extensions;
using StangaNetLib.Storage.Implementations.Local;

namespace StangaNetLib.Storage.Tests.Extensions;

public sealed class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddStangaNetLibStorage_Local_RegistersExpectedServices()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["StorageSettings:Provider"] = "Local",
                ["StorageSettings:LocalBasePath"] = "tmp-storage",
                ["StorageSettings:LocalBaseUrl"] = "https://example.test/storage",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddStangaNetLibStorage(configuration);

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IBlobStorageService>().Should().BeOfType<LocalStorageProvider>();
        provider.GetRequiredService<IPresignedUrlService>().Should().BeOfType<LocalStorageProvider>();
        provider.GetRequiredService<IContainerManager>().Should().BeOfType<LocalStorageProvider>();
    }

    [Fact]
    public void AddStangaNetLibStorageLocal_RegistersLocalProvider()
    {
        var services = new ServiceCollection();
        services.AddStangaNetLibStorageLocal(basePath: "tmp-storage-local", baseUrl: "https://example.test/local");

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IBlobStorageService>().Should().BeOfType<LocalStorageProvider>();
        provider.GetRequiredService<IPresignedUrlService>().Should().BeOfType<LocalStorageProvider>();
        provider.GetRequiredService<IContainerManager>().Should().BeOfType<LocalStorageProvider>();
    }
}
