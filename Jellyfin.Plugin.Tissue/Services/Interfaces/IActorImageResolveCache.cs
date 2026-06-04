using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.Tissue.Services;

/// <summary>
/// Coordinates cached actor image resolve requests.
/// </summary>
public interface IActorImageResolveCache
{
    /// <summary>
    /// Resolve actor image candidates with short-lived caching and in-flight de-duplication.
    /// </summary>
    /// <param name="actorName">Actor display name.</param>
    /// <param name="cancellationToken">Cancellation token for the caller waiting on the result.</param>
    /// <returns>Resolved actor images, or empty if no match.</returns>
    Task<IReadOnlyList<ResolvedActorImage>> ResolveActorImagesAsync(string actorName, CancellationToken cancellationToken);
}
