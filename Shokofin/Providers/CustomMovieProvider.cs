using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using Shokofin.ExternalIds;
using Shokofin.MergeVersions;

namespace Shokofin.Providers;
#pragma warning disable IDE0059
#pragma warning disable IDE0290

/// <summary>
/// The custom movie provider. Responsible for de-duplicating physical movies.
/// </summary>
/// <remarks>
/// This needs to be it's own class because of internal Jellyfin shenanigans
/// about how a provider cannot also be a custom provider otherwise it won't
/// save the metadata.
/// </remarks>
public class CustomMovieProvider(ILibraryManager _libraryManager, MergeVersionsManager _mergeVersionsManager) : IHasItemChangeMonitor, ICustomMetadataProvider<Movie> {
    public string Name => Plugin.MetadataProviderName;

    public bool HasChanged(BaseItem item, IDirectoryService directoryService) {
        // We're only interested in movies.
        if (item is not Movie movie)
            return false;

        // Abort if we're unable to get the shoko episode id.
        if (!movie.TryGetProviderId(ProviderNames.ShokoEpisode, out var episodeId))
            return false;

        return true;
    }

    public async Task<ItemUpdateType> FetchAsync(Movie movie, MetadataRefreshOptions options, CancellationToken cancellationToken) {
        var itemUpdated = ItemUpdateType.None;
        if (movie.TryGetProviderId(ProviderNames.ShokoEpisode, out var episodeId) && Plugin.Instance.Configuration.AutoMergeVersions && !_libraryManager.IsScanRunning && options.MetadataRefreshMode != MetadataRefreshMode.ValidationOnly) {
            await _mergeVersionsManager.SplitAndMergeMoviesByEpisodeId(episodeId).ConfigureAwait(false);
            itemUpdated |= ItemUpdateType.MetadataEdit;
        }

        return itemUpdated;
    }
}