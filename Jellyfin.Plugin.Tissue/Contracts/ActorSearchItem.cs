using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Tissue.Contracts;

/// <summary>
/// Actor search item.
/// </summary>
public sealed class ActorSearchItem
{
    /// <summary>
    /// Gets or sets actor avatar URL.
    /// </summary>
    [JsonPropertyName("thumb")]
    public string? Thumb { get; set; }

    /// <summary>
    /// Gets or sets thumbnail metadata.
    /// </summary>
    [JsonPropertyName("thumb_info")]
    public ActorThumbInfo? ThumbInfo { get; set; }

    /// <summary>
    /// Gets or sets source info.
    /// </summary>
    [JsonPropertyName("source")]
    public ActorSearchSource? Source { get; set; }
}
