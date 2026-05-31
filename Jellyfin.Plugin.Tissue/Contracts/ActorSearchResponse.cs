using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Tissue.Contracts;

/// <summary>
/// Actor search API response with unified wrapper.
/// </summary>
public sealed class ActorSearchResponse
{
    /// <summary>
    /// Gets or sets a value indicating whether the request succeeds.
    /// </summary>
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    /// <summary>
    /// Gets or sets optional response details.
    /// </summary>
    [JsonPropertyName("details")]
    public string? Details { get; set; }

    /// <summary>
    /// Gets or sets actor result entries.
    /// </summary>
    [JsonPropertyName("data")]
    [SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "DTO must be writable for JSON deserialization.")]
    public IList<ActorSearchItem> Data { get; set; } = [];
}
