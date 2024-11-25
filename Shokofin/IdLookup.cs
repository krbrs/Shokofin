using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using Shokofin.API;
using Shokofin.ExternalIds;
using Shokofin.Providers;

namespace Shokofin;

public interface IIdLookup {
    #region Base Item

    /// <summary>
    /// Check if the plugin is enabled for <see cref="BaseItem" >the item</see>.
    /// </summary>
    /// <param name="item">The <see cref="BaseItem" /> to check.</param>
    /// <returns>True if the plugin is enabled for the <see cref="BaseItem" /></returns>
    bool IsEnabledForItem(BaseItem item);

    /// <summary>
    /// Check if the plugin is enabled for <see cref="BaseItem" >the item</see>.
    /// </summary>
    /// <param name="item">The <see cref="BaseItem" /> to check.</param>
    /// <param name="isSoleProvider">True if the plugin is the only metadata provider enabled for the item.</param>
    /// <returns>True if the plugin is enabled for the <see cref="BaseItem" /></returns>
    bool IsEnabledForItem(BaseItem item, out bool isSoleProvider);

    /// <summary>
    /// Check if the plugin is enabled for <see cref="LibraryOptions" >the library options</see>.
    /// </summary>
    /// <param name="libraryOptions">The <see cref="LibraryOptions" /> to check.</param>
    /// <param name="isSoleProvider">True if the plugin is the only metadata provider enabled for the item.</param>
    /// <returns>True if the plugin is enabled for the <see cref="LibraryOptions" /></returns>
    bool IsEnabledForLibraryOptions(LibraryOptions libraryOptions, out bool isSoleProvider);

    #endregion

    #region Season Id

    bool TryGetSeasonIdFor(string path, [NotNullWhen(true)] out string? seasonId);

    bool TryGetSeasonIdFromEpisodeId(string episodeId, [NotNullWhen(true)] out string? seasonId);

    /// <summary>
    /// Try to get the main season id for the <see cref="Series" />.
    /// </summary>
    /// <param name="series">The <see cref="Series" /> to check for.</param>
    /// <param name="seasonId">The variable to put the id in.</param>
    /// <returns>True if it successfully retrieved the id for the <see cref="Series" />.</returns>
    bool TryGetSeasonIdFor(Series series, [NotNullWhen(true)] out string? seasonId);

    /// <summary>
    /// Try to get the season id for the <see cref="Season" />.
    /// </summary>
    /// <param name="season">The <see cref="Season" /> to check for.</param>
    /// <param name="seasonId">The variable to put the id in.</param>
    /// <returns>True if it successfully retrieved the id for the <see cref="Season" />.</returns>
    bool TryGetSeasonIdFor(Season season, [NotNullWhen(true)] out string? seasonId);

    /// <summary>
    /// Try to get the season id for the <see cref="Movie" />.
    /// </summary>
    /// <param name="season">The <see cref="Movie" /> to check for.</param>
    /// <param name="seasonId">The variable to put the id in.</param>
    /// <returns>True if it successfully retrieved the id for the <see cref="Movie" />.</returns>
    bool TryGetSeasonIdFor(Movie movie, [NotNullWhen(true)] out string? seasonId);

    #endregion

    #region Episode Id

    bool TryGetEpisodeIdFor(string path, [NotNullWhen(true)] out string? episodeId);

    bool TryGetEpisodeIdFor(BaseItem item, [NotNullWhen(true)] out string? episodeId);

    bool TryGetEpisodeIdsFor(string path, [NotNullWhen(true)] out List<string>? episodeIds);

    bool TryGetEpisodeIdsFor(BaseItem item, [NotNullWhen(true)] out List<string>? episodeIds);

    #endregion

    #region File Id

    bool TryGetFileIdFor(BaseItem item, [NotNullWhen(true)] out string? fileId, [NotNullWhen(true)] out string? seriesId);

    #endregion
}

public class IdLookup(ShokoApiManager _apiManager, ILibraryManager _libraryManager) : IIdLookup {
    #region Base Item

    private static readonly HashSet<string> AllowedTypes = [nameof(Series), nameof(Season), nameof(Episode), nameof(Movie)];

    public bool IsEnabledForItem(BaseItem item)
        => IsEnabledForItem(item, out var _);

    public bool IsEnabledForItem(BaseItem item, out bool isSoleProvider) {
        var reItem = item switch {
            Series s => s,
            Season s => s.Series,
            Episode e => e.Series,
            _ => item,
        };
        if (reItem == null) {
            isSoleProvider = false;
            return false;
        }

        var libraryOptions = _libraryManager.GetLibraryOptions(reItem);
        if (libraryOptions == null) {
            isSoleProvider = false;
            return false;
        }

        return IsEnabledForLibraryOptions(libraryOptions, out isSoleProvider);
    }

    public bool IsEnabledForLibraryOptions(LibraryOptions libraryOptions, out bool isSoleProvider) {
        var isEnabled = false;
        isSoleProvider = true;
        foreach (var options in libraryOptions.TypeOptions) {
            if (!AllowedTypes.Contains(options.Type))
                continue;
            var isEnabledForType = options.MetadataFetchers.Contains(Plugin.MetadataProviderName);
            if (isEnabledForType) {
                if (!isEnabled)
                    isEnabled = true;
                if (options.MetadataFetchers.Length > 1 && isSoleProvider)
                    isSoleProvider = false;
            }
        }
        return isEnabled;
    }

    #endregion

    #region Season Id

    public bool TryGetSeasonIdFor(string path, [NotNullWhen(true)] out string? seasonId) {
        if (_apiManager.TryGetSeasonIdForPath(path, out seasonId))
            return true;

        seasonId = string.Empty;
        return false;
    }

    public bool TryGetSeasonIdFromEpisodeId(string episodeId, [NotNullWhen(true)] out string? seasonId) {
        if (_apiManager.TryGetSeasonIdForEpisodeId(episodeId, out seasonId))
            return true;

        seasonId = string.Empty;
        return false;
    }

    public bool TryGetSeasonIdFor(Series series, [NotNullWhen(true)] out string? seasonId) {
        if (series.TryGetSeasonId(out seasonId))
            return true;

        if (TryGetSeasonIdFor(series.Path, out seasonId)) {
            if (_apiManager.TryGetShowIdForSeasonId(seasonId, out var mainSeasonId))
                SeriesProvider.AddProviderIds(series, mainSeasonId);
            else
                SeriesProvider.AddProviderIds(series, seasonId);
            // Make sure the presentation unique is not cached, so we won't reuse the cache key.
            series.PresentationUniqueKey = null;
            return true;
        }

        return false;
    }

    public bool TryGetSeasonIdFor(Season season, [NotNullWhen(true)] out string? seasonId) {
        if (season.TryGetSeasonId(out seasonId))
            return true;

        return TryGetSeasonIdFor(season.Path, out seasonId);
    }

    public bool TryGetSeasonIdFor(Movie movie, [NotNullWhen(true)] out string? seasonId) {
        if (movie.TryGetProviderId(ShokoSeriesId.Name, out seasonId))
            return true;

        if (TryGetSeasonIdFor(movie.Path, out var episodeId) && TryGetSeasonIdFromEpisodeId(episodeId, out seasonId))
            return true;

        return false;
    }

    #endregion

    #region Episode Id

    public bool TryGetEpisodeIdFor(string path, [NotNullWhen(true)] out string? episodeId) {
        if (_apiManager.TryGetEpisodeIdForPath(path, out episodeId))
            return true;

        episodeId = string.Empty;
        return false;
    }

    public bool TryGetEpisodeIdFor(BaseItem item, [NotNullWhen(true)] out string? episodeId) {
        // This will account for virtual episodes and existing episodes
        if (item.TryGetProviderId(ShokoEpisodeId.Name, out episodeId)) {
            return true;
        }

        // This will account for new episodes that haven't received their first metadata update yet.
        if (TryGetEpisodeIdFor(item.Path, out episodeId)) {
            return true;
        }

        return false;
    }

    public bool TryGetEpisodeIdsFor(string path, [NotNullWhen(true)] out List<string>? episodeIds) {
        if (_apiManager.TryGetEpisodeIdsForPath(path, out episodeIds))
            return true;

        episodeIds = [];
        return false;
    }

    public bool TryGetEpisodeIdsFor(BaseItem item, [NotNullWhen(true)] out List<string>? episodeIds) {
        // This will account for existing episodes.
        if (item.TryGetProviderId(ShokoFileId.Name, out var fileId) && item.TryGetProviderId(ShokoSeriesId.Name, out var seasonId) && _apiManager.TryGetEpisodeIdsForFileId(fileId, seasonId, out episodeIds))
            return true;

        // This will account for new episodes that haven't received their first metadata update yet.
        if (TryGetEpisodeIdsFor(item.Path, out episodeIds))
            return true;

        // This will account for "missing" episodes.
        if (item.TryGetProviderId(ShokoEpisodeId.Name, out var episodeId)) {
            episodeIds = [episodeId];
            return true;
        }

        return false;
    }

    #endregion

    #region File Id

    public bool TryGetFileIdFor(BaseItem episode, [NotNullWhen(true)] out string? fileId, [NotNullWhen(true)] out string? seriesId) {
        if (episode.TryGetProviderId(ShokoFileId.Name, out fileId) && episode.TryGetProviderId(ShokoSeriesId.Name, out seriesId))
            return true;

        if (_apiManager.TryGetFileIdForPath(episode.Path, out fileId, out seriesId))
            return true;

        fileId = null;
        seriesId = null;
        return false;
    }

    #endregion
}