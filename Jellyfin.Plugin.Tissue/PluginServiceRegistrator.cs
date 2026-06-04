using System;
using Jellyfin.Plugin.Tissue.Services;
using Jellyfin.Plugin.Tissue.Tasks;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Tissue;

/// <summary>
/// Registers plugin services.
/// </summary>
public sealed class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddHttpClient("Tissue");
        serviceCollection.AddSingleton(TimeProvider.System);
        serviceCollection.AddSingleton<ITissueClient, TissueClient>();
        serviceCollection.AddSingleton<IActorImageResolveCache, ActorImageResolveCache>();
        serviceCollection.AddSingleton<ILibraryScopeEvaluator, LibraryScopeEvaluator>();
        serviceCollection.AddSingleton<MissingActorImageBackfillTask>();
    }
}
