using System.ComponentModel.DataAnnotations;

namespace StangaNetLib.Storage.Settings;

/// <summary>
/// AWS S3 connection settings. Bind to the <c>"S3StorageSettings"</c> configuration section.
/// Used when <see cref="StorageSettings.Provider"/> is <see cref="StorageProvider.S3"/>.
/// </summary>
public sealed class S3StorageSettings
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "S3StorageSettings";

    /// <summary>AWS access key ID.</summary>
    [Required(AllowEmptyStrings = false)]
    public string AccessKey { get; set; } = string.Empty;

    /// <summary>AWS secret access key.</summary>
    [Required(AllowEmptyStrings = false)]
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>AWS region system name (e.g. <c>"eu-west-1"</c>). Default: <c>"eu-west-1"</c>.</summary>
    [Required(AllowEmptyStrings = false)]
    public string Region { get; set; } = "eu-west-1";

    /// <summary>
    /// Optional custom service endpoint for S3-compatible backends (LocalStack, MinIO).
    /// Default: <see langword="null"/> — uses the official AWS endpoint.
    /// </summary>
    public string? ServiceUrl { get; set; }

    /// <summary>
    /// Forces path-style addressing (<c>https://endpoint/bucket/key</c> instead of virtual-hosted).
    /// Required by most S3-compatible backends. Default: <see langword="false"/>.
    /// </summary>
    public bool ForcePathStyle { get; set; } = false;
}
