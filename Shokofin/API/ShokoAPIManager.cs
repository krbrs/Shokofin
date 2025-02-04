using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Shokofin.API.Info;
using Shokofin.API.Models;
using Shokofin.API.Models.Shoko;
using Shokofin.API.Models.TMDB;
using Shokofin.Configuration;
using Shokofin.ExternalIds;
using Shokofin.Utils;

using ContentRating = Shokofin.Utils.ContentRating;
using Path = System.IO.Path;
using Regex = System.Text.RegularExpressions.Regex;
using RegexOptions = System.Text.RegularExpressions.RegexOptions;
using Shokofin.Extensions;

namespace Shokofin.API;

public partial class ShokoApiManager : IDisposable {
    // Note: This regex will only get uglier with time.
    [System.Text.RegularExpressions.GeneratedRegex(@"\s+\((?<year>\d{4})(?: dai [2-9] bu)?\)\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex YearRegex();

    private readonly ILogger<ShokoApiManager> Logger;

    private readonly ShokoApiClient ApiClient;

    private readonly ILibraryManager LibraryManager;

    private readonly UsageTracker UsageTracker;

    private readonly GuardedMemoryCache DataCache;

    private readonly object MediaFolderListLock = new();

    private readonly List<Folder> MediaFolderList = [];

    private readonly ConcurrentDictionary<string, string> PathToSeasonIdDictionary = new();

    private readonly ConcurrentDictionary<string, List<string>> PathToEpisodeIdsDictionary = new();

    private readonly ConcurrentDictionary<string, (string FileId, string SeriesId)> PathToFileIdAndSeriesIdDictionary = new();

    private readonly ConcurrentDictionary<string, string> SeasonIdToShowIdDictionary = new();

    private readonly ConcurrentDictionary<string, string> EpisodeIdToSeasonIdDictionary = new();

    private readonly ConcurrentDictionary<string, List<string>> FileAndSeasonIdToEpisodeIdDictionary = new();

    public ShokoApiManager(ILogger<ShokoApiManager> logger, ShokoApiClient apiClient, ILibraryManager libraryManager, UsageTracker usageTracker) {
        Logger = logger;
        ApiClient = apiClient;
        LibraryManager = libraryManager;
        UsageTracker = usageTracker;
        DataCache = new(logger, new() { ExpirationScanFrequency = TimeSpan.FromMinutes(25) }, new() { AbsoluteExpirationRelativeToNow = new(2, 30, 0) });
        UsageTracker.Stalled += OnTrackerStalled;
    }

    ~ShokoApiManager() {
        UsageTracker.Stalled -= OnTrackerStalled;
    }

    private void OnTrackerStalled(object? sender, EventArgs eventArgs)
        => Clear();

    #region Ignore rule

    /// <summary>
    /// We'll let the ignore rule "scan" for the media folder, and populate our
    /// dictionary for later use, then we'll use said dictionary to lookup the
    /// media folder by path later in the ignore rule and when stripping the
    /// media folder from the path to get the relative path in
    /// <see cref="StripMediaFolder"/>.
    /// </summary>
    /// <param name="path">The path to find the media folder for.</param>
    /// <param name="parent">The parent folder of <paramref name="path"/>.
    /// </param>
    /// <returns>The media folder and partial string within said folder for
    /// <paramref name="path"/>.</returns>
    public (Folder mediaFolder, string partialPath) FindMediaFolder(string path, Folder parent) {
        Folder? mediaFolder = null;
        lock (MediaFolderListLock)
            mediaFolder = MediaFolderList.FirstOrDefault((folder) => path.StartsWith(folder.Path + Path.DirectorySeparatorChar));
        if (mediaFolder is not null)
            return (mediaFolder, path[mediaFolder.Path.Length..]);
        if (parent.GetTopParent() is not Folder topParent)
            throw new Exception($"Unable to find media folder for path \"{path}\"");
        lock (MediaFolderListLock)
            MediaFolderList.Add(topParent);
        return (topParent, path[topParent.Path.Length..]);
    }

    /// <summary>
    /// Strip the media folder from the full path, leaving only the partial
    /// path to use when searching Shoko for a match.
    /// </summary>
    /// <param name="fullPath">The full path to strip.</param>
    /// <returns>The partial path, void of the media folder.</returns>
    public string StripMediaFolder(string fullPath) {
        Folder? mediaFolder = null;
        lock (MediaFolderListLock)
            mediaFolder = MediaFolderList.FirstOrDefault((folder) => fullPath.StartsWith(folder.Path + Path.DirectorySeparatorChar));
        if (mediaFolder is not null)
            return fullPath[mediaFolder.Path.Length..];
        if (Path.GetDirectoryName(fullPath) is not string directoryPath || LibraryManager.FindByPath(directoryPath, true)?.GetTopParent() is not Folder topParent)
            return fullPath;
        lock (MediaFolderListLock)
            MediaFolderList.Add(topParent);
        return fullPath[topParent.Path.Length..];
    }

    #endregion

    #region Clear

    public void Dispose() {
        GC.SuppressFinalize(this);
        Clear();
    }

    public void Clear() {
        Logger.LogDebug("Clearing data…");
        EpisodeIdToSeasonIdDictionary.Clear();
        FileAndSeasonIdToEpisodeIdDictionary.Clear();
        lock (MediaFolderListLock)
            MediaFolderList.Clear();
        PathToEpisodeIdsDictionary.Clear();
        PathToFileIdAndSeriesIdDictionary.Clear();
        PathToSeasonIdDictionary.Clear();
        SeasonIdToShowIdDictionary.Clear();
        DataCache.Clear();
        Logger.LogDebug("Cleanup complete.");
    }

    #endregion

    #region Series Settings

    private Task<SeriesConfiguration> GetSeriesConfiguration(string id)
        => DataCache.GetOrCreateAsync($"series-settings:{id}", async () => {
            var seriesSettings = new SeriesConfiguration() {
                TypeOverride = null,
                StructureType = Plugin.Instance.Configuration.DefaultLibraryStructure,
                MergeOverride = SeriesMergingOverride.None,
                EpisodesAsSpecials = false,
                SpecialsAsEpisodes = false,
                OrderByAirdate = false,
            };
            var tags = await GetNamespacedTagsForSeries(id).ConfigureAwait(false);
            if (tags.TryGetValue("/custom user tags/series type", out var seriesTypeTag) &&
                seriesTypeTag.Children.Count is > 1 &&
                Enum.TryParse<SeriesType>(NormalizeCustomSeriesType(seriesTypeTag.Children.Keys.First()), out var seriesType) &&
                seriesType is not SeriesType.Unknown
            )
                seriesSettings.TypeOverride = seriesType;

            if (!tags.TryGetValue("/custom user tags/shokofin", out var customTags))
                return seriesSettings;

            tags = customTags.RecursiveNamespacedChildren;
            if (tags.ContainsKey("/anidb structure"))
                seriesSettings.StructureType = SeriesStructureType.AniDB_Anime;
            else if (tags.ContainsKey("/shoko structure"))
                seriesSettings.StructureType = SeriesStructureType.Shoko_Groups;
            else if (tags.ContainsKey("/tmdb structure"))
                seriesSettings.StructureType = SeriesStructureType.TMDB_SeriesAndMovies;

            if (tags.ContainsKey("/no merge"))
                seriesSettings.MergeOverride = SeriesMergingOverride.NoMerge;
            else if (tags.ContainsKey("/merge with main story"))
                seriesSettings.MergeOverride = SeriesMergingOverride.MergeWithMainStory;
            else if (tags.ContainsKey("/merge forward") && tags.ContainsKey("/merge backward"))
                seriesSettings.MergeOverride = SeriesMergingOverride.MergeForward | SeriesMergingOverride.MergeBackward;
            else if (tags.ContainsKey("/merge forward"))
                seriesSettings.MergeOverride = SeriesMergingOverride.MergeForward;
            else if (tags.ContainsKey("/merge backward"))
                seriesSettings.MergeOverride = SeriesMergingOverride.MergeBackward;

            if (tags.ContainsKey("/episodes as specials")) {
                seriesSettings.EpisodesAsSpecials = true;
            }
            else {
                if (tags.ContainsKey("/specials as episodes"))
                    seriesSettings.SpecialsAsEpisodes = true;
            }

            if (tags.ContainsKey("/order by airdate"))
                seriesSettings.OrderByAirdate = true;

            return seriesSettings;
        });

    private static string NormalizeCustomSeriesType(string seriesType) {
        seriesType = seriesType.ToLowerInvariant().Replace(" ", "");
        if (seriesType[^1] == 's')
          seriesType = seriesType[..^1];
        return seriesType;
    }

    #endregion

    #region Tags, Genres, And Content Ratings

    public Task<IReadOnlyDictionary<string, ResolvedTag>> GetNamespacedTagsForSeries(string seriesId)
        => DataCache.GetOrCreateAsync(
            $"series-linked-tags:{seriesId}",
            async () => {
                var nextUserTagId = 1;
                var hasCustomTags = false;
                var rootTags = new List<Tag>();
                var tagMap = new Dictionary<string, List<Tag>>();
                var tags = (await ApiClient.GetTagsForShokoSeries(seriesId).ConfigureAwait(false))
                    .OrderBy(tag => tag.Source)
                    .ThenBy(tag => tag.Source == "User" ? tag.Name.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length : 0)
                    .ToList();
                foreach (var tag in tags) {
                    if (Plugin.Instance.Configuration.HideUnverifiedTags && tag.IsVerified.HasValue && !tag.IsVerified.Value)
                        continue;

                    switch (tag.Source) {
                        case "AniDB": {
                            var parentKey = $"{tag.Source}:{tag.ParentId ?? 0}";
                            if (!tag.ParentId.HasValue) {
                                rootTags.Add(tag);
                                continue;
                            }
                            if (!tagMap.TryGetValue(parentKey, out var list))
                                tagMap[parentKey] = list = [];
                            // Remove comment on tag name itself.
                            if (tag.Name.Contains(" - "))
                                tag.Name = tag.Name.Split(" - ").First().Trim();
                            else if (tag.Name.Contains("--"))
                                tag.Name = tag.Name.Split("--").First().Trim();
                            list.Add(tag);
                            break;
                        }
                        case "User": {
                            if (!hasCustomTags) {
                                rootTags.Add(new() {
                                    Id = 0,
                                    Name = "custom user tags",
                                    Description = string.Empty,
                                    IsVerified = true,
                                    IsGlobalSpoiler = false,
                                    IsLocalSpoiler = false,
                                    LastUpdated = DateTime.UnixEpoch,
                                    Source = "Shokofin",
                                });
                                hasCustomTags = true;
                            }
                            var parentNames = tag.Name.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
                            tag.Name = parentNames.Last();
                            parentNames.RemoveAt(parentNames.Count - 1);
                            var customTagsRoot = rootTags.First(tag => tag.Source == "Shokofin" && tag.Id == 0);
                            var lastParentTag = customTagsRoot;
                            while (parentNames.Count > 0) {
                                // Take the first element from the list.
                                if (!parentNames.TryRemoveAt(0, out var name))
                                    break;

                                // Make sure the parent's children exists in our map.
                                var parentKey = $"Shokofin:{lastParentTag.Id}";
                                if (!tagMap!.TryGetValue(parentKey, out var children))
                                    tagMap[parentKey] = children = [];

                                // Add the child tag to the parent's children if needed.
                                var childTag = children.Find(t => string.Equals(name, t.Name, StringComparison.InvariantCultureIgnoreCase));
                                if (childTag is null)
                                    children.Add(childTag = new() {
                                        Id = nextUserTagId++,
                                        ParentId = lastParentTag.Id,
                                        Name = name.ToLowerInvariant(),
                                        IsVerified = true,
                                        Description = string.Empty,
                                        IsGlobalSpoiler = false,
                                        IsLocalSpoiler = false,
                                        LastUpdated = customTagsRoot.LastUpdated,
                                        Source = "Shokofin",
                                    });

                                // Switch to the child tag for the next parent name.
                                lastParentTag = childTag;
                            };

                            // Same as above, but for the last parent, be it the root or any other layer.
                            var lastParentKey = $"Shokofin:{lastParentTag.Id}";
                            if (!tagMap!.TryGetValue(lastParentKey, out var lastChildren))
                                tagMap[lastParentKey] = lastChildren = [];

                            if (!lastChildren.Any(childTag => string.Equals(childTag.Name, tag.Name, StringComparison.InvariantCultureIgnoreCase)))
                                lastChildren.Add(new() {
                                    Id = nextUserTagId++,
                                    ParentId = lastParentTag.Id,
                                    Name = tag.Name,
                                    Description = tag.Description,
                                    IsVerified = tag.IsVerified,
                                    IsGlobalSpoiler = tag.IsGlobalSpoiler,
                                    IsLocalSpoiler = tag.IsLocalSpoiler,
                                    Weight = tag.Weight,
                                    LastUpdated = tag.LastUpdated,
                                    Source = "Shokofin",
                                });
                            break;
                        }
                    }
                }
                List<Tag>? getChildren(string source, int id) => tagMap.TryGetValue($"{source}:{id}", out var list) ? list : null;
                var allResolvedTags = rootTags
                    .Select(tag => new ResolvedTag(tag, null, getChildren))
                    .SelectMany(tag => tag.RecursiveNamespacedChildren.Values.Prepend(tag))
                    .ToDictionary(tag => tag.FullName, StringComparer.InvariantCultureIgnoreCase);
                // We reassign the children because they may have been moved to a different namespace.
                foreach (var groupBy in allResolvedTags.Values.GroupBy(tag => tag.Namespace).OrderByDescending(pair => pair.Key)) {
                    if (!allResolvedTags.TryGetValue(groupBy.Key[..^1], out var nsTag))
                        continue;
                    nsTag.Children = groupBy.ToDictionary(childTag => childTag.Name, StringComparer.InvariantCultureIgnoreCase);
                    nsTag.RecursiveNamespacedChildren = nsTag.Children.Values
                        .SelectMany(childTag => childTag.RecursiveNamespacedChildren.Values.Prepend(childTag))
                        .ToDictionary(childTag => childTag.FullName[nsTag.FullName.Length..], StringComparer.InvariantCultureIgnoreCase);
                }
                return allResolvedTags as IReadOnlyDictionary<string, ResolvedTag>;
            }
        );

    private async Task<string[]> GetTagsForSeries(string seriesId) {
        var tags = await GetNamespacedTagsForSeries(seriesId).ConfigureAwait(false);
        return TagFilter.FilterTags(tags);
    }

    private async Task<string[]> GetGenresForSeries(string seriesId) {
        var tags = await GetNamespacedTagsForSeries(seriesId).ConfigureAwait(false);
        return TagFilter.FilterGenres(tags);
    }

    private async Task<string[]> GetProductionLocations(string seriesId) {
        var tags = await GetNamespacedTagsForSeries(seriesId).ConfigureAwait(false);
        return TagFilter.GetProductionCountriesFromTags(tags);
    }

    private async Task<string?> GetAssumedContentRating(string seriesId) {
        var tags = await GetNamespacedTagsForSeries(seriesId).ConfigureAwait(false);
        return ContentRating.GetTagBasedContentRating(tags);
    }

    #endregion

    #region Path Set And Local Episode IDs

    /// <summary>
    /// Get a set of paths that are unique to the series and don't belong to
    /// any other series.
    /// </summary>
    /// <param name="seriesId">Shoko series id.</param>
    /// <returns>Unique path set for the series</returns>
    public Task<HashSet<string>> GetPathSetForSeries(string seriesId)
        => DataCache.GetOrCreateAsync(
                $"series-path-set:${seriesId}",
                async () => {
                    var pathSet = new HashSet<string>();
                    foreach (var file in await ApiClient.GetFilesForShokoSeries(seriesId).ConfigureAwait(false)) {
                        if (file.CrossReferences.Count == 1 && file.CrossReferences[0] is { } xref && xref.Series.Shoko.HasValue && xref.Series.Shoko.ToString() == seriesId)
                            foreach (var fileLocation in file.Locations)
                                pathSet.Add((Path.GetDirectoryName(fileLocation.RelativePath) ?? string.Empty) + Path.DirectorySeparatorChar);
                    }

                    return pathSet;
                }
            );

    /// <summary>
    /// Get a set of local episode ids for the series.
    /// </summary>
    /// <param name="seasonInfo">Season info.</param>
    /// <returns>Local episode ids for the series</returns>
    public Task<HashSet<string>> GetLocalEpisodeIdsForSeason(SeasonInfo seasonInfo)
        => DataCache.GetOrCreateAsync(
            $"season-episode-ids:${seasonInfo.Id}",
            async () => {
                var episodeIds = new HashSet<string>();
                foreach (var seasonId in new HashSet<string>([seasonInfo.Id, ..seasonInfo.ExtraIds])) {
                    switch (seasonId[0]) {
                        case IdPrefix.TmdbShow: {
                            var files = await ApiClient.GetFilesForTmdbSeason(seasonId[1..]).ConfigureAwait(false);
                            var episodes = await ApiClient.GetTmdbEpisodesInTmdbSeason(seasonId[1..]).ConfigureAwait(false);
                            foreach (var episode in episodes) {
                                if (files.Any(file => file.CrossReferences.Any(fileXref => fileXref.Episodes.Any(episodeXref => episodeXref.TMDB.Episode.Contains(episode.Id)))))
                                    episodeIds.Add(IdPrefix.TmdbShow + episode.Id.ToString());
                            }
                            break;
                        }

                        case IdPrefix.TmdbMovie: {
                            var files = await ApiClient.GetFilesForTmdbMovie(seasonId[1..]).ConfigureAwait(false);
                            if (files.Count > 0)
                                episodeIds.Add(seasonId);
                            break;
                        }

                        default: {
                            var files = await ApiClient.GetFilesForShokoSeries(seasonId).ConfigureAwait(false);
                            foreach (var file in files) {
                                var xref = file.CrossReferences.FirstOrDefault(xref => xref.Series.Shoko.HasValue && xref.Series.Shoko.ToString() == seasonId);
                                foreach (var episodeXRef in xref?.Episodes.Where(e => e.Shoko.HasValue) ?? [])
                                    episodeIds.Add(episodeXRef.Shoko!.Value.ToString());
                            }
                            break;
                        }
                    }
                }

                return episodeIds;
            },
            new()
        );

    #endregion

    #region File Info

    internal void AddFileLookupIds(string path, string fileId, string seriesId, IEnumerable<string> episodeIds) {
        PathToFileIdAndSeriesIdDictionary.TryAdd(path, (fileId, seriesId));
        PathToEpisodeIdsDictionary.TryAdd(path, episodeIds.ToList());
    }

    public async Task<(FileInfo?, SeasonInfo?, ShowInfo?)> GetFileInfoByPath(string path) {
        // Use pointer for fast lookup.
        if (PathToFileIdAndSeriesIdDictionary.TryGetValue(path, out (string FileId, string SeriesId) tuple)) {
            var (fI, sI) = tuple;
            var fileInfo = await GetFileInfo(fI, sI).ConfigureAwait(false);
            if (fileInfo == null || fileInfo.EpisodeList.Count is 0)
                return (null, null, null);

            var selectedSeasonId = fileInfo.EpisodeList[0].Episode.SeasonId;
            var seasonInfo = await GetSeasonInfo(selectedSeasonId).ConfigureAwait(false);
            if (seasonInfo == null)
                return (null, null, null);

            var showInfo = await GetShowInfoBySeasonId(selectedSeasonId).ConfigureAwait(false);
            if (showInfo == null)
                return (null, null, null);

            return new(fileInfo, seasonInfo, showInfo);
        }

        // Fast-path for VFS.
        if (path.StartsWith(Plugin.Instance.VirtualRoot + Path.DirectorySeparatorChar)) {
            var fileName = Path.GetFileNameWithoutExtension(path);
            if (!fileName.TryGetAttributeValue(ShokoSeriesId.Name, out var sI) || !int.TryParse(sI, out _))
                return (null, null, null);
            if (!fileName.TryGetAttributeValue(ShokoFileId.Name, out var fI) || !int.TryParse(fI, out _))
                return (null, null, null);

            var fileInfo = await GetFileInfo(fI, sI).ConfigureAwait(false);
            if (fileInfo == null || fileInfo.EpisodeList.Count is 0)
                return (null, null, null);

            var selectedSeasonId = fileInfo.EpisodeList[0].Episode.SeasonId;
            var seasonInfo = await GetSeasonInfo(selectedSeasonId).ConfigureAwait(false);
            if (seasonInfo == null)
                return (null, null, null);

            var showInfo = await GetShowInfoBySeasonId(selectedSeasonId).ConfigureAwait(false);
            if (showInfo == null)
                return (null, null, null);

            AddFileLookupIds(path, fI, sI, fileInfo.EpisodeList.Select(episode => episode.Id));
            return (fileInfo, seasonInfo, showInfo);
        }

        // Strip the path and search for a match.
        var partialPath = StripMediaFolder(path);
        var result = await ApiClient.GetFileByPath(partialPath).ConfigureAwait(false);
        Logger.LogDebug("Looking for a match for {Path}", partialPath);

        // Check if we found a match.
        var file = result is { Count: > 0 } ? result[0] : null;
        if (file == null || file.CrossReferences.Count == 0) {
            Logger.LogTrace("Found no match for {Path}", partialPath);
            return (null, null, null);
        }

        // Find the file locations matching the given path.
        var fileId = file.Id.ToString();
        var fileLocations = file.Locations
            .Where(location => location.RelativePath.EndsWith(partialPath))
            .ToList();
        Logger.LogTrace("Found a file match for {Path} (File={FileId})", partialPath, file.Id.ToString());
        if (fileLocations.Count != 1) {
            if (fileLocations.Count == 0)
                throw new Exception($"I have no idea how this happened, but the path gave a file that doesn't have a matching file location. See you in #support. (File={fileId})");

            Logger.LogWarning("Multiple locations matched the path, picking the first location. (File={FileId})", fileId);
        }

        // Find the correct series based on the path.
        var selectedPath = (Path.GetDirectoryName(fileLocations.First().RelativePath) ?? string.Empty) + Path.DirectorySeparatorChar;
        foreach (var seriesXRef in file.CrossReferences.Where(xref => xref.Series.Shoko.HasValue && xref.Episodes.All(e => e.Shoko.HasValue))) {
            var seriesId = seriesXRef.Series.Shoko!.Value.ToString();

            // Check if the file is in the series folder.
            var pathSet = await GetPathSetForSeries(seriesId).ConfigureAwait(false);
            if (!pathSet.Contains(selectedPath))
                continue;

            // Find the file info for the series.
            var fileInfo = await CreateFileInfo(file, fileId, seriesId).ConfigureAwait(false);
            if (fileInfo.EpisodeList.Count is 0)
                return (null, null, null);

            var seasonId = fileInfo.EpisodeList[0].Episode.SeasonId;
            var seasonInfo = await GetSeasonInfo(seasonId).ConfigureAwait(false);
            if (seasonInfo == null)
                return (null, null, null);

            var showInfo = await GetShowInfoBySeasonId(seasonId).ConfigureAwait(false);
            if (showInfo == null)
                return (null, null, null);

            // Add pointers for faster lookup.
            AddFileLookupIds(path, fileId, seriesId, fileInfo.EpisodeList.Select(episode => episode.Id));

            // Return the result.
            return new(fileInfo, seasonInfo, showInfo);
        }

        throw new Exception($"Unable to determine the series to use for the file based on it's location because the file resides within a mixed folder with multiple AniDB anime in it. You will either have to fix your file structure or use the VFS to avoid this issue. (File={fileId})\nFile location; {path}");
    }

    public async Task<FileInfo?> GetFileInfo(string fileId, string seriesId) {
        if (string.IsNullOrEmpty(fileId) || string.IsNullOrEmpty(seriesId))
            return null;

        var cacheKey = $"file:{fileId}:{seriesId}";
        if (DataCache.TryGetValue<FileInfo>(cacheKey, out var fileInfo))
            return fileInfo;

        if (await ApiClient.GetFile(fileId).ConfigureAwait(false) is not { } file)
            return null;

        return await CreateFileInfo(file, fileId, seriesId).ConfigureAwait(false);
    }

    private static readonly EpisodeType[] EpisodePickOrder = [EpisodeType.Special, EpisodeType.Normal, EpisodeType.Other];

    private Task<FileInfo> CreateFileInfo(File file, string fileId, string seriesId)
        => DataCache.GetOrCreateAsync(
            $"file:{fileId}:{seriesId}",
            async () => {
                Logger.LogTrace("Creating info object for file. (File={FileId},Series={SeriesId})", fileId, seriesId);

                // Find the cross-references for the selected series.
                var seriesConfig = await GetSeriesConfiguration(seriesId).ConfigureAwait(false);
                var seriesXRef = file.CrossReferences
                    .Where(xref => xref.Series.Shoko.HasValue && xref.Episodes.All(e => e.Shoko.HasValue))
                    .FirstOrDefault(xref => xref.Series.Shoko!.Value.ToString() == seriesId) ??
                    throw new Exception($"Unable to find any cross-references for the specified series for the file. (File={fileId},Series={seriesId})");

                // Find a list of the episode info for each episode linked to the file for the series.
                var episodeList = new List<(EpisodeInfo Episode, CrossReference.EpisodeCrossReferenceIDs CrossReference, string Id)>();
                foreach (var episodeXRef in seriesXRef.Episodes) {
                    var episodeId = episodeXRef.Shoko!.Value.ToString();
                    if (await ApiClient.GetShokoEpisode(episodeId).ConfigureAwait(false) is not { } episode) {
                        Logger.LogDebug("Skipped unknown episode linked to file. (File={FileId},Episode={EpisodeId},Series={SeriesId})", fileId, episodeId, seriesId);
                        continue;
                    }

                    if (episode.IsHidden) {
                        Logger.LogDebug("Skipped hidden episode linked to file. (File={FileId},Episode={EpisodeId},Series={SeriesId})", fileId, episodeId, seriesId);
                        continue;
                    }

                    if (seriesConfig.StructureType is SeriesStructureType.TMDB_SeriesAndMovies) {
                        var tmdbEpisodes = await Task.WhenAll(episodeXRef.TMDB.Episode.Select(id => GetEpisodeInfo(IdPrefix.TmdbShow + id.ToString()))).ConfigureAwait(false);
                        foreach (var tmdbEpisode in tmdbEpisodes) {
                            if (tmdbEpisode == null)
                                continue;
                            episodeList.Add((tmdbEpisode, episodeXRef, tmdbEpisode.Id));
                        }

                        var tmdbMovies = await Task.WhenAll(episodeXRef.TMDB.Movie.Select(id => GetEpisodeInfo(IdPrefix.TmdbMovie + id.ToString()))).ConfigureAwait(false);
                        foreach (var tmdbMovie in tmdbMovies) {
                            if (tmdbMovie == null)
                                continue;
                            episodeList.Add((tmdbMovie, episodeXRef, tmdbMovie.Id));
                        }
                        continue;
                    }
                    var episodeInfo = await GetEpisodeInfo(episodeId).ConfigureAwait(false) ??
                        throw new Exception($"Unable to find episode cross-reference for the specified series and episode for the file. (File={fileId},Episode={episodeId},Series={seriesId})");
                    episodeList.Add((episodeInfo, episodeXRef, episodeId));
                }

                // Distinct the list in case the shoko episodes are linked to the same tmdb episode(s)/movie(s).
                if (seriesConfig.StructureType is SeriesStructureType.TMDB_SeriesAndMovies) {
                    episodeList = episodeList
                        .DistinctBy(tuple => tuple.Id)
                        .ToList();
                }

                // Group and order the episodes, then select the first group to use.
                var groupedEpisodeLists = episodeList
                    .GroupBy(tuple => (type: tuple.Episode.Type, group: tuple.CrossReference.Percentage.Group, isStandalone: tuple.Episode.IsStandalone))
                    .OrderByDescending(a => Array.IndexOf(EpisodePickOrder, a.Key.type))
                    .ThenBy(a => a.Key.group)
                    .ThenByDescending(a => a.Key.isStandalone)
                    .Select(epList => epList.OrderBy(tuple => tuple.Episode.SeasonNumber).ThenBy(tuple => tuple.Episode.EpisodeNumber).ToList() as IReadOnlyList<(EpisodeInfo Episode, CrossReference.EpisodeCrossReferenceIDs CrossReference, string Id)> ?? [])
                    .ToList();
                var selectedEpisodeList = groupedEpisodeLists.FirstOrDefault() ?? [];
                var fileInfo = new FileInfo(file, seriesId, selectedEpisodeList);

                FileAndSeasonIdToEpisodeIdDictionary[$"{fileId}:{seriesId}"] = episodeList.Select(episode => episode.Id).ToList();

                return fileInfo;
            }
        );

    public bool TryGetFileIdForPath(string path, [NotNullWhen(true)] out string? fileId, [NotNullWhen(true)] out string? seriesId) {
        if (string.IsNullOrEmpty(path)) {
            fileId = null;
            seriesId = null;
            return false;
        }

        // Fast path; using the lookup.
        if (PathToFileIdAndSeriesIdDictionary.TryGetValue(path, out var pair)) {
            fileId = pair.FileId;
            seriesId = pair.SeriesId;
            return true;
        }

        // Slow path; getting the show from cache or remote and finding the default season's id.
        Logger.LogDebug("Trying to find file id using the slow path. (Path={FullPath})", path);
        try {
            if (GetFileInfoByPath(path).ConfigureAwait(false).GetAwaiter().GetResult() is { } tuple && tuple.Item1 is not null) {
                var (fileInfo, _, _) = tuple;
                fileId = fileInfo.Id;
                seriesId = fileInfo.SeriesId;
                return true;
            }
        }
        catch (Exception ex) {
            Logger.LogError(ex, "Encountered an error while trying to lookup the file id for {Path}", path);
        }

        fileId = null;
        seriesId = null;
        return false;
    }

    #endregion

    #region Episode Info

    public async Task<EpisodeInfo?> GetEpisodeInfo(string episodeId) {
        if (string.IsNullOrEmpty(episodeId))
            return null;

        if (DataCache.TryGetValue<EpisodeInfo>($"episode:{episodeId}", out var episodeInfo))
            return episodeInfo;

        switch (episodeId[0]) {
            case IdPrefix.TmdbShow:
                if (await ApiClient.GetTmdbEpisode(episodeId[1..]).ConfigureAwait(false) is not { } tmdbEpisode)
                    return null;

                if (await ApiClient.GetTmdbShowForSeason(tmdbEpisode.SeasonId).ConfigureAwait(false) is not { } tmdbShow)
                    return null;

                return CreateEpisodeInfo(tmdbEpisode, tmdbShow);

            case IdPrefix.TmdbMovie:
                if (await ApiClient.GetTmdbMovie(episodeId[1..]).ConfigureAwait(false) is not { } tmdbMovie)
                    return null;

                return CreateEpisodeInfo(tmdbMovie);

            default:
                if (await ApiClient.GetShokoEpisode(episodeId).ConfigureAwait(false) is not { } shokoEpisode)
                    return null;

                return await CreateEpisodeInfo(shokoEpisode).ConfigureAwait(false);
        }
    }

    private EpisodeInfo CreateEpisodeInfo(TmdbMovie movie)
        => DataCache.GetOrCreate(
            $"episode:{IdPrefix.TmdbMovie}{movie.Id}",
            () => {
                Logger.LogTrace("Creating info object for episode {EpisodeName}. (Source=TMDB,Movie={MovieId})", movie.Title, movie.Id);

                return new EpisodeInfo(ApiClient, movie);
            }
        );

    private EpisodeInfo CreateEpisodeInfo(TmdbEpisode episode, TmdbShow show)
        => DataCache.GetOrCreate(
            $"episode:{IdPrefix.TmdbShow}{episode.Id}",
            () => {
                Logger.LogTrace("Creating info object for episode {EpisodeName}. (Source=TMDB,Episode={EpisodeId})", episode.Title, episode.Id);

                return new EpisodeInfo(ApiClient, episode, show);
            }
        );

    private Task<EpisodeInfo> CreateEpisodeInfo(ShokoEpisode episode)
        => DataCache.GetOrCreateAsync(
            $"episode:{episode.Id}",
            async () => {
                Logger.LogTrace("Creating info object for episode {EpisodeName}. (Source=Shoko,Episode={EpisodeId})", episode.Name, episode.Id);

                var (cast, genres, tags, productionLocations, contentRating) = await GetExtraEpisodeDetailsForShokoSeries(episode.IDs.ParentSeries.ToString()).ConfigureAwait(false);

                ITmdbEntity? tmdbEntity = null;
                ITmdbParentEntity? tmdbParentEntity = null;
                foreach (var tmdbMovieId in episode.IDs.TMDB.Movie) {
                    Logger.LogTrace("Trying to find TMDB movie {MovieId} for episode {EpisodeName}. (Source=Shoko,Episode={EpisodeId})", tmdbMovieId, episode.Name, episode.Id);
                    if (await ApiClient.GetTmdbMovie(tmdbMovieId.ToString()).ConfigureAwait(false) is { } tmdbMovie) {
                        tmdbEntity = tmdbMovie;
                        Logger.LogTrace("Found TMDB movie {MovieId} for episode {EpisodeName}. (Source=Shoko,Episode={EpisodeId})", tmdbMovieId, episode.Name, episode.Id);
                        break;
                    }
                    Logger.LogTrace("Did not find TMDB movie {MovieId} for episode {EpisodeName}. (Source=Shoko,Episode={EpisodeId})", tmdbMovieId, episode.Name, episode.Id);
                }

                if (tmdbEntity is null) {
                    foreach (var tmdbEpisodeId in episode.IDs.TMDB.Episode) {
                        Logger.LogTrace("Trying to find TMDB episode {EpisodeId} for episode {EpisodeName}. (Source=Shoko,Episode={EpisodeId})", tmdbEpisodeId, episode.Name, episode.Id);
                        if (await ApiClient.GetTmdbEpisode(tmdbEpisodeId.ToString()).ConfigureAwait(false) is { } tmdbEpisode) {
                            tmdbEntity = tmdbEpisode;
                            Logger.LogTrace("Found TMDB episode {EpisodeId} for episode {EpisodeName}. (Source=Shoko,Episode={EpisodeId})", tmdbEpisodeId, episode.Name, episode.Id);

                            if (await ApiClient.GetTmdbShowForSeason(tmdbEpisode.SeasonId).ConfigureAwait(false) is { } tmdbShow) {
                                tmdbParentEntity = tmdbShow;
                                Logger.LogTrace("Found TMDB show {ShowId} for episode {EpisodeName}. (Source=Shoko,Episode={EpisodeId})", tmdbShow.Id, episode.Name, episode.Id);
                            }

                            break;
                        }
                        Logger.LogTrace("Did not find TMDB episode {EpisodeId} for episode {EpisodeName}. (Source=Shoko,Episode={EpisodeId})", tmdbEpisodeId, episode.Name, episode.Id);
                    }
                }

                return new EpisodeInfo(ApiClient, episode, cast, [.. genres], [.. tags], productionLocations, contentRating, tmdbEntity, tmdbParentEntity);
            }
        );

    private Task<(IReadOnlyList<Role>, string[], string[], string[], string?)> GetExtraEpisodeDetailsForShokoSeries(string seriesId)
        => DataCache.GetOrCreateAsync(
            $"series-episode-details:{seriesId}",
            async () => {
                var cast = await ApiClient.GetCastForShokoSeries(seriesId).ConfigureAwait(false);
                var genres = await GetGenresForSeries(seriesId).ConfigureAwait(false);
                var tags = await GetTagsForSeries(seriesId).ConfigureAwait(false);
                var productionLocations = await GetProductionLocations(seriesId).ConfigureAwait(false);
                var contentRating = await GetAssumedContentRating(seriesId).ConfigureAwait(false);
                return (cast, genres, tags, productionLocations, contentRating);
            }
        );

    #endregion

    #region Episode Id Helpers

    public bool TryGetEpisodeIdsForPath(string path, [NotNullWhen(true)] out List<string>? episodeIds) {
        if (string.IsNullOrEmpty(path)) {
            episodeIds = null;
            return false;
        }

        // Fast path; using the lookup.
        if (PathToEpisodeIdsDictionary.TryGetValue(path, out episodeIds))
            return true;

        // Slow path; getting the show from cache or remote and finding the default season's id.
        Logger.LogDebug("Trying to find episode ids using the slow path. (Path={FullPath})", path);
        if (GetFileInfoByPath(path).ConfigureAwait(false).GetAwaiter().GetResult() is { } tuple && tuple.Item1 is not null) {
            var (fileInfo, _, _) = tuple;
            episodeIds = fileInfo.EpisodeList.Select(episodeInfo => episodeInfo.Id).ToList();
            return episodeIds.Count is > 0;
        }

        episodeIds = null;
        return false;
    }

    public bool TryGetEpisodeIdsForFileId(string fileId, string seriesId, [NotNullWhen(true)] out List<string>? episodeIds) {
        if (string.IsNullOrEmpty(fileId) || string.IsNullOrEmpty(seriesId)) {
            episodeIds = null;
            return false;
        }

        // Fast path; using the lookup.
        if (FileAndSeasonIdToEpisodeIdDictionary.TryGetValue($"{fileId}:{seriesId}", out episodeIds))
            return true;

        // Slow path; getting the show from cache or remote and finding the default season's id.
        Logger.LogDebug("Trying to find episode ids using the slow path. (Series={SeriesId},File={FileId})", seriesId, fileId);
        if (GetFileInfo(fileId, seriesId).ConfigureAwait(false).GetAwaiter().GetResult() is { } fileInfo) {
            episodeIds = fileInfo.EpisodeList.Select(episodeInfo => episodeInfo.Id).ToList();
            return true;
        }

        episodeIds = null;
        return false;
    }

    #endregion

    #region Season Info

    public async Task<SeasonInfo?> GetSeasonInfo(string seasonId) {
        if (string.IsNullOrEmpty(seasonId))
            return null;

        if (DataCache.TryGetValue<SeasonInfo>($"season:{seasonId}", out var seasonInfo))
            return seasonInfo;

        switch (seasonId[0]) {
            case IdPrefix.TmdbShow:
                if (await ApiClient.GetTmdbSeason(seasonId[1..]).ConfigureAwait(false) is not { } tmdbSeason)
                    return null;

                if (await ApiClient.GetTmdbShowForSeason(tmdbSeason.Id).ConfigureAwait(false) is not { } tmdbShow)
                    return null;

                return await CreateSeasonInfo(tmdbSeason, tmdbShow).ConfigureAwait(false);

            case IdPrefix.TmdbMovie:
                if (await ApiClient.GetTmdbMovie(seasonId[1..]).ConfigureAwait(false) is not { } tmdbMovie)
                    return null;

                return await CreateSeasonInfo(tmdbMovie).ConfigureAwait(false);

            case IdPrefix.TmdbMovieCollection:
                if (await ApiClient.GetTmdbMovieCollection(seasonId[1..]).ConfigureAwait(false) is not { } tmdbMovieCollection)
                    return null;

                return await CreateSeasonInfo(tmdbMovieCollection).ConfigureAwait(false);

            default:
                if (await ApiClient.GetShokoSeries(seasonId).ConfigureAwait(false) is not { } shokoSeries)
                    return null;

                return await CreateSeasonInfo(shokoSeries).ConfigureAwait(false);
        }
    }

    public async Task<SeasonInfo?> GetSeasonInfoByPath(string path) {
        if (!PathToSeasonIdDictionary.TryGetValue(path, out var seasonId)) {
            seasonId = await GetSeasonIdForPath(path).ConfigureAwait(false);
            if (string.IsNullOrEmpty(seasonId))
                return null;
        }

        return await GetSeasonInfo(seasonId).ConfigureAwait(false);
    }

    public async Task<SeasonInfo?> GetSeasonInfoForEpisode(string episodeId) {
        if (string.IsNullOrEmpty(episodeId))
            return null;

        if (EpisodeIdToSeasonIdDictionary.TryGetValue(episodeId, out var seasonId))
            return await GetSeasonInfo(seasonId).ConfigureAwait(false);

        switch (episodeId[0]) {
            case IdPrefix.TmdbShow:
                if (await ApiClient.GetTmdbSeasonForTmdbEpisode(episodeId[1..]).ConfigureAwait(false) is not { } tmdbSeason)
                    return null;

                if (await ApiClient.GetTmdbShowForSeason(tmdbSeason.Id).ConfigureAwait(false) is not { } tmdbShow)
                    return null;

                return await CreateSeasonInfo(tmdbSeason, tmdbShow).ConfigureAwait(false);

            case IdPrefix.TmdbMovie:
                if (await ApiClient.GetTmdbMovie(episodeId[1..]).ConfigureAwait(false) is not { } tmdbMovie)
                    return null;

                var episodeInfo = CreateEpisodeInfo(tmdbMovie);
                return await GetSeasonInfo(episodeInfo.SeasonId).ConfigureAwait(false);

            default:
                if (await ApiClient.GetShokoSeriesForShokoEpisode(episodeId).ConfigureAwait(false) is not { } shokoSeries)
                    return null;

                return await CreateSeasonInfo(shokoSeries).ConfigureAwait(false);
        }
    }

    public Task<IReadOnlyList<SeasonInfo>> GetSeasonInfosForShokoSeries(string seriesId)
        => DataCache.GetOrCreateAsync<IReadOnlyList<SeasonInfo>>(
            $"seasons-by-series-id:{seriesId}",
            (seasons) => Logger.LogTrace("Reusing info objects for seasons. (Series={SeriesId})", seriesId),
            async () => {
                Logger.LogTrace("Creating info objects for seasons for series {SeriesName}. (Series={SeriesId})", seriesId, seriesId);
                if (await ApiClient.GetShokoSeries(seriesId).ConfigureAwait(false) is not { } series)
                    return [];

                var seriesConfig = await GetSeriesConfiguration(seriesId).ConfigureAwait(false);
                if (seriesConfig.StructureType is SeriesStructureType.TMDB_SeriesAndMovies) {
                    var seasons = new List<SeasonInfo>();
                    var episodeXrefs = await ApiClient.GetTmdbCrossReferencesForShokoSeries(seriesId).ConfigureAwait(false);
                    var showIds = episodeXrefs
                        .GroupBy(x => x.TmdbShowId)
                        .OrderByDescending(x => x.Count())
                        .Select(x => x.Key)
                        .Except([0])
                        .ToList();
                    foreach (var showId in showIds) {
                        var episodes = (await ApiClient.GetTmdbEpisodesInTmdbShow(showId.ToString()).ConfigureAwait(false)).ToDictionary(e => e.Id);
                        var seasonIds = episodeXrefs
                            .Where(x => x.TmdbShowId == showId)
                            .GroupBy(x => episodes.TryGetValue(x.TmdbEpisodeId, out var e) ? e.SeasonId : string.Empty)
                            .OrderByDescending(x => x.Count())
                            .Select(x => x.Key)
                            .Except([string.Empty])
                            .ToList();
                        foreach (var seasonId in seasonIds) {
                            if (await GetSeasonInfo(IdPrefix.TmdbShow + seasonId).ConfigureAwait(false) is not { } seasonInfo)
                                continue;

                            seasons.Add(seasonInfo);
                        }
                    }
                    foreach (var movieId in series.IDs.TMDB.Movie) {
                        if (await GetSeasonInfo(IdPrefix.TmdbMovie + movieId.ToString()).ConfigureAwait(false) is not { } seasonInfo)
                            continue;

                        seasons.Add(seasonInfo);
                    }

                    return seasons;
                }
                else {
                    if (await GetSeasonInfo(seriesId).ConfigureAwait(false) is not { } seasonInfo)
                        return [];

                    return [seasonInfo];
                }
            }
        );

    private Task<SeasonInfo> CreateSeasonInfo(TmdbMovie tmdbMovie)
        => DataCache.GetOrCreateAsync(
            $"season:{IdPrefix.TmdbMovie}{tmdbMovie.Id}",
            (seasonInfo) => Logger.LogTrace("Reusing info object for season {SeasonTitle}. (Source=TMDB,Movie={MovieId})", seasonInfo.Title, tmdbMovie.Id),
            async () => {
                Logger.LogTrace("Creating info object for season {SeasonTitle}. (Source=TMDB,Movie={MovieId})", tmdbMovie.Title, tmdbMovie.Id);

                var episodeInfo = CreateEpisodeInfo(tmdbMovie);
                var animeIds = (await ApiClient.GetTmdbCrossReferencesForTmdbMovie(tmdbMovie.Id.ToString()).ConfigureAwait(false))
                    .GroupBy(x => x.AnidbAnimeId)
                    .OrderByDescending(x => x.Count())
                    .Select(x => x.Key)
                    .Except([0])
                    .ToList();
                var (anidbId, shokoSeriesId, shokoGroupId, topLevelShokoGroupId) = await GetGroupIdsForAnidbAnime(animeIds, $"TMDB movie \"{tmdbMovie.Title}\"", $"Movie=\"{tmdbMovie.Id}\"").ConfigureAwait(false);
                return new SeasonInfo(ApiClient, tmdbMovie, episodeInfo, anidbId, shokoSeriesId, shokoGroupId, topLevelShokoGroupId);
            });

    private Task<SeasonInfo> CreateSeasonInfo(TmdbMovieCollection tmdbMovieCollection)
        => DataCache.GetOrCreateAsync(
            $"season:{IdPrefix.TmdbMovieCollection}{tmdbMovieCollection.Id}",
            (seasonInfo) => Logger.LogTrace("Reusing info object for season {SeasonTitle}. (Source=TMDB,MovieCollection={MovieId})", seasonInfo.Title, tmdbMovieCollection.Id),
            async () => {
                Logger.LogTrace("Creating info object for season {SeasonTitle}. (Source=TMDB,MovieCollection={MovieId})", tmdbMovieCollection.Title, tmdbMovieCollection.Id);

                var moviesInCollection = await ApiClient.GetTmdbMoviesInMovieCollection(tmdbMovieCollection.Id.ToString()).ConfigureAwait(false);
                var episodeInfos = moviesInCollection.Select(tmdbMovie => CreateEpisodeInfo(tmdbMovie)).ToList();
                var animeIds = (await Task.WhenAll(moviesInCollection.Select(tmdbMovie => ApiClient.GetTmdbCrossReferencesForTmdbMovie(tmdbMovie.Id.ToString()))).ConfigureAwait(false))
                    .SelectMany(x => x)
                    .GroupBy(x => x.AnidbAnimeId)
                    .OrderByDescending(x => x.Count())
                    .Select(x => x.Key)
                    .Except([0])
                    .ToList();
                var (anidbId, shokoSeriesId, shokoGroupId, topLevelShokoGroupId) = await GetGroupIdsForAnidbAnime(animeIds, $"TMDB movie collection \"{tmdbMovieCollection.Title}\"", $"MovieCollection=\"{tmdbMovieCollection.Id}\"").ConfigureAwait(false);
                return new SeasonInfo(ApiClient, tmdbMovieCollection, moviesInCollection, episodeInfos, anidbId, shokoSeriesId, shokoGroupId, topLevelShokoGroupId);
            });

    private Task<SeasonInfo> CreateSeasonInfo(TmdbSeason tmdbSeason, TmdbShow tmdbShow)
        => DataCache.GetOrCreateAsync(
            $"season:{IdPrefix.TmdbShow}{tmdbSeason.Id}",
            (seasonInfo) => Logger.LogTrace("Reusing info object for season {SeasonTitle}. (Source=TMDB,Season={SeasonId},Show={ShowId})", seasonInfo.Title, tmdbSeason.Id, tmdbSeason.ShowId),
            async () => {
                Logger.LogTrace("Creating info object for season {SeasonTitle}. (Source=TMDB,Season={SeasonId},Show={ShowId})", tmdbSeason.Title, tmdbSeason.Id, tmdbSeason.ShowId);

                var tmdbEpisodes = (await ApiClient.GetTmdbEpisodesInTmdbSeason(tmdbSeason.Id).ConfigureAwait(false))
                    .ToDictionary(e => e.Id);
                var episodeInfos = tmdbEpisodes.Values.Select(tmdbEpisode => CreateEpisodeInfo(tmdbEpisode, tmdbShow)).ToList();

                string? anidbId = null;
                string? shokoSeriesId = null;
                string? shokoGroupId = null;
                string? topLevelShokoGroupId = null;
                if (tmdbSeason.SeasonNumber > 0) {
                    var animeIds = (await ApiClient.GetTmdbCrossReferencesForTmdbShow(tmdbSeason.ShowId.ToString()).ConfigureAwait(false))
                        .Where(x => tmdbEpisodes.TryGetValue(x.TmdbEpisodeId, out var tmdbEpisode) && tmdbEpisode.SeasonId == tmdbSeason.Id)
                        .GroupBy(x => x.AnidbAnimeId)
                        .OrderByDescending(x => x.Count())
                        .Select(x => x.Key)
                        .Except([0])
                        .ToList();
                    (anidbId, shokoSeriesId, shokoGroupId, topLevelShokoGroupId) = await GetGroupIdsForAnidbAnime(animeIds, $"season {tmdbSeason.SeasonNumber} in TMDB show \"{tmdbShow.Title}\"", $"Season=\"{tmdbSeason.Id}\",Show=\"{tmdbSeason.ShowId}\"").ConfigureAwait(false);
                }

                return new SeasonInfo(ApiClient, tmdbSeason, tmdbShow, episodeInfos, anidbId, shokoSeriesId, shokoGroupId, topLevelShokoGroupId);
            });

#pragma warning disable CA2254 // Template should be a static method
    private async Task<(string? anidbId, string? shokoSeriesId, string? shokoGroupId, string? topLevelShokoGroupId)> GetGroupIdsForAnidbAnime(IReadOnlyList<int> animeIds, string entryName, string entryId) {
        Logger.LogTrace($"Found {{AnidbAnimeCount}} AniDB anime for {entryName} to pick a Shoko Group to use. (Anime={{AnimeIds}},{entryId})", animeIds.Count, animeIds);

        if (animeIds.Count is 0)
            return (null, null, null, null);

        string? anidbId = null;
        string? shokoSeriesId = null;
        string? shokoGroupId = null;
        string? topLevelShokoGroupId = null;
        var shokoGroupIdList = new List<(string anidbId, string shokoSeriesId, string shokoGroupId, string topLevelShokoGroupId)>();
        foreach (var animeId in animeIds) {
            if (await ApiClient.GetShokoSeriesForAnidbAnime(animeId.ToString()).ConfigureAwait(false) is not { } shokoSeries)
                continue;

            shokoGroupIdList.Add((animeId.ToString(), shokoSeries.IDs.Shoko.ToString(), shokoSeries.IDs.ParentGroup.ToString(), shokoSeries.IDs.TopLevelGroup.ToString()));

            Logger.LogTrace($"Found Shoko series to use for {entryName}. (Anime={{AnimeId}},Series={{SeriesId}},Group={{GroupId}},{entryId})", animeId, shokoSeries.Id, shokoSeries.IDs.ParentGroup.ToString());
        }

        var shokoGroupIdCounts = shokoGroupIdList
            .GroupBy(x => x.shokoGroupId)
            .OrderByDescending(x => x.Count())
            .ToDictionary(x => x.Key, x => (x.Select(y => (y.shokoSeriesId, y.anidbId)).Distinct().ToArray(), x.First().topLevelShokoGroupId));
        if (shokoGroupIdCounts.Count is > 1) {
            var topLevelGroupIdCount = shokoGroupIdList
                .GroupBy(x => x.topLevelShokoGroupId)
                .OrderByDescending(x => x.Count())
                .Select(x => x.Key)
                .ToList();
            if (topLevelGroupIdCount.Count is 1) {
                topLevelShokoGroupId = topLevelGroupIdCount[0];
                Logger.LogTrace($"Multiple Shoko groups in the same top-level groups linked to {entryName}. (Anime={{AnimeIds}},TopLevelGroup={{TopLevelGroupId}},{entryId})", animeIds, topLevelShokoGroupId);
            }
            else {
                Logger.LogTrace($"Multiple Shoko groups in multiple top-level groups linked to {entryName}. (Anime={{AnimeIds}},{entryId})", animeIds);
            }
        }
        else if (shokoGroupIdCounts.Count is 1) {
            (shokoGroupId, (var shokoSeriesIdList, topLevelShokoGroupId)) = shokoGroupIdCounts.First();
            if (shokoSeriesIdList.Length is 1) {
                anidbId = shokoSeriesIdList[0].anidbId;
                shokoSeriesId = shokoSeriesIdList[0].shokoSeriesId;
                Logger.LogTrace($"Found Shoko series and Shoko group to use for {entryName}. (Anime={{AnimeId}}Series={{SeriesId}},Group={{GroupId}},TopLevelGroup={{TopLevelGroupId}},{entryId})", anidbId, shokoSeriesId, shokoGroupId, topLevelShokoGroupId);
            }
            else {
                Logger.LogTrace($"Found Shoko group to use for {entryName}. (Group={{GroupId}},TopLevelGroup={{TopLevelGroupId}},{entryId})", shokoGroupId, topLevelShokoGroupId);
            }
        }
        else {
            Logger.LogTrace($"Could not find Shoko group for {entryName}. ({entryId})");
        }

        return (anidbId, shokoSeriesId, shokoGroupId, topLevelShokoGroupId);
    }
#pragma warning restore CA2254 // Template should be a static method

    private async Task<SeasonInfo> CreateSeasonInfo(ShokoSeries series) {
        var (primaryId, extraIds) = await GetSeriesIdsForSeason(series).ConfigureAwait(false);
        return await DataCache.GetOrCreateAsync(
            $"season:{primaryId}",
            (seasonInfo) => Logger.LogTrace("Reusing info object for season {SeasonTitle}. (Source=Shoko,Series={SeriesId},ExtraSeries={ExtraIds})", seasonInfo.Title, primaryId, extraIds),
            async () => {
                // We updated the "primary" series id for the merge group, so fetch the new series details from the client cache.
                if (!string.Equals(series.Id, primaryId, StringComparison.Ordinal))
                    series = await ApiClient.GetShokoSeries(primaryId).ConfigureAwait(false)
                        ?? throw new InvalidOperationException("Could not find series with id " + primaryId);

                Logger.LogTrace("Creating info object for season {SeasonTitle}. (Source=Shoko,Series={SeriesId},ExtraSeries={ExtraIds})", series.Name, primaryId, extraIds);

                var episodes = (await Task.WhenAll(
                    extraIds.Prepend(primaryId)
                        .Select(id => ApiClient.GetShokoEpisodesInShokoSeries(id)
                            .ContinueWith(task => Task.WhenAll(task.Result.Select(CreateEpisodeInfo)))
                            .Unwrap()
                        )
                ).ConfigureAwait(false))
                    .SelectMany(list => list)
                    .ToList();

                ITmdbEntity? tmdbEntity = null;
                if (series.IDs.TMDB.Show.Count > 0 || series.IDs.TMDB.Movie.Count > 0) {
                    if (series.IDs.TMDB.Show.Count > 0) {
                        Logger.LogTrace("Found {TmdbShowCount} TMDB shows for Shoko Series {SeriesTitle} to pick a season to use. (Series={SeriesId})", series.IDs.TMDB.Show.Count, series.Name, primaryId);

                        var episodeXrefs = await ApiClient.GetTmdbCrossReferencesForShokoSeries(primaryId).ConfigureAwait(false);
                        var showIds = episodeXrefs
                            .GroupBy(x => x.TmdbShowId)
                            .OrderByDescending(x => x.Count())
                            .Select(x => x.Key)
                            .Except([0])
                            .ToList();
                        foreach (var showId in showIds) {
                            var tmdbEpisodes = (await ApiClient.GetTmdbEpisodesInTmdbShow(showId.ToString()).ConfigureAwait(false)).ToDictionary(e => e.Id);
                            var seasonIds = episodeXrefs
                                .Where(x => x.TmdbShowId == showId)
                                .GroupBy(x => tmdbEpisodes.TryGetValue(x.TmdbEpisodeId, out var e) ? e.SeasonId : string.Empty)
                                .OrderByDescending(x => x.Count())
                                .ExceptBy([string.Empty], x => x.Key)
                                .ToDictionary(x => x.Key, x => x.Count());

                            Logger.LogTrace("Found {TmdbSeasonCount} TMDB seasons to potentially use. (Series={SeriesId},Show={ShowId})", seasonIds.Count, primaryId, showId);

                            var fullyMatchedSeasons = 0;
                            foreach (var (seasonId, matchedEpisodeCount) in seasonIds) {
                                if (await ApiClient.GetTmdbSeason(seasonId).ConfigureAwait(false) is { } tmdbSeason) {
                                    if (tmdbSeason.SeasonNumber is 0) {
                                        Logger.LogTrace("Found season zero for Shoko Series {SeriesTitle}. Skipping season match. (Series={SeriesId},Season={SeasonId},Show={ShowId})", series.Name, primaryId, tmdbSeason.Id, tmdbSeason.ShowId);
                                        continue;
                                    }

                                    tmdbEntity ??= tmdbSeason;
                                    Logger.LogTrace("Found TMDB season {TmdbSeasonTitle} for Shoko Series {SeriesTitle}. (Series={SeriesId},Season={SeasonId},Show={ShowId})", tmdbSeason.Title, series.Name, primaryId, tmdbSeason.Id, tmdbSeason.ShowId);

                                    // If the Shoko Series is fully matched to more than one TMDB season that's not season zero, then switch to using the show instead.
                                    if (tmdbSeason.EpisodeCount == matchedEpisodeCount) {
                                        if (++fullyMatchedSeasons > 1)
                                            break;
                                        continue;
                                    }

                                    break;
                                }
                            }
                            if (tmdbEntity is not null && fullyMatchedSeasons > 1) {
                                if (await ApiClient.GetTmdbShowForSeason(((TmdbSeason)tmdbEntity).Id).ConfigureAwait(false) is { } tmdbShow) {
                                    tmdbEntity = tmdbShow;
                                    Logger.LogTrace("Found multiple TMDB seasons for Shoko Series {SeriesTitle}, so switched to show {ShowName} instead. (Series={SeriesId})", series.Name, tmdbShow.Title, primaryId);
                                }
                            }

                            if (tmdbEntity is not null)
                                break;
                        }
                    }

                    if (tmdbEntity is null && series.IDs.TMDB.Movie.Count > 0) {
                        Logger.LogTrace("Found {TmdbMovieCount} TMDB movies for Shoko Series {SeriesTitle} to pick a movie collection to use. (Series={SeriesId})", series.IDs.TMDB.Movie.Count, series.Name, primaryId);

                        var collectionIds = new List<int>();
                        foreach (var movieId in series.IDs.TMDB.Movie) {
                            if (await ApiClient.GetTmdbMovie(movieId.ToString()).ConfigureAwait(false) is not { } tmdbMovie ||
                                !tmdbMovie.CollectionId.HasValue)
                                continue;

                            collectionIds.Add(tmdbMovie.CollectionId.Value);
                        }

                        collectionIds = collectionIds
                            .GroupBy(x => x)
                            .OrderByDescending(x => x.Count())
                            .Select(x => x.Key)
                            .ToList();
                        foreach (var collectionId in collectionIds) {
                            if (await ApiClient.GetTmdbMovieCollection(collectionId.ToString()).ConfigureAwait(false) is { } tmdbCollection) {
                                tmdbEntity = tmdbCollection;
                                Logger.LogTrace("Found TMDB movie collection {TmdbCollectionTitle} for Shoko Series {SeriesTitle}. (Series={SeriesId},Collection={CollectionId})", tmdbCollection.Title, series.Name, primaryId, tmdbCollection.Id);
                                break;
                            }
                        }
                    }

                    if (tmdbEntity is null)
                        Logger.LogTrace("Could not find TMDB entity to use for Shoko Series {SeriesTitle}. (Series={SeriesId})", series.Name, primaryId);
                }

                SeasonInfo seasonInfo;
                if (extraIds.Count > 0) {
                    var detailsIds = extraIds.Prepend(primaryId).ToList();

                    // Create the tasks.
                    var relationsTasks = detailsIds.Select(id => ApiClient.GetRelationsForShokoSeries(id));
                    var seriesConfigurationsTasks = detailsIds.Select(id => GetSeriesConfiguration(id));

                    // Await the tasks in order.
                    var relations = (await Task.WhenAll(relationsTasks).ConfigureAwait(false))
                        .SelectMany(r => r)
                        .Where(r => r.RelatedIDs.Shoko.HasValue && !detailsIds.Contains(r.RelatedIDs.Shoko.Value.ToString()))
                        .ToList();
                    var seriesConfigurations = (await Task.WhenAll(seriesConfigurationsTasks).ConfigureAwait(false))
                        .Select((t, i) => (t, i))
                        .ToDictionary(t => detailsIds[t.i], (t) => t.t);

                    // Create the season info using the merged details.
                    seasonInfo = new SeasonInfo(ApiClient, series, extraIds, episodes, relations, tmdbEntity, seriesConfigurations);
                }
                else {
                    var relations = await ApiClient.GetRelationsForShokoSeries(primaryId).ConfigureAwait(false);
                    var seriesConfigurations = new Dictionary<string, SeriesConfiguration>() { { primaryId, await GetSeriesConfiguration(primaryId).ConfigureAwait(false) },
                    };
                    seasonInfo = new SeasonInfo(ApiClient, series, extraIds, episodes, relations, tmdbEntity, seriesConfigurations);
                }

                foreach (var episode in episodes)
                    EpisodeIdToSeasonIdDictionary.TryAdd(episode.Id, primaryId);

                return seasonInfo;
            }
        ).ConfigureAwait(false);
    }

    #endregion

    #region Series Merging

    public async Task<(string primaryId, List<string> extraIds)> GetSeriesIdsForShokoSeries(string seriesId) {
        if (await ApiClient.GetShokoSeries(seriesId).ConfigureAwait(false) is not { } shokoSeries)
            return (seriesId, []);

        return await GetSeriesIdsForSeason(shokoSeries).ConfigureAwait(false);
    }

    private Task<(string primaryId, List<string> extraIds)> GetSeriesIdsForSeason(ShokoSeries series)
        => DataCache.GetOrCreateAsync(
            $"season-series-ids:{series.Id}",
            (tuple) => {
                var config = Plugin.Instance.Configuration;
                if (!config.EXPERIMENTAL_MergeSeasons)
                    return;

                Logger.LogTrace("Reusing existing series-to-season mapping for series. (Series={SeriesId},ExtraSeries={ExtraIds})", tuple.primaryId, tuple.extraIds);
            },
            async () => {
                var primaryId = series.Id;
                var extraIds = new List<string>();
                var config = Plugin.Instance.Configuration;
                if (!config.EXPERIMENTAL_MergeSeasons)
                    return (primaryId, extraIds);

                Logger.LogTrace("Creating new series-to-season mapping for series. (Series={SeriesId})", primaryId);

                var seriesConfig = await GetSeriesConfiguration(series.Id).ConfigureAwait(false);
                if (seriesConfig.StructureType is not SeriesStructureType.Shoko_Groups)
                    return (primaryId, extraIds);

                if (seriesConfig.MergeOverride is SeriesMergingOverride.NoMerge)
                    return (primaryId, extraIds);

                if (!config.EXPERIMENTAL_MergeSeasonsTypes.Contains(seriesConfig.TypeOverride ?? series.AniDB.Type))
                    return (primaryId, extraIds);

                if (series.AniDB.AirDate is null)
                    return (primaryId, extraIds);

                // We potentially have a "follow-up" season candidate, so look for the "primary" season candidate, then jump into that.
                var relations = await ApiClient.GetRelationsForShokoSeries(primaryId).ConfigureAwait(false);
                var mainTitle = series.AniDB.Titles.First(title => title.Type == TitleType.Main).Value;
                var maxDaysThreshold = config.EXPERIMENTAL_MergeSeasonsMergeWindowInDays;
                var adjustedMainTitle = AdjustMainTitle(mainTitle) ?? mainTitle;
                var currentSeries = series;
                var currentDate = currentSeries.AniDB.AirDate.Value;
                var currentRelations = relations;
                var currentConfig = seriesConfig;
                var groupId = currentSeries.IDs.ParentGroup;
                while (currentRelations.Count > 0) {
                    foreach (
                        var prequelRelation in currentRelations
                            .Where(relation => relation.Type is RelationType.Prequel or RelationType.MainStory && relation.RelatedIDs.Shoko.HasValue)
                            .OrderBy(relation => relation.Type is RelationType.Prequel)
                            .ThenBy(relation => relation.Type)
                            .ThenBy(relation => relation.RelatedIDs.AniDB)
                    ) {
                        if (await ApiClient.GetShokoSeries(prequelRelation.RelatedIDs.Shoko!.Value.ToString()).ConfigureAwait(false) is not { } prequelSeries)
                            continue;

                        if (prequelSeries.IDs.ParentGroup != groupId)
                            continue;

                        var prequelConfig = await GetSeriesConfiguration(prequelSeries.Id).ConfigureAwait(false);
                        if (prequelConfig.MergeOverride is SeriesMergingOverride.NoMerge)
                            continue;

                        if (prequelConfig.StructureType is not SeriesStructureType.Shoko_Groups)
                            continue;

                        if (!config.EXPERIMENTAL_MergeSeasonsTypes.Contains(prequelConfig.TypeOverride ?? prequelSeries.AniDB.Type))
                            continue;

                        if (prequelSeries.AniDB.AirDate is not { } prequelDate || (prequelRelation.Type is RelationType.Prequel && prequelDate > currentDate))
                            continue;

                        var mergeOverride = (
                            prequelRelation.Type is RelationType.Prequel && (currentConfig.MergeOverride.HasFlag(SeriesMergingOverride.MergeBackward) || prequelConfig.MergeOverride.HasFlag(SeriesMergingOverride.MergeForward))
                        ) || (
                            prequelRelation.Type is RelationType.MainStory && currentConfig.MergeOverride.HasFlag(SeriesMergingOverride.MergeWithMainStory)
                        );
                        if (!mergeOverride) {
                            if (maxDaysThreshold is -1)
                                continue;

                            if (maxDaysThreshold > 0) {
                                var deltaDays = (int)Math.Floor((currentDate - prequelDate).TotalDays);
                                if (deltaDays > maxDaysThreshold)
                                    continue;
                            }
                        }

                        var prequelMainTitle = prequelSeries.AniDB.Titles.First(title => title.Type == TitleType.Main).Value;
                        var adjustedPrequelMainTitle = AdjustMainTitle(prequelMainTitle);
                        if (mergeOverride) {
                            adjustedMainTitle = adjustedPrequelMainTitle ?? prequelMainTitle;
                            currentSeries = prequelSeries;
                            currentDate = prequelDate;
                            currentRelations = await ApiClient.GetRelationsForShokoSeries(prequelSeries.Id).ConfigureAwait(false);
                            currentConfig = prequelConfig;
                            goto continuePrequelWhileLoop;
                        }

                        // We only want to merge main/side stories if the override is set.
                        if (prequelRelation.Type is RelationType.MainStory)
                            continue;

                        if (string.IsNullOrEmpty(adjustedPrequelMainTitle)) {
                            if (string.Equals(adjustedMainTitle, prequelMainTitle, StringComparison.InvariantCultureIgnoreCase)) {
                                currentSeries = prequelSeries;
                                currentDate = prequelDate;
                                currentRelations = await ApiClient.GetRelationsForShokoSeries(prequelSeries.Id).ConfigureAwait(false);
                                currentConfig = prequelConfig;
                                goto breakPrequelWhileLoop;
                            }
                            continue;
                        }

                        if (string.Equals(adjustedMainTitle, adjustedPrequelMainTitle, StringComparison.InvariantCultureIgnoreCase)) {
                            currentSeries = prequelSeries;
                            currentDate = prequelDate;
                            currentRelations = await ApiClient.GetRelationsForShokoSeries(prequelSeries.Id).ConfigureAwait(false);
                            currentConfig = prequelConfig;
                            goto continuePrequelWhileLoop;
                        }
                    }
                    breakPrequelWhileLoop: break;
                    continuePrequelWhileLoop: continue;
                }

                // If an earlier candidate was found, use its IDs instead. We re-run the method to
                // allow it to cache the IDs once for the forward search and re-use them across all
                // other seasons that perform a backward search.
                if (currentSeries != series) {
                    (primaryId, extraIds) = await GetSeriesIdsForSeason(currentSeries).ConfigureAwait(false);

                    // I don't want to duplicate the logging here and to use an else branch with
                    // more indention for the while loop, so using goto instead.
                    goto logAndReturn;
                }

                var storyStack = new Stack<(string adjustedMainTitle, DateTime currentDate, SeriesConfiguration currentConfig, IReadOnlyList<Relation> currentRelations, int relationOffset)>([
                    (adjustedMainTitle, currentDate, currentConfig, currentRelations, 0)
                ]);
                while (storyStack.Count > 0) {
                    (adjustedMainTitle, currentDate, currentConfig, currentRelations, var relationOffset) = storyStack.Pop();
                    while (currentRelations.Count > 0) {
                        foreach (
                            var sequelRelation in currentRelations
                                .Where(relation => relation.Type is RelationType.Sequel or RelationType.SideStory && relation.RelatedIDs.Shoko.HasValue)
                                .OrderBy(relation => relation.Type is RelationType.Sequel)
                                .ThenBy(relation => relation.Type)
                                .ThenBy(relation => relation.RelatedIDs.AniDB)
                                .Skip(relationOffset)
                        ) {
                            relationOffset++;
                            if (await ApiClient.GetShokoSeries(sequelRelation.RelatedIDs.Shoko!.Value.ToString()).ConfigureAwait(false) is not { } sequelSeries)
                                continue;

                            if (sequelSeries.IDs.ParentGroup != groupId)
                                continue;

                            var sequelConfig = await GetSeriesConfiguration(sequelSeries.Id).ConfigureAwait(false);
                            if (sequelConfig.MergeOverride is SeriesMergingOverride.NoMerge)
                                continue;

                            if (sequelConfig.StructureType is not SeriesStructureType.Shoko_Groups)
                                continue;

                            if (!config.EXPERIMENTAL_MergeSeasonsTypes.Contains(sequelConfig.TypeOverride ?? sequelSeries.AniDB.Type))
                                continue;

                            if (sequelSeries.AniDB.AirDate is not { } sequelDate || (sequelRelation.Type is RelationType.Sequel && sequelDate < currentDate))
                                continue;

                            var mergeOverride = (
                                sequelRelation.Type is RelationType.Sequel && (currentConfig.MergeOverride.HasFlag(SeriesMergingOverride.MergeForward) || sequelConfig.MergeOverride.HasFlag(SeriesMergingOverride.MergeBackward))
                            ) || (
                                sequelRelation.Type is RelationType.SideStory && sequelConfig.MergeOverride.HasFlag(SeriesMergingOverride.MergeWithMainStory)
                            );
                            if (!mergeOverride) {
                                if (maxDaysThreshold < 0)
                                    continue;

                                if (maxDaysThreshold > 0) {
                                    var deltaDays = (int)Math.Floor((sequelDate - currentDate).TotalDays);
                                    if (deltaDays > maxDaysThreshold)
                                        continue;
                                }
                            }

                            var sequelMainTitle = sequelSeries.AniDB.Titles.First(title => title.Type == TitleType.Main).Value;
                            var adjustedSequelMainTitle = AdjustMainTitle(sequelMainTitle);
                            if (mergeOverride) {
                                // If we're about to enter a side story, then push the main story on the stack at the next relation index.
                                if (sequelRelation.Type is RelationType.SideStory) 
                                    storyStack.Push((adjustedMainTitle, currentDate, currentConfig, currentRelations, relationOffset));

                                // Re-focus on the sequel when overriding.
                                adjustedMainTitle = adjustedSequelMainTitle ?? sequelMainTitle;
                                extraIds.Add(sequelSeries.Id);
                                currentDate = sequelDate;
                                currentRelations = await ApiClient.GetRelationsForShokoSeries(sequelSeries.Id).ConfigureAwait(false);
                                currentConfig = sequelConfig;
                                goto continueSequelWhileLoop;
                            }

                            // We only want to merge main/side stories if the override is set.
                            if (sequelRelation.Type is RelationType.SideStory)
                                continue;

                            if (string.IsNullOrEmpty(adjustedSequelMainTitle))
                                continue;

                            if (string.Equals(adjustedMainTitle, adjustedSequelMainTitle, StringComparison.InvariantCultureIgnoreCase)) {
                                extraIds.Add(sequelSeries.Id);
                                currentDate = sequelDate;
                                currentRelations = await ApiClient.GetRelationsForShokoSeries(sequelSeries.Id).ConfigureAwait(false);
                                currentConfig = sequelConfig;
                                goto continueSequelWhileLoop;
                            }
                        }
                        break;
                        continueSequelWhileLoop: continue;
                    }
                }

                logAndReturn:
                Logger.LogTrace("Created new series-to-season mapping for series. (Series={SeriesId},Primary={PrimaryId},ExtraSeries={ExtraIds})", series.Id, primaryId, extraIds);

                return (primaryId, extraIds);
            }
        );

    private string? AdjustMainTitle(string title)
        => YearRegex().Match(title) is { Success: true } result
            ? title[..^result.Length]
            : null;

    #endregion

    #region Season Id Helpers

    public bool TryGetSeasonIdForPath(string path, [NotNullWhen(true)] out string? seasonId) {
        if (string.IsNullOrEmpty(path)) {
            seasonId = null;
            return false;
        }

        // Fast path; using the lookup.
        if (PathToSeasonIdDictionary.TryGetValue(path, out seasonId))
            return true;

        // Slow path; getting the show from cache or remote and finding the season's series id.
        Logger.LogDebug("Trying to find the season's series id for {Path} using the slow path.", path);
        if (GetSeasonInfoByPath(path).ConfigureAwait(false).GetAwaiter().GetResult() is { } seasonInfo) {
            seasonId = seasonInfo.Id;
            return true;
        }

        seasonId = null;
        return false;
    }

    public bool TryGetSeasonIdForEpisodeId(string episodeId, [NotNullWhen(true)] out string? seasonId) {
        if (string.IsNullOrEmpty(episodeId)) {
            seasonId = null;
            return false;
        }

        // Fast path; using the lookup.
        if (EpisodeIdToSeasonIdDictionary.TryGetValue(episodeId, out seasonId))
            return true;

        // Slow path; asking the http client to get the series from remote to look up it's id.
        Logger.LogDebug("Trying to find episode ids using the slow path. (Episode={EpisodeId})", episodeId);
        switch (episodeId[0]) {
            case IdPrefix.TmdbShow:
                if (ApiClient.GetTmdbSeasonForTmdbEpisode(episodeId[1..]).ConfigureAwait(false).GetAwaiter().GetResult() is not { } tmdbSeason) {
                    seasonId = null;
                    return false;
                }

                seasonId = IdPrefix.TmdbShow + tmdbSeason.Id;
                return true;

            case IdPrefix.TmdbMovie:
                seasonId = episodeId;
                return true;

            default:
                if (ApiClient.GetShokoSeriesForShokoEpisode(episodeId).ConfigureAwait(false).GetAwaiter().GetResult() is not { } series) {
                    seasonId = null;
                    return false;
                }

                seasonId = series.Id;
                return true;
        }
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"Season (?<seasonNumber>\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled, "en-US")]
    private static partial Regex SeasonNameRegex();

    private async Task<string?> GetSeasonIdForPath(string path) {
        // Reuse cached value.
        if (PathToSeasonIdDictionary.TryGetValue(path, out var seasonId))
            return seasonId;

        // Fast-path for VFS.
        if (path.StartsWith(Plugin.Instance.VirtualRoot + Path.DirectorySeparatorChar)) {
            var fileName = Path.GetFileName(path);
            var seasonNumberResult = SeasonNameRegex().Match(fileName);
            if (seasonNumberResult.Success)
                fileName = Path.GetFileName(Path.GetDirectoryName(path)!);

            if (!fileName.TryGetAttributeValue(ShokoSeriesId.Name, out seasonId))
                return null;

            if (seasonNumberResult.Success) {
                var seasonNumber = int.Parse(seasonNumberResult.Groups["seasonNumber"].Value);
                var showInfo = await GetShowInfoBySeasonId(seasonId).ConfigureAwait(false);
                if (showInfo == null)
                    return null;

                var seasonInfo = showInfo.GetSeasonInfoBySeasonNumber(seasonNumber);
                if (seasonInfo == null)
                    return null;

                seasonId = seasonInfo.Id;
            }

            PathToSeasonIdDictionary[path] = seasonId;
            return seasonId;
        }

        var partialPath = StripMediaFolder(path);
        Logger.LogDebug("Looking for shoko series matching path {Path}", partialPath);
        var result = await ApiClient.GetShokoSeriesForDirectory(partialPath).ConfigureAwait(false);
        Logger.LogTrace("Found {Count} matches for path {Path}", result.Count, partialPath);

        // Return the first match where the series unique paths partially match
        // the input path.
        foreach (var series in result) {
            if (await GetSeasonInfosForShokoSeries(series.Id).ConfigureAwait(false) is not { Count: > 0 } seasonInfoList)
                continue;

            var seasonInfo = seasonInfoList[0];
            var pathSet = await GetPathSetForSeries(series.Id).ConfigureAwait(false);
            foreach (var uniquePath in pathSet) {
                // Remove the trailing slash before matching.
                if (!uniquePath[..^1].EndsWith(partialPath))
                    goto continueForeach;

                PathToSeasonIdDictionary[path] = seasonInfo.Id;
            }

            return seasonInfo.Id;
            continueForeach: continue;
        }

        // In the edge case for series with only files with multiple
        // cross-references we just return the first match.
        if (result.Count > 0) {
            if (await GetSeasonInfosForShokoSeries(result[0].Id).ConfigureAwait(false) is not { Count: > 0 } seasonInfoList)
                return null;

            return seasonInfoList[0].Id;
        }

        return null;
    }

    #endregion

    #region Show Info

    public async Task<ShowInfo?> GetShowInfoByPath(string path) {
        if (!PathToSeasonIdDictionary.TryGetValue(path, out var seasonId)) {
            seasonId = await GetSeasonIdForPath(path).ConfigureAwait(false);
            if (string.IsNullOrEmpty(seasonId))
                return null;
        }

        return await GetShowInfoBySeasonId(seasonId).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ShowInfo>> GetShowInfosForShokoSeries(string seriesId) {
        if (await GetSeasonInfosForShokoSeries(seriesId).ConfigureAwait(false) is not { Count: > 0 } seasonInfoList)
            return [];

        var showInfoList = await Task.WhenAll(seasonInfoList.Select(seasonInfo => GetShowInfoBySeasonId(seasonInfo.Id))).ConfigureAwait(false);
        return showInfoList
            .OfType<ShowInfo>()
            .DistinctBy(showInfo => showInfo.Id)
            .ToList();
    }

    public async Task<ShowInfo?> GetShowInfoBySeasonId(string seasonId) {
        if (string.IsNullOrEmpty(seasonId))
            return null;

        if (seasonId[0] is IdPrefix.TmdbShow) {
            if (await ApiClient.GetTmdbShowForSeason(seasonId[1..]).ConfigureAwait(false) is not { } tmdbShow)
                return null;

            return await CreateShowInfo(tmdbShow).ConfigureAwait(false);
        }
        else if (seasonId[0] is IdPrefix.TmdbMovie) {
            if (await ApiClient.GetTmdbMovie(seasonId[1..]).ConfigureAwait(false) is not { } tmdbMovie)
                return null;

            if (!tmdbMovie.CollectionId.HasValue)
                return await CreateShowInfoForTmdbMovie(tmdbMovie).ConfigureAwait(false);

            if (await ApiClient.GetTmdbMovieCollection(tmdbMovie.CollectionId.Value.ToString()).ConfigureAwait(false) is not { } tmdbMovieCollection)
                return await CreateShowInfoForTmdbMovie(tmdbMovie).ConfigureAwait(false);

            return await CreateShowInfoForTmdbMovieCollection(tmdbMovieCollection).ConfigureAwait(false);
        }
        else if (seasonId[0] is IdPrefix.TmdbMovieCollection) {
            if (await ApiClient.GetTmdbMovieCollection(seasonId[1..]).ConfigureAwait(false) is not { } tmdbMovieCollection)
                return null;

            return await CreateShowInfoForTmdbMovieCollection(tmdbMovieCollection, true).ConfigureAwait(false);
        }

        var seasonInfo = await GetSeasonInfo(seasonId).ConfigureAwait(false);
        if (seasonInfo == null)
            return null;

        // Create a standalone group if grouping is disabled and/or for each series in a group with sub-groups.
        var seriesConfig = await GetSeriesConfiguration(seasonId).ConfigureAwait(false);
        if (seriesConfig.StructureType is not SeriesStructureType.Shoko_Groups)
            return CreateShowInfoForShokoSeries(seasonInfo);

        var group = await ApiClient.GetShokoGroupForShokoSeries(seasonId).ConfigureAwait(false);
        if (group == null)
            return null;

        // Create a standalone group if grouping is disabled and/or for each series in a group with sub-groups.
        if (group.Sizes.SubGroups > 0)
            return CreateShowInfoForShokoSeries(seasonInfo);

        // If we found a movie, and we're assigning movies as stand-alone shows, and we didn't create a stand-alone show
        // above, then attach the stand-alone show to the parent group of the group that might otherwise
        // contain the movie.
        if (seasonInfo.Type == SeriesType.Movie && Plugin.Instance.Configuration.SeparateMovies)
            return CreateShowInfoForShokoSeries(seasonInfo, group.Size > 0 ? group.IDs.ParentGroup?.ToString() : null);

        return await CreateShowInfoForShokoGroup(group, group.Id).ConfigureAwait(false);
    }

    private Task<ShowInfo> CreateShowInfo(TmdbShow tmdbShow)
        => DataCache.GetOrCreateAsync(
            $"show:by-tmdb-show-id:{tmdbShow.Id}",
            (showInfo) => Logger.LogTrace("Reusing info object for show {ShowName}. (Source=TMDB,Show={ShowId})", showInfo?.Title, tmdbShow.Id),
            async () => {
                Logger.LogTrace("Creating info object for show {ShowName}. (Source=TMDB,Show={ShowId})", tmdbShow.Title, tmdbShow.Id);
                var seasonsInShow = await ApiClient.GetTmdbSeasonsInTmdbShow(tmdbShow.Id.ToString()).ConfigureAwait(false);
                var seasonList = (await Task.WhenAll(seasonsInShow.Select(season => CreateSeasonInfo(season, tmdbShow))).ConfigureAwait(false))
                    .ToList();
                var showInfo = new ShowInfo(ApiClient, tmdbShow, seasonList);

                foreach (var seasonInfo in seasonList)
                    SeasonIdToShowIdDictionary[seasonInfo.Id] = showInfo.Id;

                return showInfo;
            }
    );

    private Task<ShowInfo> CreateShowInfoForTmdbMovieCollection(TmdbMovieCollection tmdbMovieCollection, bool singleSeasonMode = false)
        => DataCache.GetOrCreateAsync(
            $"show:by-tmdb-movie-collection-id:{tmdbMovieCollection.Id}:{singleSeasonMode}",
            (showInfo) => Logger.LogTrace("Reusing info object for show {ShowName}. (Source=TMDB,MovieCollection={MovieCollectionId},SingleSeasonMode={SingleSeasonMode})", showInfo?.Title, tmdbMovieCollection.Id, singleSeasonMode),
            async () => {
                Logger.LogTrace("Creating info object for show {ShowName}. (Source=TMDB,MovieCollection={MovieCollectionId},SingleSeasonMode={SingleSeasonMode})", tmdbMovieCollection.Title, tmdbMovieCollection.Id, singleSeasonMode);

                var moviesInCollection = await ApiClient.GetTmdbMoviesInMovieCollection(tmdbMovieCollection.Id.ToString()).ConfigureAwait(false);
                var seasonList = singleSeasonMode
                    ? [await CreateSeasonInfo(tmdbMovieCollection).ConfigureAwait(false)]
                    : await Task.WhenAll(moviesInCollection.Select(CreateSeasonInfo)).ConfigureAwait(false);
                var showInfo = new ShowInfo(ApiClient, tmdbMovieCollection, seasonList);

                foreach (var seasonInfo in seasonList)
                    SeasonIdToShowIdDictionary[seasonInfo.Id] = showInfo.Id;

                return showInfo;
            }
    );

    private Task<ShowInfo> CreateShowInfoForTmdbMovie(TmdbMovie tmdbMovie)
        => DataCache.GetOrCreateAsync(
            $"show:by-tmdb-movie-id:{tmdbMovie.Id}",
            (showInfo) => Logger.LogTrace("Reusing info object for show {ShowName}. (Source=TMDB,Movie={MovieId})", showInfo?.Title, tmdbMovie.Id),
            async () => {
                Logger.LogTrace("Creating info object for show {ShowName}. (Source=TMDB,Movie={MovieId})", tmdbMovie.Title, tmdbMovie.Id);

                var seasonInfo = await CreateSeasonInfo(tmdbMovie).ConfigureAwait(false);
                var showInfo = new ShowInfo(ApiClient, tmdbMovie, seasonInfo);

                SeasonIdToShowIdDictionary[seasonInfo.Id] = showInfo.Id;

                return showInfo;
            }
    );

    private Task<ShowInfo?> CreateShowInfoForShokoGroup(ShokoGroup group, string groupId)
        => DataCache.GetOrCreateAsync(
            $"show:by-group-id:{groupId}",
            (showInfo) => Logger.LogTrace("Reusing info object for show {GroupName}. (Source=Shoko,Group={GroupId})", showInfo?.Title, groupId),
            async () => {
                Logger.LogTrace("Creating info object for show {GroupName}. (Source=Shoko,Group={GroupId})", group.Name, groupId);

                var seriesInGroup = await ApiClient.GetShokoSeriesInGroup(groupId).ConfigureAwait(false);
                var seasonList = (await Task.WhenAll(seriesInGroup.Select(CreateSeasonInfo)).ConfigureAwait(false))
                    .DistinctBy(seasonInfo => seasonInfo.Id)
                    .ToList();
                var length = seasonList.Count;

                seasonList = seasonList
                    .Where(s => s.StructureType is SeriesStructureType.Shoko_Groups)
                    .ToList();

                if (Plugin.Instance.Configuration.SeparateMovies)
                    seasonList = seasonList.Where(s => s.Type is not SeriesType.Movie).ToList();

                // Return early if no series matched the filter or if the list was empty.
                if (seasonList.Count == 0) {
                    Logger.LogWarning("Creating an empty show info for filter! (Source=Shoko,Group={GroupId})", groupId);

                    return null;
                }

                var tmdbEntities = new List<ITmdbEntity>();
                foreach (var seasonInfo in seasonList) {
                    if (!string.IsNullOrEmpty(seasonInfo.TmdbSeasonId)) {
                        Logger.LogTrace("Fetching TMDB show for Shoko Series {SeriesName}. (Series={SeriesId},Show={ShowId})", seasonInfo.Title, seasonInfo.Id, seasonInfo.TmdbSeasonId);

                        if (await ApiClient.GetTmdbShowForSeason(seasonInfo.TmdbSeasonId).ConfigureAwait(false) is not { } tmdbShow) {
                            Logger.LogTrace("Failed to fetch TMDB show for Shoko Series {SeriesName}. (Series={SeriesId},Show={ShowId})", seasonInfo.Title, seasonInfo.Id, seasonInfo.TmdbSeasonId);
                            continue;
                        }

                        tmdbEntities.Add(tmdbShow);
                    }

                    if (!string.IsNullOrEmpty(seasonInfo.TmdbMovieCollectionId)) {
                        Logger.LogTrace("Fetching TMDB movie collection for Shoko Series {SeriesName}. (Series={SeriesId},Show={ShowId})", seasonInfo.Title, seasonInfo.Id, seasonInfo.TmdbMovieCollectionId);

                        if (await ApiClient.GetTmdbMovieCollection(seasonInfo.TmdbMovieCollectionId).ConfigureAwait(false) is not { } tmdbMovieCollection) {
                            Logger.LogTrace("Failed to fetch TMDB movie collection for Shoko Series {SeriesName}. (Series={SeriesId},Show={ShowId})", seasonInfo.Title, seasonInfo.Id, seasonInfo.TmdbMovieCollectionId);
                            continue;
                        }

                        tmdbEntities.Add(tmdbMovieCollection);
                    }
                }
                var tmdbEntity = tmdbEntities
                    .GroupBy(x => (x.Kind, x.Id))
                    .OrderByDescending(x => x.Count())
                    .Select(x => x.First())
                    .FirstOrDefault();
                if (tmdbEntity is not null)
                    Logger.LogTrace("Found TMDB show for group {GroupName}. (Group={GroupId},Show={ShowId})", group.Name, groupId, tmdbEntity.Id);

                var showInfo = new ShowInfo(ApiClient, Logger, group, seasonList, tmdbEntity, length != seasonList.Count);

                foreach (var seasonInfo in seasonList)
                    SeasonIdToShowIdDictionary[seasonInfo.Id] = showInfo.Id;

                return showInfo;
            }
        );

    private ShowInfo CreateShowInfoForShokoSeries(SeasonInfo seasonInfo, string? collectionId = null)
        => DataCache.GetOrCreate(
            $"show:by-series-id:{seasonInfo.Id}",
            (showInfo) => Logger.LogTrace("Reusing info object for show {GroupName}. (Source=Shoko,Series={SeriesId})", showInfo.Title, seasonInfo.Id),
            () => {
                Logger.LogTrace("Creating info object for show {SeriesName}. (Source=Shoko,Series={SeriesId})", seasonInfo.Title, seasonInfo.Id);

                var showInfo = new ShowInfo(ApiClient, seasonInfo, collectionId);

                SeasonIdToShowIdDictionary[seasonInfo.Id] = showInfo.Id;

                return showInfo;
            }
        );

    #endregion

    #region Show Id Helper

    public bool TryGetShowIdForSeasonId(string seasonId, [NotNullWhen(true)] out string? showId) {
        if (string.IsNullOrEmpty(seasonId)) {
            showId = null;
            return false;
        }

        // Fast path; using the lookup.
        if (SeasonIdToShowIdDictionary.TryGetValue(seasonId, out showId))
            return true;

        // Slow path; getting the show from cache or remote and finding the show id.
        Logger.LogDebug("Trying to find the show id for season using the slow path. (Season={SeasonId})", seasonId);
        if (GetShowInfoBySeasonId(seasonId).ConfigureAwait(false).GetAwaiter().GetResult() is { } showInfo) {
            showId = showInfo.Id;
            return true;
        }

        showId = null;
        return false;
    }

    #endregion

    #region Collection Info

    public async Task<CollectionInfo?> GetCollectionInfo(string collectionId) {
        if (string.IsNullOrEmpty(collectionId))
            return null;

        if (DataCache.TryGetValue<CollectionInfo>($"collection:{collectionId}", out var collectionInfo)) {
            Logger.LogTrace("Reusing info object for collection {GroupName}. (Group={GroupId})", collectionInfo.Title, collectionId);
            return collectionInfo;
        }

        if (await ApiClient.GetShokoGroup(collectionId).ConfigureAwait(false) is not { } group)
            return null;

        return await CreateCollectionInfo(group, collectionId).ConfigureAwait(false);
    }

    private Task<CollectionInfo> CreateCollectionInfo(ShokoGroup group, string groupId)
        => DataCache.GetOrCreateAsync(
            $"collection:{groupId}",
            (collectionInfo) => Logger.LogTrace("Reusing info object for collection {GroupName}. (Group={GroupId})", collectionInfo.Title, groupId),
            async () => {
                Logger.LogTrace("Creating info object for collection {GroupName}. (Group={GroupId})", group.Name, groupId);
                Logger.LogTrace("Fetching show info objects for collection {GroupName}. (Group={GroupId})", group.Name, groupId);

                var showGroupIds = new HashSet<string>();
                var collectionIds = new HashSet<string>();
                var showDict = new Dictionary<string, ShowInfo>();
                foreach (var series in await ApiClient.GetShokoSeriesInGroup(groupId, recursive: true).ConfigureAwait(false)) {
                    foreach (var showInfo in await GetShowInfosForShokoSeries(series.Id).ConfigureAwait(false)) {
                        if (showInfo == null)
                            continue;

                        if (!string.IsNullOrEmpty(showInfo.ShokoGroupId))
                            showGroupIds.Add(showInfo.ShokoGroupId);

                        if (string.IsNullOrEmpty(showInfo.CollectionId))
                            continue;

                        collectionIds.Add(showInfo.CollectionId);
                        if (showInfo.CollectionId == groupId)
                            showDict.TryAdd(showInfo.Id, showInfo);
                    }
                }

                var groupList = new List<CollectionInfo>();
                if (group.Sizes.SubGroups > 0) {
                    Logger.LogTrace("Fetching sub-collection info objects for collection {GroupName}. (Group={GroupId})", group.Name, groupId);
                    foreach (var subGroup in await ApiClient.GetShokoGroupsInShokoGroup(groupId).ConfigureAwait(false)) {
                        if (showGroupIds.Contains(subGroup.Id) && !collectionIds.Contains(subGroup.Id))
                            continue;

                        var subCollectionInfo = await CreateCollectionInfo(subGroup, subGroup.Id).ConfigureAwait(false);
                        if (subCollectionInfo.FileCount > 0)
                            groupList.Add(subCollectionInfo);
                    }
                }

                var showInfoList = await GetShowInfosForShokoSeries(group.IDs.MainSeries.ToString()).ConfigureAwait(false);
                var mainShowId = showInfoList is { Count: > 0 } ? showInfoList[0].Id : null;
                var showList = showDict.Values.ToList();
                if (
                    await ApiClient.GetShokoSeries(group.IDs.MainSeries.ToString()).ConfigureAwait(false) is { } mainSeries &&
                    group.Name == mainSeries.Name &&
                    group.Description == mainSeries.AniDB.Description
                ) {
                    Logger.LogTrace("Finalizing info object for collection {GroupName}. (MainSeries={MainSeriesId},Group={GroupId})", group.Name, mainSeries.IDs.Shoko.ToString(), groupId);
                    return new CollectionInfo(group, mainSeries, mainShowId, showList, groupList);
                }

                Logger.LogTrace("Finalizing info object for collection {GroupName}. (Group={GroupId})", group.Name, groupId);
                return new CollectionInfo(group, mainShowId, showList, groupList);
            }
        );

    #endregion
}
