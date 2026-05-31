namespace Jellyfin.Plugin.Tissue.Services;

/// <summary>
/// Resolved actor image candidate from Tissue.
/// </summary>
public sealed class ResolvedActorImage
{
    /// <summary>
    /// Gets or sets image URL.
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets image width.
    /// </summary>
    public int? Width { get; set; }

    /// <summary>
    /// Gets or sets image height.
    /// </summary>
    public int? Height { get; set; }
}
