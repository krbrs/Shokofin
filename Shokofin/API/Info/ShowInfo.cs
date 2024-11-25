using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using Microsoft.Extensions.Logging;
using Shokofin.API.Models;
using Shokofin.API.Models.Shoko;
using Shokofin.API.Models.TMDB;
using Shokofin.Events.Interfaces;
using Shokofin.ExternalIds;
using Shokofin.Utils;

using ContentRating = Shokofin.API.Models.ContentRating;
using ContentRatingUtil = Shokofin.Utils.ContentRating;

namespace Shokofin.API.Info;

public class ShowInfo : IExtendedItemInfo {
    private readonly ShokoApiClient _client;

    public string Id { get; init; }

    public string InternalId => ShokoInternalId.Namespace + Id;

    public string? AnidbId { get; init; }

    public string? TmdbId { get; init; }

    public string? TvdbId { get; init; }

    /// <summary>
    /// Main Shoko Series Id.
    /// </summary>
    public string? ShokoSeriesId { get; init; }

    /// <summary>
    /// Main Shoko Group Id.
    /// </summary>
    public string? ShokoGroupId { get; init; }

    /// <summary>
    /// Shoko Group Id used for Collection Support.
    /// </summary>
    public string? CollectionId { get; init; }

    public string DefaultTitle { get; init; }

    public IReadOnlyList<Title> Titles { get; init; }

    public string? DefaultOverview { get; init; }

    public IReadOnlyList<TextOverview> Overviews { get; init; }

    public string? OriginalLanguageCode { get; init; }

    /// <summary>
    /// Indicates that this show is consistent of only movies.
    /// </summary>
    public bool IsMovieCollection { get; init; }

    /// <summary>
    /// Indicates this is a standalone show without a group attached to it.
    /// </summary>
    public bool IsStandalone { get; init; }

    /// <summary>
    /// First premiere date of the show.
    /// </summary>
    public DateTime? PremiereDate { get; init; }

    /// <summary>
    /// Ended date of the show.
    /// </summary>
    public DateTime? EndDate { get; init; }

    /// <summary>
    /// Custom rating of the show.
    /// </summary>
    public string? CustomRating =>
        DefaultSeason.IsRestricted ? "XXX" : null;

    /// <summary>
    /// Overall community rating of the show.
    /// </summary>
    public float CommunityRating =>
        (float)(SeasonList.Aggregate(0f, (total, seasonInfo) => total + seasonInfo.CommunityRating.ToFloat(10)) / SeasonList.Count);

    /// <summary>
    /// All tags from across all seasons.
    /// </summary>
    public IReadOnlyList<string> Tags { get; init; }

    /// <summary>
    /// All genres from across all seasons.
    /// </summary>
    public IReadOnlyList<string> Genres { get; init; }

    /// <summary>
    /// All production locations from across all seasons.
    /// </summary>
    public IReadOnlyDictionary<ProviderName, IReadOnlyList<string>> ProductionLocations { get; init; }

    public IReadOnlyList<ContentRating> ContentRatings { get; init; }

    /// <summary>
    /// All studios from across all seasons.
    /// </summary>
    public IReadOnlyList<string> Studios { get; init; }

    /// <summary>
    /// All staff from across all seasons.
    /// </summary>
    public IReadOnlyList<PersonInfo> Staff { get; init; }

    /// <summary>
    /// All seasons.
    /// </summary>
    public IReadOnlyList<SeasonInfo> SeasonList { get; init; }

    /// <summary>
    /// The season order dictionary.
    /// </summary>
    public IReadOnlyDictionary<int, SeasonInfo> SeasonOrderDictionary { get; init; }

    /// <summary>
    /// A pre-filtered set of special episode ids without an ExtraType
    /// attached.
    /// </summary>
    public IReadOnlyDictionary<string, bool> SpecialsDict { get; init; }

    /// <summary>
    /// The season number base-number dictionary.
    /// </summary>
    private Dictionary<string, int> SeasonNumberBaseDictionary { get; init; }

    /// <summary>
    /// Indicates that the show has specials.
    /// </summary>
    public bool HasSpecials =>
        SpecialsDict.Count > 0;

    /// <summary>
    /// Indicates that the show has specials with files.
    /// </summary>
    public bool HasSpecialsWithFiles =>
        SpecialsDict.Values.Contains(true);

    /// <summary>
    /// The default season for the show.
    /// </summary>
    public readonly SeasonInfo DefaultSeason;

    /// <summary>
    /// Episode number padding for file name generation.
    /// </summary>
    public readonly int EpisodePadding;

    public ShowInfo(ShokoApiClient client, SeasonInfo seasonInfo, string? collectionId = null) {
        var seasonNumberBaseDictionary = new Dictionary<string, int>();
        var seasonOrderDictionary = new Dictionary<int, SeasonInfo>();
        var seasonNumberOffset = 1;
        if (seasonInfo.EpisodeList.Count > 0 || seasonInfo.AlternateEpisodesList.Count > 0)
            seasonNumberBaseDictionary.Add(seasonInfo.Id, seasonNumberOffset);
        if (seasonInfo.EpisodeList.Count > 0)
            seasonOrderDictionary.Add(seasonNumberOffset++, seasonInfo);
        if (seasonInfo.AlternateEpisodesList.Count > 0)
            seasonOrderDictionary.Add(seasonNumberOffset++, seasonInfo);

        _client = client;
        Id = seasonInfo.Id;
        ShokoGroupId = seasonInfo.ShokoGroupId;
        CollectionId = collectionId ?? seasonInfo.ShokoGroupId;
        IsMovieCollection = seasonInfo.Type is SeriesType.Movie;
        IsStandalone = true;
        DefaultTitle = seasonInfo.DefaultTitle;
        Titles = seasonInfo.Titles;
        DefaultOverview = seasonInfo.DefaultOverview;
        Overviews = seasonInfo.Overviews;
        OriginalLanguageCode = seasonInfo.OriginalLanguageCode;
        Tags = seasonInfo.Tags;
        PremiereDate = seasonInfo.PremiereDate;
        EndDate = seasonInfo.EndDate;
        Genres = seasonInfo.Genres;
        ProductionLocations = seasonInfo.ProductionLocations;
        ContentRatings = seasonInfo.ContentRatings;
        Studios = seasonInfo.Studios;
        Staff = seasonInfo.Staff;
        SeasonList = [seasonInfo];
        SeasonNumberBaseDictionary = seasonNumberBaseDictionary;
        SeasonOrderDictionary = seasonOrderDictionary;
        SpecialsDict = seasonInfo.SpecialsList.ToDictionary(episodeInfo => episodeInfo.Id, episodeInfo => episodeInfo.IsAvailable);
        DefaultSeason = seasonInfo;
        EpisodePadding = Math.Max(2, (new int[] { seasonInfo.EpisodeList.Count, seasonInfo.AlternateEpisodesList.Count, seasonInfo.SpecialsList.Count }).Max().ToString().Length);
    }

    public ShowInfo(
        ShokoApiClient client,
        ILogger logger,
        ShokoGroup group,
        List<SeasonInfo> seasonList,
        ITmdbEntity? tmdbEntity,
        bool useGroupIdForCollection
    ) {
        var groupId = group.Id;

        // Order series list
        var orderingType = Plugin.Instance.Configuration.SeasonOrdering;
        switch (orderingType) {
            case Ordering.OrderType.Default:
                break;
            case Ordering.OrderType.ReleaseDate:
                seasonList = [.. seasonList.OrderBy(s => s?.PremiereDate ?? DateTime.MaxValue)];
                break;
            case Ordering.OrderType.Chronological:
            case Ordering.OrderType.ChronologicalIgnoreIndirect:
                seasonList.Sort(new SeriesInfoRelationComparer());
                break;
        }

        // Select the targeted id if a group specify a default series.
        var foundIndex = -1;
        switch (orderingType) {
            case Ordering.OrderType.ReleaseDate:
                foundIndex = 0;
                break;
            case Ordering.OrderType.Default:
            case Ordering.OrderType.Chronological:
            case Ordering.OrderType.ChronologicalIgnoreIndirect: {
                var targetId = group.IDs.MainSeries.ToString();
                foundIndex = seasonList.FindIndex(s => s.Id == targetId);
                break;
            }
        }

        // Fallback to the first series if we can't get a base point for seasons.
        if (foundIndex == -1) {
            logger.LogWarning("Unable to get a base-point for seasons within the group for the filter, so falling back to the first series in the group. This is most likely due to library separation being enabled. (Group={GroupID})", groupId);
            foundIndex = 0;
        }

        var defaultSeason = seasonList[foundIndex];
        var specialsSet = new Dictionary<string, bool>();
        var seasonOrderDictionary = new Dictionary<int, SeasonInfo>();
        var seasonNumberBaseDictionary = new Dictionary<string, int>();
        var seasonNumberOffset = 1;
        foreach (var seasonInfo in seasonList) {
            if (seasonInfo.EpisodeList.Count > 0 || seasonInfo.AlternateEpisodesList.Count > 0)
                seasonNumberBaseDictionary.Add(seasonInfo.Id, seasonNumberOffset);
            if (seasonInfo.EpisodeList.Count > 0)
                seasonOrderDictionary.Add(seasonNumberOffset++, seasonInfo);
            if (seasonInfo.AlternateEpisodesList.Count > 0)
                seasonOrderDictionary.Add(seasonNumberOffset++, seasonInfo);
            foreach (var episodeInfo in seasonInfo.SpecialsList)
                specialsSet.Add(episodeInfo.Id, episodeInfo.IsAvailable);
        }

        var anidbRating = ContentRatingUtil.GetCombinedAnidbContentRating(seasonOrderDictionary.Values);
        var contentRatings = seasonOrderDictionary.Values
            .SelectMany(sI => sI.ContentRatings)
            .Where(cR => cR.Source is not "AniDB")
            .Distinct()
            .ToList();
        if (!string.IsNullOrEmpty(anidbRating))
            contentRatings.Add(new() {
                Rating = anidbRating,
                Country = "US",
                Language = "en",
                Source = "AniDB",
            });

        _client = client;
        Id = defaultSeason.Id;
        ShokoGroupId = groupId;
        if (tmdbEntity is TmdbShow tmdbShow) {
            TmdbId = tmdbShow.Id.ToString();
            TvdbId = tmdbShow.TvdbId?.ToString();
        }
        DefaultTitle = group.Name;
        Titles = [
            ..defaultSeason.Titles.Where(t => t.Source is "AniDB"),
            ..(tmdbEntity?.Titles ?? []),
        ];
        DefaultOverview = Text.SanitizeAnidbDescription(group.Description) == defaultSeason.Overviews.FirstOrDefault(t => t.Source is "AniDB")?.Value
            ? Text.SanitizeAnidbDescription(group.Description)
            : group.Description;
        Overviews = [
            ..defaultSeason.Overviews.Where(t => t.Source is "AniDB"),
            ..(tmdbEntity?.Overviews ?? []),
        ];
        CollectionId = useGroupIdForCollection ? groupId : group.IDs.ParentGroup?.ToString();
        IsStandalone = false;
        PremiereDate = seasonList.Select(s => s.PremiereDate).Where(s => s.HasValue).Min();
        EndDate = !seasonList.Any(s => s.PremiereDate.HasValue && s.PremiereDate.Value < DateTime.Now && s.EndDate == null)
            ? seasonList.Select(s => s.EndDate).Where(s => s.HasValue).Max()
            : null;
        Genres = seasonList.SelectMany(s => s.Genres).Distinct().ToArray();
        Tags = seasonList.SelectMany(s => s.Tags).Distinct().ToArray();
        Studios = seasonList.SelectMany(s => s.Studios).Distinct().ToArray();
        ProductionLocations = seasonList
            .SelectMany(sI => sI.ProductionLocations)
            .GroupBy(kP => kP.Key, kP => kP.Value)
            .ToDictionary(gB => gB.Key, gB => gB.SelectMany(l => l).Distinct().ToList() as IReadOnlyList<string>);
        ContentRatings = contentRatings;
        Staff = seasonList.SelectMany(s => s.Staff).DistinctBy(p => new { p.Type, p.Name, p.Role }).ToArray();
        SeasonList = seasonList;
        SeasonNumberBaseDictionary = seasonNumberBaseDictionary;
        SeasonOrderDictionary = seasonOrderDictionary;
        SpecialsDict = specialsSet;
        DefaultSeason = defaultSeason;
        EpisodePadding = Math.Max(2, seasonList.SelectMany(s => new int[] { s.EpisodeList.Count, s.AlternateEpisodesList.Count }).Append(specialsSet.Count).Max().ToString().Length);
    }

    public ShowInfo(ShokoApiClient client, TmdbShow tmdbShow, IReadOnlyList<SeasonInfo> seasonList) {
        var defaultSeason = seasonList.First(s => s.EpisodeList.Any(e => e.SeasonNumber is 1));
        var specialsSet = new Dictionary<string, bool>();
        var seasonOrderDictionary = new Dictionary<int, SeasonInfo>();
        var seasonNumberBaseDictionary = new Dictionary<string, int>();
        var seasonNumberOffset = 1;
        foreach (var seasonInfo in seasonList) {
            if (seasonInfo.EpisodeList.Count > 0 || seasonInfo.AlternateEpisodesList.Count > 0)
                seasonNumberBaseDictionary.Add(seasonInfo.Id, seasonNumberOffset);
            if (seasonInfo.EpisodeList.Count > 0)
                seasonOrderDictionary.Add(seasonNumberOffset++, seasonInfo);
            if (seasonInfo.AlternateEpisodesList.Count > 0)
                seasonOrderDictionary.Add(seasonNumberOffset++, seasonInfo);
            foreach (var episodeInfo in seasonInfo.SpecialsList)
                specialsSet.Add(episodeInfo.Id, episodeInfo.IsAvailable);
        }

        if (seasonList.All(seasonInfo => !string.IsNullOrEmpty(seasonInfo.ShokoSeriesId))) {
            var shokoSeriesIdList = seasonList
                .GroupBy(s => s.ShokoSeriesId)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .ToList();
            if (shokoSeriesIdList.Count is 1)
                ShokoSeriesId = shokoSeriesIdList[0];
        }
        if (seasonList.All(seasonInfo => !string.IsNullOrEmpty(seasonInfo.ShokoGroupId))) {
            var shokoGroupIdList = seasonList
                .GroupBy(s => s.ShokoGroupId)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .ToList();
            if (shokoGroupIdList.Count is 1)
                ShokoGroupId = shokoGroupIdList[0];
        }
        else if (seasonList.All(seasonInfo => !string.IsNullOrEmpty(seasonInfo.TopLevelShokoGroupId))) {
            var shokoGroupIdList = seasonList
                .GroupBy(s => s.TopLevelShokoGroupId)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .ToList();
            if (shokoGroupIdList.Count is 1)
                ShokoGroupId = shokoGroupIdList[0];
        }

        _client = client;
        Id = defaultSeason.Id;
        CollectionId = ShokoGroupId;
        TmdbId = tmdbShow.Id.ToString();
        TvdbId = tmdbShow.TvdbId?.ToString();
        IsMovieCollection = false;
        IsStandalone = true;
        DefaultTitle = tmdbShow.Title;
        Titles = tmdbShow.Titles;
        DefaultOverview = tmdbShow.Overview;
        Overviews = tmdbShow.Overviews;
        OriginalLanguageCode = tmdbShow.OriginalLanguage;
        PremiereDate = tmdbShow.FirstAiredAt?.ToDateTime(TimeOnly.Parse("00:00:00", CultureInfo.InvariantCulture), DateTimeKind.Local);
        EndDate = tmdbShow.LastAiredAt?.ToDateTime(TimeOnly.Parse("00:00:00", CultureInfo.InvariantCulture), DateTimeKind.Local);
        Genres = seasonList.SelectMany(s => s.Genres).Distinct().ToArray();
        Tags = seasonList.SelectMany(s => s.Tags).Distinct().ToArray();
        Studios = seasonList.SelectMany(s => s.Studios).Distinct().ToArray();
        ProductionLocations = seasonList
            .SelectMany(sI => sI.ProductionLocations)
            .GroupBy(kP => kP.Key, kP => kP.Value)
            .ToDictionary(gB => gB.Key, gB => gB.SelectMany(l => l).Distinct().ToList() as IReadOnlyList<string>);
        ContentRatings = seasonList
            .SelectMany(sI => sI.ContentRatings)
            .Distinct()
            .ToList();
        Staff = seasonList.SelectMany(s => s.Staff).DistinctBy(p => new { p.Type, p.Name, p.Role }).ToArray();
        SeasonList = seasonList;
        SeasonNumberBaseDictionary = seasonNumberBaseDictionary;
        SeasonOrderDictionary = seasonOrderDictionary;
        SpecialsDict = specialsSet;
        DefaultSeason = defaultSeason;
        EpisodePadding = Math.Max(2, seasonList.SelectMany(s => new int[] { s.EpisodeList.Count, s.AlternateEpisodesList.Count }).Append(specialsSet.Count).Max().ToString().Length);
    }

    public ShowInfo(ShokoApiClient client, TmdbMovie tmdbMovie, SeasonInfo seasonInfo) {
        var releasedAt = tmdbMovie.ReleasedAt?.ToDateTime(TimeOnly.Parse("00:00:00", CultureInfo.InvariantCulture), DateTimeKind.Local);

        _client = client;
        Id = IdPrefix.TmdbMovie + tmdbMovie.Id.ToString();
        ShokoSeriesId = seasonInfo.ShokoSeriesId;
        ShokoGroupId = seasonInfo.ShokoGroupId ?? seasonInfo.TopLevelShokoGroupId;
        CollectionId = ShokoGroupId;
        IsMovieCollection = true;
        IsStandalone = true;
        DefaultTitle = tmdbMovie.Title;
        Titles = tmdbMovie.Titles;
        DefaultOverview = tmdbMovie.Overview;
        Overviews = tmdbMovie.Overviews;
        OriginalLanguageCode = tmdbMovie.OriginalLanguage;
        PremiereDate = releasedAt;
        EndDate = releasedAt < DateTime.Now ? releasedAt : null;
        Genres = seasonInfo.Genres;
        Tags = seasonInfo.Tags;
        Studios = seasonInfo.Studios;
        ProductionLocations = seasonInfo.ProductionLocations;
        ContentRatings = seasonInfo.ContentRatings;
        Staff = seasonInfo.Staff;
        SeasonList = [seasonInfo];
        SeasonNumberBaseDictionary = new Dictionary<string, int> { { seasonInfo.Id, 1 } };
        SeasonOrderDictionary = new Dictionary<int, SeasonInfo> { { 1, seasonInfo } };
        SpecialsDict = new Dictionary<string, bool>();
        DefaultSeason = seasonInfo;
        EpisodePadding = Math.Max(2, (new int[] { seasonInfo.EpisodeList.Count, seasonInfo.AlternateEpisodesList.Count, seasonInfo.SpecialsList.Count }).Max().ToString().Length);
    }

    public ShowInfo(ShokoApiClient client, TmdbMovieCollection tmdbMovieCollection, IReadOnlyList<SeasonInfo> seasonList) {
        var defaultSeason = seasonList[0];
        var specialsSet = new Dictionary<string, bool>();
        var seasonOrderDictionary = new Dictionary<int, SeasonInfo>();
        var seasonNumberBaseDictionary = new Dictionary<string, int>();
        var seasonNumberOffset = 1;
        foreach (var seasonInfo in seasonList) {
            if (seasonInfo.EpisodeList.Count > 0 || seasonInfo.AlternateEpisodesList.Count > 0)
                seasonNumberBaseDictionary.Add(seasonInfo.Id, seasonNumberOffset);
            if (seasonInfo.EpisodeList.Count > 0)
                seasonOrderDictionary.Add(seasonNumberOffset++, seasonInfo);
            if (seasonInfo.AlternateEpisodesList.Count > 0)
                seasonOrderDictionary.Add(seasonNumberOffset++, seasonInfo);
            foreach (var episodeInfo in seasonInfo.SpecialsList)
                specialsSet.Add(episodeInfo.Id, episodeInfo.IsAvailable);
        }

        if (seasonList.All(seasonInfo => !string.IsNullOrEmpty(seasonInfo.ShokoSeriesId))) {
            var shokoSeriesIdList = seasonList
                .GroupBy(s => s.ShokoSeriesId)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .ToList();
            if (shokoSeriesIdList.Count is 1)
                ShokoSeriesId = shokoSeriesIdList[0];
        }
        if (seasonList.All(seasonInfo => !string.IsNullOrEmpty(seasonInfo.ShokoGroupId))) {
            var shokoGroupIdList = seasonList
                .GroupBy(s => s.ShokoGroupId)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .ToList();
            if (shokoGroupIdList.Count is 1)
                ShokoGroupId = shokoGroupIdList[0];
        }
        else if (seasonList.All(seasonInfo => !string.IsNullOrEmpty(seasonInfo.TopLevelShokoGroupId))) {
            var shokoGroupIdList = seasonList
                .GroupBy(s => s.TopLevelShokoGroupId)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .ToList();
            if (shokoGroupIdList.Count is 1)
                ShokoGroupId = shokoGroupIdList[0];
        }

        _client = client;
        Id = defaultSeason.Id;
        CollectionId = ShokoGroupId;
        IsMovieCollection = seasonList.Count is 1;
        IsStandalone = false;
        DefaultTitle = tmdbMovieCollection.Title;
        Titles = tmdbMovieCollection.Titles;
        DefaultOverview = tmdbMovieCollection.Overview;
        Overviews = tmdbMovieCollection.Overviews;
        OriginalLanguageCode = defaultSeason.OriginalLanguageCode;
        Tags = seasonList.SelectMany(s => s.Tags).Distinct().ToArray();
        Genres = seasonList.SelectMany(s => s.Genres).Distinct().ToArray();
        ProductionLocations = seasonList
            .SelectMany(sI => sI.ProductionLocations)
            .GroupBy(kP => kP.Key, kP => kP.Value)
            .ToDictionary(gB => gB.Key, gB => gB.SelectMany(l => l).Distinct().ToList() as IReadOnlyList<string>);
        ContentRatings = seasonList
            .SelectMany(sI => sI.ContentRatings)
            .Distinct()
            .ToList();
        Studios = seasonList.SelectMany(s => s.Studios).Distinct().ToArray();
        Staff = seasonList.SelectMany(s => s.Staff).DistinctBy(p => new { p.Type, p.Name, p.Role }).ToArray();
        SeasonList = seasonList;
        SeasonNumberBaseDictionary = seasonNumberBaseDictionary;
        SeasonOrderDictionary = seasonOrderDictionary;
        SpecialsDict = specialsSet;
        DefaultSeason = defaultSeason;
        EpisodePadding = Math.Max(2, seasonList.SelectMany(s => new int[] { s.EpisodeList.Count, s.AlternateEpisodesList.Count }).Append(specialsSet.Count).Max().ToString().Length);
    }

    public async Task<Images> GetImages(CancellationToken cancellationToken)
        => Id[0] switch {
                IdPrefix.TmdbShow => await _client.GetImagesForTmdbShow(TmdbId!, cancellationToken).ConfigureAwait(false),
                IdPrefix.TmdbMovie => !string.IsNullOrEmpty(DefaultSeason.TmdbMovieCollectionId)
                    ? await _client.GetImagesForTmdbMovieCollection(DefaultSeason.TmdbMovieCollectionId, cancellationToken).ConfigureAwait(false)
                    : await _client.GetImagesForTmdbMovie(Id[1..], cancellationToken).ConfigureAwait(false),
                IdPrefix.TmdbMovieCollection => await _client.GetImagesForTmdbMovieCollection(Id[1..], cancellationToken).ConfigureAwait(false),
                _ => await _client.GetImagesForShokoSeries(Id, cancellationToken).ConfigureAwait(false),
            } ?? new();

    public bool IsSpecial(EpisodeInfo episodeInfo)
        => SpecialsDict.ContainsKey(episodeInfo.Id);

    public bool TryGetBaseSeasonNumberForSeasonInfo(SeasonInfo season, out int baseSeasonNumber)
        => SeasonNumberBaseDictionary.TryGetValue(season.Id, out baseSeasonNumber);

    public int GetBaseSeasonNumberForSeasonInfo(SeasonInfo season)
        => SeasonNumberBaseDictionary.TryGetValue(season.Id, out var baseSeasonNumber) ? baseSeasonNumber : 0;

    public SeasonInfo? GetSeasonInfoBySeasonNumber(int seasonNumber)
        => seasonNumber is > 0 && SeasonOrderDictionary.TryGetValue(seasonNumber, out var seasonInfo) ? seasonInfo : null;
}
