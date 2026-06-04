using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.Tissue.Services;

/// <summary>
/// Tissue HTTP client abstraction.
/// </summary>
public interface ITissueClient
{
    /// <summary>
    /// Resolve actor image candidates from Tissue.
    /// </summary>
    /// <param name="actorName">Actor display name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Resolved actor images, or empty if no match.</returns>
    Task<IReadOnlyList<ResolvedActorImage>> ResolveActorImagesAsync(string actorName, CancellationToken cancellationToken);

    /// <summary>
    /// Download actor image response from Tissue proxy endpoint.
    /// </summary>
    /// <param name="proxyUrl">Resolved same-origin proxy URL.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>HTTP response with image stream, or <c>null</c> on failure.</returns>
    Task<HttpResponseMessage?> GetImageResponseAsync(string proxyUrl, CancellationToken cancellationToken);
}
