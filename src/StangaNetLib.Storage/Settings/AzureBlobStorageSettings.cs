using System.ComponentModel.DataAnnotations;

namespace StangaNetLib.Storage.Settings;

/// <summary>
/// Azure Blob Storage connection settings. Bind to the <c>"AzureBlobStorageSettings"</c> configuration section.
/// Used when <see cref="StorageSettings.Provider"/> is <see cref="StorageProvider.AzureBlob"/>.
/// </summary>
public sealed class AzureBlobStorageSettings
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "AzureBlobStorageSettings";

    /// <summary>
    /// Azure Storage connection string (e.g. <c>"DefaultEndpointsProtocol=https;AccountName=...;AccountKey=...;EndpointSuffix=core.windows.net"</c>).
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Optional custom service URI for Azurite or other Azure-compatible backends.
    /// Default: <see langword="null"/> — uses the official Azure endpoint derived from <see cref="ConnectionString"/>.
    /// </summary>
    public string? ServiceUrl { get; set; }
}
