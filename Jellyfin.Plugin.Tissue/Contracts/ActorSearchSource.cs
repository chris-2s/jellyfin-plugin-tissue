using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Tissue.Contracts;

/// <summary>
/// Actor source metadata.
/// </summary>
public sealed class ActorSearchSource
{
    /// <summary>
    /// Gets or sets source site id.
    /// </summary>
    [JsonPropertyName("site_id")]
    public int? SiteId { get; set; }

    /// <summary>
    /// Gets or sets source spider key.
    /// </summary>
    [JsonPropertyName("spider_key")]
    public string? SpiderKey { get; set; }

    /// <summary>
    /// Gets or sets source site name.
    /// </summary>
    [JsonPropertyName("site_name")]
    public string? SiteName { get; set; }
}
