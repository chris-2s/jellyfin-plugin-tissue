using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Tissue.Configuration;
using Jellyfin.Plugin.Tissue.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Tissue.Tests;

public sealed class LibraryScopeEvaluatorTests
{
    [Fact]
    public void IsItemAllowed_ReturnsTrueWhenPathMatchesConfiguredLibrary()
    {
        var libraryManager = TestDispatchProxy<ILibraryManager>.Create((method, _) =>
        {
            return method.Name switch
            {
                nameof(ILibraryManager.GetVirtualFolders) => new List<VirtualFolderInfo>
                {
                    new()
                    {
                        Name = "Movies",
                        Locations = ["/media/movies"]
                    }
                },
                _ => throw new NotSupportedException(method.Name)
            };
        });

        var evaluator = new LibraryScopeEvaluator(libraryManager, NullLogger<LibraryScopeEvaluator>.Instance);
        var item = new Movie
        {
            Name = "Movie",
            Path = "/media/movies/example/movie.mkv"
        };

        var isAllowed = evaluator.IsItemAllowed(item, new PluginConfiguration
        {
            AllowedLibraryNames = ["Movies"]
        });

        Assert.True(isAllowed);
    }

    [Fact]
    public void IsPersonAllowed_ReturnsTrueWhenRelatedItemMatchesConfiguredLibrary()
    {
        var personId = Guid.NewGuid();
        var libraryManager = TestDispatchProxy<ILibraryManager>.Create((method, args) =>
        {
            return method.Name switch
            {
                nameof(ILibraryManager.GetVirtualFolders) => new List<VirtualFolderInfo>
                {
                    new()
                    {
                        Name = "TV",
                        Locations = ["/media/tv"]
                    }
                },
                nameof(ILibraryManager.GetItemList) => new List<BaseItem>
                {
                    new Episode
                    {
                        Name = "Episode",
                        Path = "/media/tv/show/season 1/episode.mkv"
                    }
                },
                _ => throw new NotSupportedException(method.Name)
            };
        });

        var evaluator = new LibraryScopeEvaluator(libraryManager, NullLogger<LibraryScopeEvaluator>.Instance);
        var isAllowed = evaluator.IsPersonAllowed(new Person
        {
            Id = personId,
            Name = "Actor"
        }, new PluginConfiguration
        {
            AllowedLibraryNames = ["TV"]
        });

        Assert.True(isAllowed);
    }
}
