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
    /// Build the Tissue proxy image URL for a remote image.
    /// </summary>
    /// <param name="imageUrl">Remote image URL.</param>
    /// <returns>Resolved same-origin proxy URL, or empty on failure.</returns>
    string BuildProxyImageUrl(string imageUrl);

    /// <summary>
    /// Download actor image response from Tissue proxy endpoint.
    /// </summary>
    /// <param name="imageUrlOrProxyUrl">Remote image URL or resolved same-origin proxy URL.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>HTTP response with image stream, or <c>null</c> on failure.</returns>
    Task<HttpResponseMessage?> GetImageResponseAsync(string imageUrlOrProxyUrl, CancellationToken cancellationToken);
}
