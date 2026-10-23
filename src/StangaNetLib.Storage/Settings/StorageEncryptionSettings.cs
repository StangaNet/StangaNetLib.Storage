namespace StangaNetLib.Storage.Settings;

/// <summary>
/// At-rest encryption options for blob storage. Bound from <c>StorageSettings:Encryption</c>
/// or configured via <c>AddStangaNetLibStorageLocalOnly</c>.
/// </summary>
public sealed class StorageEncryptionSettings
{
    /// <summary>
    /// When <c>true</c>, uploads are encrypted with <c>IAtRestEncryptionService</c> before
    /// being written to the underlying provider and decrypted on download.
    /// Requires <c>AddStangaNetLibCryptography</c> and a non-empty <see cref="Passphrase"/>.
    /// </summary>
    public bool EncryptAtRest { get; set; }

    /// <summary>
    /// Passphrase used to derive the AES key via PBKDF2. Prefer secrets configuration
    /// (environment / secret store) over committing this value to source control.
    /// </summary>
    public string Passphrase { get; set; } = string.Empty;
}
