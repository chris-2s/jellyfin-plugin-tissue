using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
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
    private readonly IActorImageResolveCache _actorImageResolveCache;
    private readonly ITissueClient _tissueClient;
    private readonly ILibraryScopeEvaluator _libraryScopeEvaluator;
    private readonly ILogger<ActorImageProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ActorImageProvider"/> class.
    /// </summary>
    /// <param name="actorImageResolveCache">Actor image resolve cache.</param>
    /// <param name="tissueClient">Tissue API client.</param>
    /// <param name="libraryScopeEvaluator">Library scope evaluator instance.</param>
    /// <param name="logger">Logger instance.</param>
    public ActorImageProvider(
        IActorImageResolveCache actorImageResolveCache,
        ITissueClient tissueClient,
        ILibraryScopeEvaluator libraryScopeEvaluator,
        ILogger<ActorImageProvider> logger)
    {
        _actorImageResolveCache = actorImageResolveCache;
        _tissueClient = tissueClient;
        _libraryScopeEvaluator = libraryScopeEvaluator;
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

        if (!_libraryScopeEvaluator.IsPersonAllowed(person, config))
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
                ProxyUrl = _tissueClient.BuildProxyImageUrl(image.Url),
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
        try
        {
            var response = await _tissueClient.GetImageResponseAsync(url, cancellationToken).ConfigureAwait(false);
            if (response is null)
            {
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
}
