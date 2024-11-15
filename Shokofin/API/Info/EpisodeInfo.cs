using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Shokofin.API.Models;
using Shokofin.API.Models.TMDB;
using Shokofin.Configuration;
using Shokofin.Utils;

namespace Shokofin.API.Info;

public class EpisodeInfo
{
    public string Id;

    public string SeriesId;

    public string? AnidbId;

    public string? TmdbId;

    public string? TvdbId;

    public SeriesStructureType StructureType;

    public EpisodeType Type;

    public int? SeasonNumber;

    public int EpisodeNumber;

    public bool IsHidden;

    public MediaBrowser.Model.Entities.ExtraType? ExtraType;

    public TimeSpan? Runtime;

    public DateTime? AiredAt;

    public string DefaultTitle;

    public IReadOnlyList<Title> Titles;

    public string DefaultOverview;

    public IReadOnlyList<TextOverview> Overviews;

    public int FileCount { get; set; }

    public Rating OfficialRating;

    public List<CrossReference.EpisodeCrossReferenceIDs> CrossReferences;

    public EpisodeInfo(Episode episode, IReadOnlyList<TmdbEpisode> tmdbEpisodes)
    {
        var tmdbEpisode = tmdbEpisodes
            .OrderBy(e => e.ShowId)
            .ThenBy(e => e.SeasonNumber)
            .ThenBy(e => e.EpisodeNumber)
            .FirstOrDefault();
        Id = episode.IDs.Shoko.ToString();
        SeriesId = episode.IDs.ParentSeries.ToString();
        AnidbId = episode.AniDB.Id.ToString();
        StructureType = SeriesStructureType.Shoko_Groups;
        Type = episode.AniDB.Type;
        SeasonNumber = null;
        EpisodeNumber = episode.AniDB.EpisodeNumber;
        IsHidden = episode.IsHidden;
        ExtraType = Ordering.GetExtraType(episode.AniDB);
        Runtime = episode.AniDB.Duration;
        AiredAt = episode.AniDB.AirDate;
        DefaultTitle = episode.Name;
        Titles = [
            ..episode.AniDB.Titles,
            ..(tmdbEpisode is not null ? tmdbEpisode.Titles : []),
        ];
        DefaultOverview = episode.Description;
        Overviews = [
            new TextOverview() {
                IsDefault = true,
                IsPreferred = string.Equals(episode.Description, episode.AniDB.Description),
                LanguageCode = "en",
                Source = "AniDB",
                Value = episode.AniDB.Description,
            },
            ..(tmdbEpisode is not null ? tmdbEpisode.Overviews : []),
        ];
        FileCount = episode.Size;
        OfficialRating = episode.AniDB.Rating;
        CrossReferences = episode.CrossReferences;
    }

    public EpisodeInfo(TmdbEpisode episode)
    {
        Id = episode.Id.ToString();
        SeriesId = episode.ShowId.ToString();
        TmdbId = episode.Id.ToString();
        StructureType = SeriesStructureType.TMDB_SeriesAndMovies;
        Type = episode.SeasonNumber is 0 ? EpisodeType.Special : EpisodeType.Normal;
        SeasonNumber = episode.SeasonNumber;
        EpisodeNumber = episode.EpisodeNumber;
        Runtime = episode.Runtime;
        AiredAt = episode.AiredAt?.ToDateTime(TimeOnly.ParseExact("00:00:00.000000", "en-US", CultureInfo.InvariantCulture), DateTimeKind.Utc);
        DefaultTitle = episode.Title;
        Titles = episode.Titles;
        DefaultOverview = episode.Overview;
        Overviews = episode.Overviews;
        FileCount = episode.FileCrossReferences.Sum(a => a.Episodes.Count);
        OfficialRating = episode.UserRating;
        CrossReferences = episode.FileCrossReferences
            .SelectMany(a => a.Episodes)
            .ToList();
    }
}
