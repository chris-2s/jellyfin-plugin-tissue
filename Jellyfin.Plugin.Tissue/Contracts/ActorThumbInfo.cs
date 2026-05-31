using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Tissue.Contracts;

/// <summary>
/// Actor thumbnail metadata.
/// </summary>
public sealed class ActorThumbInfo
{
    /// <summary>
    /// Gets or sets thumbnail width.
    /// </summary>
    [JsonPropertyName("width")]
    public int? Width { get; set; }

    /// <summary>
    /// Gets or sets thumbnail height.
    /// </summary>
    [JsonPropertyName("height")]
    public int? Height { get; set; }

    /// <summary>
    /// Gets or sets thumbnail mime type.
    /// </summary>
    [JsonPropertyName("mime")]
    public string? Mime { get; set; }
}
