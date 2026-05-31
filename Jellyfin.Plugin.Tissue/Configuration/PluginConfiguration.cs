using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Tissue.Configuration;

/// <summary>
/// Plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the Tissue service base URL.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the API key used to call Tissue endpoints.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets HTTP timeout in seconds.
    /// </summary>
    public int RequestTimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// Gets or sets a value indicating whether higher resolution images are preferred.
    /// </summary>
    public bool PreferHighResolution { get; set; }

    /// <summary>
    /// Gets or sets allowed library names for actor image requests.
    /// </summary>
    [SuppressMessage("Design", "CA1002:Do not expose generic lists", Justification = "XmlSerializer requires concrete writable collection type.")]
    [SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Jellyfin plugin configuration requires writable collection property for XML serialization.")]
    public List<string> AllowedLibraryNames { get; set; } = [];
}
