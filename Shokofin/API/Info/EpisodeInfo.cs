using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Shokofin.API.Models;
using Shokofin.API.Models.AniDB;
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

    public TimeSpan Runtime;

    public DateTime? AiredAt;

    public string DefaultTitle;

    public IReadOnlyList<Title> Titles;

    public string DefaultOverview;

    public IReadOnlyList<TextOverview> Overviews;

    public int FileCount { get; set; }

    public Rating OfficialRating;

    public List<CrossReference.EpisodeCrossReferenceIDs> CrossReferences;

    public EpisodeInfo(Episode episode)
    {
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
        Titles = episode.AniDB.Titles;
        DefaultOverview = episode.Description;
        Overviews = [
            new TextOverview() {
                IsDefault = true,
                IsPreferred = string.Equals(episode.Description, episode.AniDB.Description),
                LanguageCode = "en",
                Source = "AniDB",
                Value = episode.AniDB.Description,
            },
        ];
        FileCount = episode.Size;
        OfficialRating = episode.AniDB.Rating;
        CrossReferences = episode.CrossReferences;
    }
}
