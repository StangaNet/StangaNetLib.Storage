using StangaNetLib.Core.Common;

namespace StangaNetLib.Storage.Abstractions;

/// <summary>
/// Manages the lifecycle of storage containers (S3 buckets, Azure Blob containers, or local directories).
/// Implementations should be registered as <c>Singleton</c>.
/// </summary>
public interface IContainerManager
{
    /// <summary>
    /// Creates a container. If the container already exists the operation is a no-op and returns success.
    /// </summary>
    /// <param name="containerName">Name of the container to create.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><see cref="Result"/> indicating success or an <see cref="ErrorType.Internal"/> failure.</returns>
    Task<Result> CreateContainerAsync(string containerName, CancellationToken ct = default);

    /// <summary>
    /// Deletes a container and all blobs it contains.
    /// Safe to call when the container does not exist — returns success.
    /// </summary>
    /// <param name="containerName">Name of the container to delete.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><see cref="Result"/> indicating success or an <see cref="ErrorType.Internal"/> failure.</returns>
    Task<Result> DeleteContainerAsync(string containerName, CancellationToken ct = default);

    /// <summary>
    /// Returns <see langword="true"/> if the specified container exists.
    /// </summary>
    /// <param name="containerName">Name to check.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><see langword="true"/> if the container exists; otherwise <see langword="false"/>.</returns>
    Task<bool> ContainerExistsAsync(string containerName, CancellationToken ct = default);

    /// <summary>
    /// Lists all containers visible to the current credentials.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// <see cref="Result{T}"/> containing container names on success, or an
    /// <see cref="ErrorType.Internal"/> failure.
    /// </returns>
    Task<Result<IReadOnlyList<string>>> ListContainersAsync(CancellationToken ct = default);
}
