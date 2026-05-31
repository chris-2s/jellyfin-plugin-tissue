using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Tissue.Services;

/// <summary>
/// Short-lived actor image resolve cache with single-flight request coordination.
/// </summary>
public sealed class ActorImageResolveCache : IActorImageResolveCache
{
    private static readonly TimeSpan SuccessCacheTtl = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan EmptyCacheTtl = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan FailureCacheTtl = TimeSpan.FromSeconds(10);
    private readonly ConcurrentDictionary<string, CachedActorImages> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Lazy<Task<CachedActorImages>>> _inFlight = new(StringComparer.OrdinalIgnoreCase);
    private readonly ITissueClient _tissueClient;
    private readonly ILogger<ActorImageResolveCache> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _successCacheTtl;
    private readonly TimeSpan _emptyCacheTtl;
    private readonly TimeSpan _failureCacheTtl;

    /// <summary>
    /// Initializes a new instance of the <see cref="ActorImageResolveCache"/> class.
    /// </summary>
    /// <param name="tissueClient">Tissue API client.</param>
    /// <param name="logger">Logger instance.</param>
    public ActorImageResolveCache(ITissueClient tissueClient, ILogger<ActorImageResolveCache> logger)
        : this(tissueClient, logger, TimeProvider.System, SuccessCacheTtl, EmptyCacheTtl, FailureCacheTtl)
    {
    }

    internal ActorImageResolveCache(
        ITissueClient tissueClient,
        ILogger<ActorImageResolveCache> logger,
        TimeProvider timeProvider,
        TimeSpan successCacheTtl,
        TimeSpan emptyCacheTtl,
        TimeSpan failureCacheTtl)
    {
        _tissueClient = tissueClient;
        _logger = logger;
        _timeProvider = timeProvider;
        _successCacheTtl = successCacheTtl;
        _emptyCacheTtl = emptyCacheTtl;
        _failureCacheTtl = failureCacheTtl;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ResolvedActorImage>> ResolveActorImagesAsync(string actorName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(actorName))
        {
            return [];
        }

        var normalizedActorName = NormalizeActorName(actorName);
        var now = _timeProvider.GetUtcNow();
        if (TryGetCachedImages(normalizedActorName, now, out var cachedImages))
        {
            _logger.LogDebug(
                "演员 {ActorName} 命中 Tissue Resolve 短缓存，图片数量 {ImageCount}。",
                actorName,
                cachedImages.Count);
            return cachedImages;
        }

        var lazyResolve = _inFlight.GetOrAdd(
            normalizedActorName,
            _ => new Lazy<Task<CachedActorImages>>(
                () => ResolveAndCacheAsync(normalizedActorName),
                LazyThreadSafetyMode.ExecutionAndPublication));

        if (lazyResolve.IsValueCreated)
        {
            _logger.LogDebug("演员 {ActorName} 正在 Resolve，复用已有请求。", actorName);
        }

        var cachedResult = await lazyResolve.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        return cachedResult.Images;
    }

    private static string NormalizeActorName(string actorName)
    {
        return actorName.Trim();
    }

    private bool TryGetCachedImages(string normalizedActorName, DateTimeOffset now, out IReadOnlyList<ResolvedActorImage> images)
    {
        if (_cache.TryGetValue(normalizedActorName, out var cached) && cached.ExpiresAt > now)
        {
            images = cached.Images;
            return true;
        }

        images = [];
        if (cached is not null)
        {
            _cache.TryRemove(normalizedActorName, out _);
        }

        return false;
    }

    private async Task<CachedActorImages> ResolveAndCacheAsync(string normalizedActorName)
    {
        try
        {
            CachedActorImages cached;
            try
            {
                var images = await _tissueClient.ResolveActorImagesAsync(normalizedActorName, CancellationToken.None).ConfigureAwait(false);
                var imageArray = images.ToArray();
                var ttl = imageArray.Length == 0 ? _emptyCacheTtl : _successCacheTtl;
                cached = new CachedActorImages(imageArray, _timeProvider.GetUtcNow().Add(ttl));
                _logger.LogDebug(
                    "演员 {ActorName} Tissue Resolve 结果已缓存 {CacheSeconds} 秒，图片数量 {ImageCount}。",
                    normalizedActorName,
                    ttl.TotalSeconds,
                    imageArray.Length);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "演员 {ActorName} Tissue Resolve 异常，写入短暂负缓存。", normalizedActorName);
                cached = new CachedActorImages([], _timeProvider.GetUtcNow().Add(_failureCacheTtl));
            }

            _cache[normalizedActorName] = cached;
            return cached;
        }
        finally
        {
            _inFlight.TryRemove(normalizedActorName, out _);
        }
    }

    private sealed class CachedActorImages
    {
        public CachedActorImages(IReadOnlyList<ResolvedActorImage> images, DateTimeOffset expiresAt)
        {
            Images = images;
            ExpiresAt = expiresAt;
        }

        public IReadOnlyList<ResolvedActorImage> Images { get; }

        public DateTimeOffset ExpiresAt { get; }
    }
}
