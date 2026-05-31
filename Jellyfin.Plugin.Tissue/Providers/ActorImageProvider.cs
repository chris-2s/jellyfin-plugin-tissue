using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Tissue.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Tissue.Providers;

/// <summary>
/// Tissue provider for person primary images.
/// </summary>
public sealed class ActorImageProvider : IRemoteImageProvider
{
    private const string ProviderDisplayName = "Tissue";
    private static readonly TimeSpan LibraryGateCacheTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan LibraryMappingsCacheTtl = TimeSpan.FromMinutes(5);
    private readonly IActorImageResolveCache _actorImageResolveCache;
    private readonly ITissueClient _tissueClient;
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<ActorImageProvider> _logger;
    private readonly ConcurrentDictionary<Guid, CachedLibraryGateResult> _libraryGateCache = new();
    private readonly object _libraryMappingsCacheLock = new();
    private List<(string LibraryName, string RootPath)>? _cachedLibraryMappings;
    private DateTimeOffset _cachedLibraryMappingsExpiresAt = DateTimeOffset.MinValue;
    private MethodInfo? _cachedGetItemListMethod;

    /// <summary>
    /// Initializes a new instance of the <see cref="ActorImageProvider"/> class.
    /// </summary>
    /// <param name="actorImageResolveCache">Actor image resolve cache.</param>
    /// <param name="tissueClient">Tissue API client.</param>
    /// <param name="libraryManager">Library manager instance.</param>
    /// <param name="logger">Logger instance.</param>
    public ActorImageProvider(
        IActorImageResolveCache actorImageResolveCache,
        ITissueClient tissueClient,
        ILibraryManager libraryManager,
        ILogger<ActorImageProvider> logger)
    {
        _actorImageResolveCache = actorImageResolveCache;
        _tissueClient = tissueClient;
        _libraryManager = libraryManager;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => ProviderDisplayName;

    /// <inheritdoc />
    public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
    {
        return item is Person ? [ImageType.Primary] : Array.Empty<ImageType>();
    }

    /// <inheritdoc />
    public bool Supports(BaseItem item)
    {
        return item is Person;
    }

    /// <inheritdoc />
    public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
    {
        if (item is not Person person)
        {
            return [];
        }

        var config = Plugin.Instance?.Configuration ?? new Configuration.PluginConfiguration();
        _logger.LogDebug(
            "收到演员头像请求：{PersonName}（{PersonId}）。",
            person.Name,
            person.Id);

        if (!IsPersonAllowedByLibrary(person, config))
        {
            return [];
        }

        var resolveStopwatch = Stopwatch.StartNew();
        _logger.LogDebug(
            "开始请求 Tissue Resolve：{PersonName}。",
            person.Name);

        var resolvedImages = await _actorImageResolveCache.ResolveActorImagesAsync(person.Name, cancellationToken).ConfigureAwait(false);
        resolveStopwatch.Stop();
        if (resolvedImages.Count == 0)
        {
            _logger.LogDebug(
                "Tissue Resolve 未返回图片：{PersonName}，耗时 {ElapsedMs}ms。",
                person.Name,
                resolveStopwatch.ElapsedMilliseconds);
            return [];
        }

        _logger.LogDebug(
            "Tissue Resolve 成功：{PersonName}，图片数量 {ImageCount}，耗时 {ElapsedMs}ms。",
            person.Name,
            resolvedImages.Count,
            resolveStopwatch.ElapsedMilliseconds);

        var candidates = resolvedImages
            .Select(image => new
            {
                ProxyUrl = ToProxyImageUrl(config, image.Url),
                image.Width,
                image.Height
            })
            .Where(static x => !string.IsNullOrWhiteSpace(x.ProxyUrl))
            .ToArray();

        _logger.LogDebug("演员 {PersonName} 已生成 {ProxyUrlCount} 个 Tissue 代理图片地址。", person.Name, candidates.Length);

        return candidates.Select(static item => new RemoteImageInfo
            {
                ProviderName = ProviderDisplayName,
                Type = ImageType.Primary,
                Url = item.ProxyUrl,
                Width = item.Width,
                Height = item.Height
            });
    }

    /// <inheritdoc />
    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
    {
        return GetImageResponseInternalAsync(url, cancellationToken);
    }

    private async Task<HttpResponseMessage> GetImageResponseInternalAsync(string url, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration;
        if (!TryValidateProxyUrl(config, url, out var proxyUri))
        {
            _logger.LogInformation("收到无效或非同源的 Tissue 代理地址，请求已拒绝。");
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        try
        {
            var response = await _tissueClient.GetImageResponseAsync(proxyUri.AbsoluteUri, cancellationToken).ConfigureAwait(false);
            if (response is null || !response.IsSuccessStatusCode || response.Content.Headers.ContentLength == 0)
            {
                response?.Dispose();
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            return response;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Tissue 代理图片请求被取消或超时。");
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Tissue 代理图片请求失败。");
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }

    private static string ToProxyImageUrl(Configuration.PluginConfiguration config, string imageUrl)
    {
        if (string.IsNullOrWhiteSpace(config.BaseUrl))
        {
            return string.Empty;
        }

        var baseAddress = config.BaseUrl.TrimEnd('/') + "/";
        return baseAddress + "common/cover?url=" + Uri.EscapeDataString(imageUrl);
    }

    private static bool TryValidateProxyUrl(Configuration.PluginConfiguration? config, string inputUrl, out Uri proxyUri)
    {
        proxyUri = null!;
        if (config is null ||
            string.IsNullOrWhiteSpace(config.BaseUrl) ||
            string.IsNullOrWhiteSpace(inputUrl) ||
            !Uri.TryCreate(inputUrl, UriKind.Absolute, out var candidateUri) ||
            (candidateUri.Scheme != Uri.UriSchemeHttp && candidateUri.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        if (!Uri.TryCreate(config.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var baseUri))
        {
            return false;
        }

        var basePrefix = baseUri.AbsoluteUri;
        if (!candidateUri.AbsoluteUri.StartsWith(basePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var normalizedPath = candidateUri.AbsolutePath.TrimStart('/');
        if (!normalizedPath.StartsWith("common/cover", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        proxyUri = candidateUri;
        return true;
    }

    private bool IsPersonAllowedByLibrary(Person person, Configuration.PluginConfiguration config)
    {
        if (_libraryGateCache.TryGetValue(person.Id, out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow)
        {
            return cached.IsAllowed;
        }

        var configuredLibraryNames = config.AllowedLibraryNames ?? [];
        if (configuredLibraryNames.Count == 0)
        {
            _logger.LogDebug(
                "未配置允许生效的媒体库，跳过演员：{PersonName}。",
                person.Name);
            _libraryGateCache[person.Id] = new CachedLibraryGateResult(false, DateTimeOffset.UtcNow.Add(LibraryGateCacheTtl));
            return false;
        }

        var allowedLibraryNames = new HashSet<string>(configuredLibraryNames.Where(static n => !string.IsNullOrWhiteSpace(n)), StringComparer.OrdinalIgnoreCase);
        if (allowedLibraryNames.Count == 0)
        {
            _logger.LogDebug(
                "允许生效的媒体库配置为空值，跳过演员：{PersonName}。",
                person.Name);
            _libraryGateCache[person.Id] = new CachedLibraryGateResult(false, DateTimeOffset.UtcNow.Add(LibraryGateCacheTtl));
            return false;
        }

        try
        {
            var libraryPathMappings = GetOrBuildLibraryPathMappings();
            var matchedLibraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var relatedItem in GetRelatedItemsForPerson(person.Id))
            {
                if (relatedItem is not BaseItem baseItem)
                {
                    continue;
                }

                var typeName = baseItem.GetType().Name;
                if (!string.Equals(typeName, "Movie", StringComparison.Ordinal) &&
                    !string.Equals(typeName, "Series", StringComparison.Ordinal) &&
                    !string.Equals(typeName, "Episode", StringComparison.Ordinal))
                {
                    continue;
                }

                var matchedLibraryName = ResolveLibraryNameByItem(baseItem, libraryPathMappings);
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

            _libraryGateCache[person.Id] = new CachedLibraryGateResult(isAllowed, DateTimeOffset.UtcNow.Add(LibraryGateCacheTtl));
            return isAllowed;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "计算演员 {PersonName} 的媒体库门控失败，已跳过 Tissue 请求。", person.Name);
            _libraryGateCache[person.Id] = new CachedLibraryGateResult(false, DateTimeOffset.UtcNow.Add(TimeSpan.FromMinutes(1)));
            return false;
        }
    }

    private IEnumerable<object> GetRelatedItemsForPerson(Guid personId)
    {
        // Use reflection for InternalItemsQuery/GetItemList to tolerate Jellyfin API changes across versions.
        var queryType = Type.GetType("MediaBrowser.Controller.Entities.InternalItemsQuery, MediaBrowser.Controller");
        if (queryType is null)
        {
            _logger.LogWarning("未找到 InternalItemsQuery 类型，无法执行媒体库门控。");
            return [];
        }

        var queryInstance = Activator.CreateInstance(queryType);
        if (queryInstance is null)
        {
            _logger.LogWarning("无法创建 InternalItemsQuery 实例，无法执行媒体库门控。");
            return [];
        }

        var personIdsProperty = queryType.GetProperty("PersonIds", BindingFlags.Public | BindingFlags.Instance);
        personIdsProperty?.SetValue(queryInstance, new[] { personId });

        var recursiveProperty = queryType.GetProperty("Recursive", BindingFlags.Public | BindingFlags.Instance);
        recursiveProperty?.SetValue(queryInstance, true);

        _cachedGetItemListMethod ??= ResolveGetItemListMethod(queryType);
        if (_cachedGetItemListMethod is null)
        {
            _logger.LogWarning("未找到兼容的 GetItemList(InternalItemsQuery) 方法，无法执行媒体库门控。");
            return [];
        }

        var result = _cachedGetItemListMethod.Invoke(_libraryManager, [queryInstance]);
        if (result is IEnumerable enumerable)
        {
            return enumerable.Cast<object>();
        }

        return [];
    }

    private MethodInfo? ResolveGetItemListMethod(Type queryType)
    {
        foreach (var method in _libraryManager.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!string.Equals(method.Name, "GetItemList", StringComparison.Ordinal))
            {
                continue;
            }

            var parameters = method.GetParameters();
            if (parameters.Length == 1 && parameters[0].ParameterType.IsAssignableFrom(queryType))
            {
                return method;
            }
        }

        return null;
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
