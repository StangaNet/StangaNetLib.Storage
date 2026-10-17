namespace StangaNetLib.Storage.Models;

/// <summary>A time-limited URL granting unauthenticated access to a single blob operation.</summary>
/// <param name="Url">The pre-signed URL to use in an HTTP request.</param>
/// <param name="ExpiresAt">The point in time after which the URL is no longer valid.</param>
public sealed record PresignedUrl(string Url, DateTimeOffset ExpiresAt);
