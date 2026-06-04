using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Tissue.Configuration;
using Jellyfin.Plugin.Tissue.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Tissue.Tasks;

/// <summary>
/// Manually backfills missing actor images for configured libraries.
/// </summary>
public sealed class MissingActorImageBackfillTask : IScheduledTask, IConfigurableScheduledTask
{
    private readonly ILibraryManager _libraryManager;
    private readonly IActorImageResolveCache _actorImageResolveCache;
    private readonly IProviderManager _providerManager;
    private readonly ILogger<MissingActorImageBackfillTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="MissingActorImageBackfillTask"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="actorImageResolveCache">The actor image resolve cache.</param>
    /// <param name="providerManager">The provider manager.</param>
    /// <param name="logger">The logger.</param>
    public MissingActorImageBackfillTask(
        ILibraryManager libraryManager,
        IActorImageResolveCache actorImageResolveCache,
        IProviderManager providerManager,
        ILogger<MissingActorImageBackfillTask> logger)
    {
        _libraryManager = libraryManager;
        _actorImageResolveCache = actorImageResolveCache;
        _providerManager = providerManager;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Tissue Missing Actor Image Backfill";

    /// <inheritdoc />
    public string Key => "TissueMissingActorImageBackfill";

    /// <inheritdoc />
    public string Description => "为已配置媒体库中的缺失演员头像执行 Tissue 补全。";

    /// <inheritdoc />
    public string Category => "Tissue";

    /// <inheritdoc />
    public bool IsHidden => false;

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <inheritdoc />
    public bool IsLogged => true;

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        if (!HasUsableTissueConfig(config))
        {
            _logger.LogDebug("Tissue 服务配置不完整，跳过计划任务执行。");
            progress.Report(100);
            return;
        }

        var items = GetCandidateItems(config);
        if (items.Count == 0)
        {
            progress.Report(100);
            return;
        }

        var processedPeople = new HashSet<Guid>();
        for (var index = 0; index < items.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var item = items[index];
            try
            {
                await FillItemActorsAsync(item, processedPeople, config, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "计划任务处理条目 {ItemName} 的演员头像补全失败。", item.Name);
            }

            progress.Report(((index + 1D) / items.Count) * 100D);
        }
    }

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return Array.Empty<TaskTriggerInfo>();
    }

    private List<BaseItem> GetCandidateItems(PluginConfiguration config)
    {
        var allowedLibraryIds = GetAllowedLibraryIds(config);
        if (allowedLibraryIds.Count == 0)
        {
            return [];
        }

        return _libraryManager.GetItemList(new InternalItemsQuery
            {
                AncestorIds = allowedLibraryIds.ToArray(),
                Recursive = true,
                IncludeItemTypes = [BaseItemKind.Movie, BaseItemKind.Series, BaseItemKind.Episode]
            })
            .Where(static item => item.SupportsPeople)
            .ToList();
    }

    private List<Guid> GetAllowedLibraryIds(PluginConfiguration config)
    {
        var allowedLibraryNames = new HashSet<string>(
            (config.AllowedLibraryNames ?? []).Where(static name => !string.IsNullOrWhiteSpace(name)),
            StringComparer.OrdinalIgnoreCase);
        if (allowedLibraryNames.Count == 0)
        {
            return [];
        }

        return _libraryManager.GetVirtualFolders()
            .Where(folder => !string.IsNullOrWhiteSpace(folder.Name) && allowedLibraryNames.Contains(folder.Name))
            .Select(static folder => Guid.TryParse(folder.ItemId, out var id) ? id : Guid.Empty)
            .Where(static id => id != Guid.Empty)
            .Distinct()
            .ToList();
    }

    private async Task FillItemActorsAsync(
        BaseItem item,
        HashSet<Guid> processedPeople,
        PluginConfiguration config,
        CancellationToken cancellationToken)
    {
        foreach (var personInfo in _libraryManager.GetPeople(item))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (personInfo.Type != PersonKind.Actor)
            {
                continue;
            }

            var person = ResolvePerson(personInfo);
            if (person is null)
            {
                _logger.LogDebug(
                    "计划任务处理条目 {ItemName} 时，演员 {PersonName} 当前无法解析为 Person 实体，已跳过。",
                    item.Name,
                    personInfo.Name);
                continue;
            }

            if (person.Id != Guid.Empty && !processedPeople.Add(person.Id))
            {
                continue;
            }

            if (!ShouldAttemptPrimaryImageFill(person))
            {
                continue;
            }

            try
            {
                await TryFillPrimaryImageAsync(person, config, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "计划任务补全演员 {PersonName} 头像失败。", person.Name);
            }
        }
    }

    private Person? ResolvePerson(PersonInfo personInfo)
    {
        var person = personInfo.Id != Guid.Empty
            ? _libraryManager.GetItemById<Person>(personInfo.Id)
            : null;

        if (person is not null)
        {
            return person;
        }

        if (!string.IsNullOrWhiteSpace(personInfo.Name))
        {
            person = _libraryManager.GetPerson(personInfo.Name);
        }

        return person;
    }

    private async Task<bool> TryFillPrimaryImageAsync(
        Person person,
        PluginConfiguration config,
        CancellationToken cancellationToken)
    {
        if (!ShouldAttemptPrimaryImageFill(person))
        {
            return false;
        }

        var images = await _actorImageResolveCache.ResolveActorImagesAsync(person.Name, cancellationToken).ConfigureAwait(false);
        var chosenImage = images.FirstOrDefault(static image => !string.IsNullOrWhiteSpace(image.Url));
        if (chosenImage is null)
        {
            _logger.LogDebug("演员 {PersonName} 未命中可用的 Tissue 头像。", person.Name);
            return false;
        }

        await _providerManager.SaveImage(person, chosenImage.Url, ImageType.Primary, null, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("已为演员 {PersonName} 保存 Tissue 头像。", person.Name);
        return true;
    }

    private static bool HasUsableTissueConfig(PluginConfiguration config)
    {
        return !string.IsNullOrWhiteSpace(config.BaseUrl)
            && !string.IsNullOrWhiteSpace(config.ApiKey);
    }

    private bool ShouldAttemptPrimaryImageFill(Person person)
    {
        if (HasPrimaryImage(person))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(person.Name))
        {
            return true;
        }

        _logger.LogDebug("演员 {PersonId} 名称为空，跳过头像补全。", person.Id);
        return false;
    }

    private static bool HasPrimaryImage(Person person)
    {
        return person.ImageInfos.Any(static imageInfo => imageInfo.Type == ImageType.Primary);
    }
}
