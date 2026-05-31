using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Tissue.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Tissue.Tests;

public sealed class ActorImageResolveCacheTests
{
    [Fact]
    public async Task ConcurrentRequestsShareSingleResolve()
    {
        var client = new FakeTissueClient();
        var releaseResolve = new TaskCompletionSource<IReadOnlyList<ResolvedActorImage>>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Resolve = (_, _) => releaseResolve.Task;
        var cache = CreateCache(client);

        var requests = Enumerable.Range(0, 10)
            .Select(_ => cache.ResolveActorImagesAsync("Actor", CancellationToken.None))
            .ToArray();

        await WaitForCallCountAsync(client, 1);
        releaseResolve.SetResult([new ResolvedActorImage { Url = "https://example.test/a.jpg" }]);

        var results = await Task.WhenAll(requests);

        Assert.Equal(1, client.ResolveCallCount);
        Assert.All(results, result => Assert.Equal("https://example.test/a.jpg", Assert.Single(result).Url));
    }

    [Fact]
    public async Task SuccessfulResolveIsCachedWithinTtl()
    {
        var client = new FakeTissueClient
        {
            Resolve = (_, _) => Task.FromResult<IReadOnlyList<ResolvedActorImage>>(
                [new ResolvedActorImage { Url = "https://example.test/a.jpg" }])
        };
        var cache = CreateCache(client);

        await cache.ResolveActorImagesAsync(" Actor ", CancellationToken.None);
        var secondResult = await cache.ResolveActorImagesAsync("actor", CancellationToken.None);

        Assert.Equal(1, client.ResolveCallCount);
        Assert.Equal("https://example.test/a.jpg", Assert.Single(secondResult).Url);
    }

    [Fact]
    public async Task EmptyResolveUsesShortCacheAndExpires()
    {
        var client = new FakeTissueClient
        {
            Resolve = (_, _) => Task.FromResult<IReadOnlyList<ResolvedActorImage>>([])
        };
        var timeProvider = new ManualTimeProvider();
        var cache = CreateCache(client, timeProvider);

        await cache.ResolveActorImagesAsync("Actor", CancellationToken.None);
        await cache.ResolveActorImagesAsync("Actor", CancellationToken.None);
        timeProvider.Advance(TimeSpan.FromSeconds(11));
        await cache.ResolveActorImagesAsync("Actor", CancellationToken.None);

        Assert.Equal(2, client.ResolveCallCount);
    }

    [Fact]
    public async Task FailureResolveUsesShortNegativeCacheAndExpires()
    {
        var client = new FakeTissueClient();
        client.Resolve = (_, _) => client.ResolveCallCount == 1
            ? Task.FromException<IReadOnlyList<ResolvedActorImage>>(new HttpRequestException("boom"))
            : Task.FromResult<IReadOnlyList<ResolvedActorImage>>([new ResolvedActorImage { Url = "https://example.test/a.jpg" }]);
        var timeProvider = new ManualTimeProvider();
        var cache = CreateCache(client, timeProvider);

        var firstResult = await cache.ResolveActorImagesAsync("Actor", CancellationToken.None);
        var secondResult = await cache.ResolveActorImagesAsync("Actor", CancellationToken.None);
        timeProvider.Advance(TimeSpan.FromSeconds(11));
        var thirdResult = await cache.ResolveActorImagesAsync("Actor", CancellationToken.None);

        Assert.Empty(firstResult);
        Assert.Empty(secondResult);
        Assert.Equal(2, client.ResolveCallCount);
        Assert.Equal("https://example.test/a.jpg", Assert.Single(thirdResult).Url);
    }

    private static ActorImageResolveCache CreateCache(FakeTissueClient client)
    {
        return CreateCache(client, new ManualTimeProvider());
    }

    private static ActorImageResolveCache CreateCache(FakeTissueClient client, TimeProvider timeProvider)
    {
        return new ActorImageResolveCache(
            client,
            NullLogger<ActorImageResolveCache>.Instance,
            timeProvider,
            TimeSpan.FromSeconds(60),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(10));
    }

    private static async Task WaitForCallCountAsync(FakeTissueClient client, int callCount)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (client.ResolveCallCount < callCount)
        {
            await Task.Delay(10, timeout.Token).ConfigureAwait(false);
        }
    }

    private sealed class FakeTissueClient : ITissueClient
    {
        private int _resolveCallCount;

        public Func<string, CancellationToken, Task<IReadOnlyList<ResolvedActorImage>>> Resolve { get; set; } =
            (_, _) => Task.FromResult<IReadOnlyList<ResolvedActorImage>>([]);

        public int ResolveCallCount => Volatile.Read(ref _resolveCallCount);

        public Task<IReadOnlyList<ResolvedActorImage>> ResolveActorImagesAsync(string actorName, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _resolveCallCount);
            return Resolve(actorName, cancellationToken);
        }

        public Task<HttpResponseMessage?> GetImageResponseAsync(string proxyUrl, CancellationToken cancellationToken)
        {
            return Task.FromResult<HttpResponseMessage?>(null);
        }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow = new(2026, 5, 31, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow()
        {
            return _utcNow;
        }

        public void Advance(TimeSpan timeSpan)
        {
            _utcNow = _utcNow.Add(timeSpan);
        }
    }
}
