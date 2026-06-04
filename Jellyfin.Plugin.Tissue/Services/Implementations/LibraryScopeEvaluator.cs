using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.Tissue.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Tissue.Services;

/// <summary>
/// Resolves whether items and people belong to configured Jellyfin libraries.
/// </summary>
public sealed class LibraryScopeEvaluator : ILibraryScopeEvaluator
{
    private static readonly TimeSpan LibraryGateCacheTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan LibraryMappingsCacheTtl = TimeSpan.FromMinutes(5);
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<LibraryScopeEvaluator> _logger;
    private readonly ConcurrentDictionary<Guid, CachedLibraryGateResult> _personGateCache = new();
    private readonly object _libraryMappingsCacheLock = new();
    private List<(string LibraryName, string RootPath)>? _cachedLibraryMappings;
    private DateTimeOffset _cachedLibraryMappingsExpiresAt = DateTimeOffset.MinValue;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryScopeEvaluator"/> class.
    /// </summary>
    /// <param name="libraryManager">The Jellyfin library manager.</param>
    /// <param name="logger">The logger.</param>
    public LibraryScopeEvaluator(ILibraryManager libraryManager, ILogger<LibraryScopeEvaluator> logger)
    {
        _libraryManager = libraryManager;
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsItemAllowed(BaseItem item, PluginConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(config);

        if (!TryBuildAllowedLibrarySet(config, out var allowedLibraryNames))
        {
            return false;
        }

        try
        {
            var libraryName = ResolveLibraryNameByItem(item, GetOrBuildLibraryPathMappings());
            return !string.IsNullOrWhiteSpace(libraryName) && allowedLibraryNames.Contains(libraryName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "计算条目 {ItemName} 的媒体库门控失败，已跳过。", item.Name);
            return false;
        }
    }

    /// <inheritdoc />
    public bool IsPersonAllowed(Person person, PluginConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(person);
        ArgumentNullException.ThrowIfNull(config);

        if (_personGateCache.TryGetValue(person.Id, out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow)
        {
            return cached.IsAllowed;
        }

        if (!TryBuildAllowedLibrarySet(config, out var allowedLibraryNames))
        {
            _personGateCache[person.Id] = new CachedLibraryGateResult(false, DateTimeOffset.UtcNow.Add(LibraryGateCacheTtl));
            return false;
        }

        try
        {
            var libraryPathMappings = GetOrBuildLibraryPathMappings();
            var matchedLibraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var relatedItem in GetRelatedItemsForPerson(person.Id))
            {
                var matchedLibraryName = ResolveLibraryNameByItem(relatedItem, libraryPathMappings);
                if (!string.IsNullOrWhiteSpace(matchedLibraryName))
                {
                    matchedLibraries.Add(matchedLibraryName);
                }
            }

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(
                    "演员 {PersonName} 关联的顶级媒体库：{Libraries}。",
                    person.Name,
                    string.Join(", ", matchedLibraries));
            }

            var isAllowed = matchedLibraries.Any(allowedLibraryNames.Contains);
            if (!isAllowed)
            {
                _logger.LogDebug(
                    "演员 {PersonName} 未命中允许生效的媒体库，跳过。命中库数量：{MatchedLibraryCount}。",
                    person.Name,
                    matchedLibraries.Count);
            }

            _personGateCache[person.Id] = new CachedLibraryGateResult(isAllowed, DateTimeOffset.UtcNow.Add(LibraryGateCacheTtl));
            return isAllowed;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "计算演员 {PersonName} 的媒体库门控失败，已跳过。", person.Name);
            _personGateCache[person.Id] = new CachedLibraryGateResult(false, DateTimeOffset.UtcNow.Add(TimeSpan.FromMinutes(1)));
            return false;
        }
    }

    private bool TryBuildAllowedLibrarySet(PluginConfiguration config, out HashSet<string> allowedLibraryNames)
    {
        var configuredLibraryNames = config.AllowedLibraryNames ?? [];
        if (configuredLibraryNames.Count == 0)
        {
            _logger.LogDebug("未配置允许生效的媒体库，跳过 Tissue 自动化和远程请求。");
            allowedLibraryNames = [];
            return false;
        }

        allowedLibraryNames = new HashSet<string>(
            configuredLibraryNames.Where(static n => !string.IsNullOrWhiteSpace(n)),
            StringComparer.OrdinalIgnoreCase);
        if (allowedLibraryNames.Count == 0)
        {
            _logger.LogDebug("允许生效的媒体库配置为空值，跳过 Tissue 自动化和远程请求。");
            return false;
        }

        return true;
    }

    private IReadOnlyList<BaseItem> GetRelatedItemsForPerson(Guid personId)
    {
        var query = new InternalItemsQuery
        {
            PersonIds = [personId],
            Recursive = true
        };

        return _libraryManager.GetItemList(query);
    }

    private List<(string LibraryName, string RootPath)> GetOrBuildLibraryPathMappings()
    {
        var now = DateTimeOffset.UtcNow;
        if (_cachedLibraryMappings is not null && _cachedLibraryMappingsExpiresAt > now)
        {
            return _cachedLibraryMappings;
        }

        lock (_libraryMappingsCacheLock)
        {
            if (_cachedLibraryMappings is not null && _cachedLibraryMappingsExpiresAt > now)
            {
                return _cachedLibraryMappings;
            }

            var mappings = _libraryManager.GetVirtualFolders()
                .Where(static folder => !string.IsNullOrWhiteSpace(folder.Name))
                .SelectMany(folder => (folder.Locations ?? Array.Empty<string>())
                    .Where(static location => !string.IsNullOrWhiteSpace(location))
                    .Select(location => (LibraryName: folder.Name, RootPath: NormalizePath(location))))
                .Where(static x => !string.IsNullOrWhiteSpace(x.RootPath))
                .OrderByDescending(static x => x.RootPath.Length)
                .ToList();

            _cachedLibraryMappings = mappings;
            _cachedLibraryMappingsExpiresAt = now.Add(LibraryMappingsCacheTtl);
            return mappings;
        }
    }

    private static string? ResolveLibraryNameByItem(BaseItem baseItem, List<(string LibraryName, string RootPath)> mappings)
    {
        var path = NormalizePath(baseItem.Path);
        if (string.IsNullOrWhiteSpace(path))
        {
            path = NormalizePath(baseItem.GetTopParent()?.Path);
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return baseItem.GetTopParent()?.Name;
        }

        foreach (var mapping in mappings)
        {
            if (IsPathInRoot(path, mapping.RootPath))
            {
                return mapping.LibraryName;
            }
        }

        return baseItem.GetTopParent()?.Name;
    }

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        return Path.TrimEndingDirectorySeparator(path.Trim());
    }

    private static bool IsPathInRoot(string path, string rootPath)
    {
        if (string.Equals(path, rootPath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var rootWithSeparator = rootPath + Path.DirectorySeparatorChar;
        var altRootWithSeparator = rootPath + Path.AltDirectorySeparatorChar;
        return path.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(altRootWithSeparator, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record CachedLibraryGateResult(bool IsAllowed, DateTimeOffset ExpiresAt);
}
