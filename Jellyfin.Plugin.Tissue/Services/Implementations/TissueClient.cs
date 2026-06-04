using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Tissue.Contracts;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Tissue.Services;

/// <summary>
/// Tissue API client.
/// </summary>
public sealed class TissueClient : ITissueClient
{
    private const string HttpClientName = "Tissue";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ILogger<TissueClient> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="TissueClient"/> class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    /// <param name="httpClientFactory">HTTP client factory.</param>
    public TissueClient(ILogger<TissueClient> logger, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ResolvedActorImage>> ResolveActorImagesAsync(string actorName, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration;
        if (config is null || string.IsNullOrWhiteSpace(config.BaseUrl))
        {
            _logger.LogWarning("Tissue BaseUrl 为空，跳过演员 {ActorName} 的 Resolve 请求。", actorName);
            return [];
        }

        using var httpClient = CreateClient(config);
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildActorSearchRelativeUrl(actorName));

        try
        {
            var resolveStopwatch = Stopwatch.StartNew();
            using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            resolveStopwatch.Stop();
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.LogInformation("演员 {ActorName} 未命中图片。状态码={StatusCode}，耗时={ElapsedMs}ms。", actorName, response.StatusCode, resolveStopwatch.ElapsedMilliseconds);
                return [];
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("演员 {ActorName} 的 Resolve 请求失败。状态码={StatusCode}，耗时={ElapsedMs}ms。", actorName, response.StatusCode, resolveStopwatch.ElapsedMilliseconds);
                return [];
            }

            var result = await response.Content.ReadFromJsonAsync<ActorSearchResponse>(JsonOptions, cancellationToken).ConfigureAwait(false);
            if (result is null)
            {
                _logger.LogInformation("演员 {ActorName} 的查询响应体为空。", actorName);
                return [];
            }

            if (!result.Success)
            {
                _logger.LogInformation("演员 {ActorName} 的查询结果 success=false。Details={Details}", actorName, result.Details);
                return [];
            }

            var preferHighResolution = config.PreferHighResolution;
            var images = ReorderByResolution(result.Data, preferHighResolution);

            if (images.Length == 0)
            {
                _logger.LogInformation("演员 {ActorName} 的 Resolve 未返回可用图片地址。", actorName);
                return [];
            }

            _logger.LogInformation(
                "演员 {ActorName} 查询成功，返回 {ImageCount} 个图片地址。高分辨率优先={PreferHighResolution}。状态码={StatusCode}，耗时={ElapsedMs}ms。",
                actorName,
                images.Length,
                preferHighResolution,
                response.StatusCode,
                resolveStopwatch.ElapsedMilliseconds);
            return images;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("演员 {ActorName} 的查询请求被取消或超时。", actorName);
            return [];
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "演员 {ActorName} 的查询请求异常。", actorName);
            return [];
        }
    }

    /// <inheritdoc />
    public async Task<HttpResponseMessage?> GetImageResponseAsync(string proxyUrl, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration;
        if (config is null || string.IsNullOrWhiteSpace(config.BaseUrl))
        {
            _logger.LogWarning("Tissue BaseUrl 为空，跳过图片内容请求。");
            return null;
        }

        using var httpClient = CreateClient(config);
        using var request = new HttpRequestMessage(HttpMethod.Get, proxyUrl);

        try
        {
            var contentStopwatch = Stopwatch.StartNew();
            var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            contentStopwatch.Stop();
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("图片内容请求失败。状态码={StatusCode}，耗时={ElapsedMs}ms。", response.StatusCode, contentStopwatch.ElapsedMilliseconds);
                response.Dispose();
                return null;
            }

            if (response.Content.Headers.ContentLength == 0)
            {
                _logger.LogWarning("图片内容请求返回空内容。状态码={StatusCode}，耗时={ElapsedMs}ms。", response.StatusCode, contentStopwatch.ElapsedMilliseconds);
                response.Dispose();
                return null;
            }

            _logger.LogDebug("图片内容请求成功。状态码={StatusCode}，耗时={ElapsedMs}ms。", response.StatusCode, contentStopwatch.ElapsedMilliseconds);

            return response;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("图片内容请求被取消或超时。");
            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "图片内容请求异常。");
            return null;
        }
    }

    private HttpClient CreateClient(Configuration.PluginConfiguration config)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        var timeoutSeconds = config.RequestTimeoutSeconds < 1 ? 1 : config.RequestTimeoutSeconds;
        client.BaseAddress = new Uri(config.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Remove("X-API-Key");

        if (!string.IsNullOrWhiteSpace(config.ApiKey))
        {
            client.DefaultRequestHeaders.Add("X-API-Key", config.ApiKey);
        }

        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private static string BuildActorSearchRelativeUrl(string actorName)
    {
        return "actor/?name=" + Uri.EscapeDataString(actorName);
    }

    private static ResolvedActorImage[] ReorderByResolution(IList<ActorSearchItem> items, bool preferHighResolution)
    {
        var ordered = items
            .Where(static item => !string.IsNullOrWhiteSpace(item.Thumb))
            .Select(static item => new ResolvedActorImage
            {
                Url = item.Thumb!,
                Width = item.ThumbInfo?.Width,
                Height = item.ThumbInfo?.Height
            })
            .ToList();

        if (preferHighResolution)
        {
            ordered = ordered
                .Select((image, index) => new
                {
                    Image = image,
                    Index = index,
                    Area = (long)(image.Width ?? 0) * (long)(image.Height ?? 0),
                    Width = image.Width ?? 0,
                    Height = image.Height ?? 0
                })
                .OrderByDescending(x => x.Area)
                .ThenByDescending(x => x.Width)
                .ThenByDescending(x => x.Height)
                .ThenBy(x => x.Index)
                .Select(x => x.Image)
                .ToList();
        }

        var deduped = new List<ResolvedActorImage>(ordered.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var image in ordered)
        {
            if (seen.Add(image.Url))
            {
                deduped.Add(image);
            }
        }

        return deduped.ToArray();
    }
}
