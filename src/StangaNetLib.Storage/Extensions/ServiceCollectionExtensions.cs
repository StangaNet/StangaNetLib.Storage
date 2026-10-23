using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StangaNetLib.Cryptography.Abstractions;
using StangaNetLib.Resilience.Execution;
using StangaNetLib.Storage.Abstractions;
using StangaNetLib.Storage.Implementations;
using StangaNetLib.Storage.Implementations.AzureBlob;
using StangaNetLib.Storage.Implementations.Local;
using StangaNetLib.Storage.Implementations.S3;
using StangaNetLib.Storage.Settings;

namespace StangaNetLib.Storage.Extensions;

/// <summary>
/// Extension methods to register StangaNetLib.Storage services in the DI container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IBlobStorageService"/>, <see cref="IPresignedUrlService"/>,
    /// and <see cref="IContainerManager"/> using the backend configured in
    /// <c>StorageSettings:Provider</c>.
    /// </summary>
    /// <remarks>
    /// <para>Minimum required appsettings section: <c>"StorageSettings"</c>.</para>
    /// <para>When using <c>S3</c> also add <c>"S3StorageSettings"</c> — required fields are validated at application startup.</para>
    /// <para>When using <c>AzureBlob</c> also add <c>"AzureBlobStorageSettings"</c> — required fields are validated at application startup.</para>
    /// <para>
    /// When <c>StorageSettings:Encryption:EncryptAtRest</c> is <c>true</c>, register
    /// <c>AddStangaNetLibCryptography</c> first and supply a passphrase; uploads are wrapped
    /// by an encrypted decorator.
    /// </para>
    /// <para>
    /// If <see cref="StangaNetLib.Resilience.Execution.IResilientExecutor"/> is registered in
    /// the DI container before this call (via <c>AddStangaNetLibResilience</c>), it is injected
    /// automatically into the S3 and Azure Blob providers to apply the <c>"Storage"</c> resilience
    /// pipeline to all network I/O operations. The local provider does not use the executor.
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="configuration">Application configuration used to bind settings sections.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddStangaNetLibStorage(
        this IServiceCollection services,
        IConfiguration          configuration)
    {
        services.Configure<StorageSettings>(configuration.GetSection(StorageSettings.SectionName));

        var settings = configuration
            .GetSection(StorageSettings.SectionName)
            .Get<StorageSettings>() ?? new StorageSettings();

        RegisterProvider(services, settings, configuration);
        MaybeWrapEncryption(services, settings.Encryption);

        return services;
    }

    /// <summary>
    /// Registers the <c>Local</c> filesystem provider directly,
    /// without requiring an appsettings file.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="basePath">Root directory for blob files (default <c>"wwwroot/storage"</c>).</param>
    /// <param name="baseUrl">URL prefix used to build pre-signed URLs (default empty — pre-signed URL generation disabled).</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddStangaNetLibStorageLocal(
        this IServiceCollection services,
        string basePath = "wwwroot/storage",
        string baseUrl  = "")
    {
        services.Configure<StorageSettings>(opts =>
        {
            opts.Provider      = StorageProvider.Local;
            opts.LocalBasePath = basePath;
            opts.LocalBaseUrl  = baseUrl;
        });

        services.AddSingleton<LocalStorageProvider>();
        services.AddSingleton<IBlobStorageService>(sp  => sp.GetRequiredService<LocalStorageProvider>());
        services.AddSingleton<IPresignedUrlService>(sp => sp.GetRequiredService<LocalStorageProvider>());
        services.AddSingleton<IContainerManager>(sp    => sp.GetRequiredService<LocalStorageProvider>());

        return services;
    }

    /// <summary>
    /// Local-only production profile: forces <see cref="StorageProvider.Local"/> and optionally
    /// enables at-rest encryption (requires <c>AddStangaNetLibCryptography</c>).
    /// </summary>
    public static IServiceCollection AddStangaNetLibStorageLocalOnly(
        this IServiceCollection services,
        string basePath = "wwwroot/storage",
        string baseUrl = "",
        bool encryptAtRest = false,
        string? encryptionPassphrase = null)
    {
        var encryption = new StorageEncryptionSettings
        {
            EncryptAtRest = encryptAtRest,
            Passphrase = encryptionPassphrase ?? string.Empty,
        };

        services.Configure<StorageSettings>(opts =>
        {
            opts.Provider = StorageProvider.Local;
            opts.LocalBasePath = basePath;
            opts.LocalBaseUrl = baseUrl;
            opts.Encryption = encryption;
        });

        services.AddSingleton<LocalStorageProvider>();
        services.AddSingleton<IPresignedUrlService>(sp => sp.GetRequiredService<LocalStorageProvider>());
        services.AddSingleton<IContainerManager>(sp => sp.GetRequiredService<LocalStorageProvider>());
        services.AddSingleton<IBlobStorageService>(sp => sp.GetRequiredService<LocalStorageProvider>());

        MaybeWrapEncryption(services, encryption);
        return services;
    }

    private static void RegisterProvider(
        IServiceCollection services,
        StorageSettings settings,
        IConfiguration configuration)
    {
        switch (settings.Provider)
        {
            case StorageProvider.S3:
                services.AddOptions<S3StorageSettings>()
                    .Bind(configuration.GetSection(S3StorageSettings.SectionName))
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                services.AddSingleton<S3StorageProvider>(sp => new S3StorageProvider(
                    sp.GetRequiredService<IOptions<S3StorageSettings>>(),
                    sp.GetService<IResilientExecutor>()));
                services.AddSingleton<IBlobStorageService>(sp  => sp.GetRequiredService<S3StorageProvider>());
                services.AddSingleton<IPresignedUrlService>(sp => sp.GetRequiredService<S3StorageProvider>());
                services.AddSingleton<IContainerManager>(sp    => sp.GetRequiredService<S3StorageProvider>());
                break;

            case StorageProvider.AzureBlob:
                services.AddOptions<AzureBlobStorageSettings>()
                    .Bind(configuration.GetSection(AzureBlobStorageSettings.SectionName))
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                services.AddSingleton<AzureBlobStorageProvider>(sp => new AzureBlobStorageProvider(
                    sp.GetRequiredService<IOptions<AzureBlobStorageSettings>>(),
                    sp.GetService<IResilientExecutor>()));
                services.AddSingleton<IBlobStorageService>(sp  => sp.GetRequiredService<AzureBlobStorageProvider>());
                services.AddSingleton<IPresignedUrlService>(sp => sp.GetRequiredService<AzureBlobStorageProvider>());
                services.AddSingleton<IContainerManager>(sp    => sp.GetRequiredService<AzureBlobStorageProvider>());
                break;

            default:
                services.AddSingleton<LocalStorageProvider>();
                services.AddSingleton<IBlobStorageService>(sp  => sp.GetRequiredService<LocalStorageProvider>());
                services.AddSingleton<IPresignedUrlService>(sp => sp.GetRequiredService<LocalStorageProvider>());
                services.AddSingleton<IContainerManager>(sp    => sp.GetRequiredService<LocalStorageProvider>());
                break;
        }
    }

    private static void MaybeWrapEncryption(IServiceCollection services, StorageEncryptionSettings encryption)
    {
        if (!encryption.EncryptAtRest)
            return;

        if (string.IsNullOrEmpty(encryption.Passphrase))
        {
            throw new InvalidOperationException(
                "StorageSettings.Encryption.EncryptAtRest is true but Passphrase is empty.");
        }

        // Replace IBlobStorageService registration with a decorator factory.
        var existing = services.LastOrDefault(d => d.ServiceType == typeof(IBlobStorageService))
            ?? throw new InvalidOperationException("IBlobStorageService must be registered before encryption wrapping.");

        if (existing.ImplementationFactory is null)
        {
            throw new InvalidOperationException(
                "EncryptAtRest requires IBlobStorageService to be registered via a factory (as done by AddStangaNetLibStorage*).");
        }

        var innerFactory = existing.ImplementationFactory;
        services.Remove(existing);

        services.AddSingleton<IBlobStorageService>(sp =>
        {
            var encryptionService = sp.GetService<IAtRestEncryptionService>()
                ?? throw new InvalidOperationException(
                    "EncryptAtRest requires AddStangaNetLibCryptography (IAtRestEncryptionService) to be registered first.");

            var inner = (IBlobStorageService)innerFactory(sp);
            return new EncryptedBlobStorageService(inner, encryptionService, encryption);
        });
    }
}
