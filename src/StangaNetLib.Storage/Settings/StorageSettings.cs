namespace StangaNetLib.Storage.Settings;

/// <summary>
/// Root settings for StangaNetLib.Storage. Bind to the <c>"StorageSettings"</c> configuration section.
/// </summary>
public sealed class StorageSettings
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "StorageSettings";

    /// <summary>Storage backend to use (default <see cref="StorageProvider.Local"/>).</summary>
    public StorageProvider Provider { get; set; } = StorageProvider.Local;

    /// <summary>
    /// Root directory for the <see cref="StorageProvider.Local"/> backend.
    /// Default: <c>"wwwroot/storage"</c>.
    /// </summary>
    public string LocalBasePath { get; set; } = "wwwroot/storage";

    /// <summary>
    /// URL prefix used to build pre-signed download URLs for the local backend
    /// (e.g. <c>"https://myapp.local/storage"</c>).
    /// Default: <c>""</c> — pre-signed URL generation is disabled until this is set.
    /// </summary>
    public string LocalBaseUrl { get; set; } = string.Empty;

    /// <summary>Optional at-rest encryption settings (Cryptography integration).</summary>
    public StorageEncryptionSettings Encryption { get; set; } = new();
}

/// <summary>Identifies the storage backend to register.</summary>
public enum StorageProvider
{
    /// <summary>Local filesystem — supported local-only / self-hosted production profile.</summary>
    Local = 0,

    /// <summary>AWS S3 (or S3-compatible, e.g. MinIO / LocalStack).</summary>
    S3 = 1,

    /// <summary>Azure Blob Storage.</summary>
    AzureBlob = 2,
}
