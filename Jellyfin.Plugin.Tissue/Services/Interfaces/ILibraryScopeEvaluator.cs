using Jellyfin.Plugin.Tissue.Configuration;
using MediaBrowser.Controller.Entities;

namespace Jellyfin.Plugin.Tissue.Services;

/// <summary>
/// Evaluates whether items or people belong to plugin-enabled libraries.
/// </summary>
public interface ILibraryScopeEvaluator
{
    /// <summary>
    /// Determines whether the specified item belongs to an allowed library.
    /// </summary>
    /// <param name="item">The item to evaluate.</param>
    /// <param name="config">The current plugin configuration.</param>
    /// <returns><c>true</c> if the item belongs to an allowed library; otherwise, <c>false</c>.</returns>
    bool IsItemAllowed(BaseItem item, PluginConfiguration config);

    /// <summary>
    /// Determines whether the specified person is associated with an allowed library.
    /// </summary>
    /// <param name="person">The person to evaluate.</param>
    /// <param name="config">The current plugin configuration.</param>
    /// <returns><c>true</c> if the person is associated with an allowed library; otherwise, <c>false</c>.</returns>
    bool IsPersonAllowed(Person person, PluginConfiguration config);
}
