using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Entities;
using Shokofin.API.Models;
using Shokofin.API.Models.Shoko;
using Shokofin.API.Models.TMDB;
using Shokofin.Configuration;
using Shokofin.Events.Interfaces;
using Shokofin.Extensions;
using Shokofin.ExternalIds;
using Shokofin.Utils;

namespace Shokofin.API.Info;

public class EpisodeInfo : IExtendedItemInfo {
    private readonly ShokoApiClient _client;

    public string Id { get; init; }

    public string SeasonId { get; init; }

    public string? AnidbId { get; init; }

    public string? TmdbMovieId { get; init; }

    public string? TmdbEpisodeId { get; init; }

    public string? TvdbEpisodeId { get; init; }

    public SeriesStructureType StructureType { get; init; }

    public EpisodeType Type { get; init; }

    public bool IsHidden { get; init; }

    public bool IsMainEntry { get; init; }

    public bool IsStandalone { get; init; }

    public int? SeasonNumber { get; init; }

    public int EpisodeNumber { get; init; }

    public string Title { get; init; }

    public IReadOnlyList<Title> Titles { get; init; }

    public string? Overview { get; init; }

    public IReadOnlyList<TextOverview> Overviews { get; init; }

    public string? OriginalLanguageCode { get; init; }

    public ExtraType? ExtraType { get; init; }

    public TimeSpan? Runtime { get; init; }

    public DateTime? AiredAt { get; init; }

    public Rating CommunityRating { get; init; }

    public IReadOnlyList<string> Genres { get; init; }

    public IReadOnlyList<string> Tags { get; init; }

    public IReadOnlyList<string> Studios { get; init; }

    public IReadOnlyDictionary<ProviderName, IReadOnlyList<string>> ProductionLocations { get; init; }

    public IReadOnlyList<Models.ContentRating> ContentRatings { get; init; }

    public IReadOnlyList<PersonInfo> Staff { get; init; }

    public List<CrossReference.EpisodeCrossReferenceIDs> CrossReferences { get; init; }

    public bool IsAvailable => CrossReferences.Count is > 0;

    public EpisodeInfo(
        ShokoApiClient client,
        ShokoEpisode episode, 
        IReadOnlyList<Role> cast,
        List<string> genres,
        List<string> tags,
        string[] productionLocations,
        string? anidbContentRating,
        ITmdbEntity? tmdbEntity = null,
        ITmdbParentEntity? tmdbParentEntity = null
    ) {
        var contentRatings = new List<Models.ContentRating>();
        var productionLocationDict = new Dictionary<ProviderName, IReadOnlyList<string>>();
        var tmdbMovie = tmdbEntity as TmdbMovie;
        var tmdbEpisode = tmdbEntity as TmdbEpisode;
        var isMainEntry = episode.AniDB.Titles.FirstOrDefault(t => t.LanguageCode is "en")?.Value is { } mainAnidbTitle &&
            Text.IgnoredSubTitles.Contains(mainAnidbTitle);
        if (!string.IsNullOrEmpty(anidbContentRating))
            contentRatings.Add(new() {
                Rating = anidbContentRating,
                Country = "US",
                Language = "en",
                Source = "AniDB",
            });
        if (productionLocations.Length > 0)
            productionLocationDict[ProviderName.AniDB] = productionLocations;

        _client = client;
        Id = episode.Id;
        SeasonId = episode.IDs.ParentSeries.ToString();
        AnidbId = episode.AniDB.Id.ToString();
        StructureType = SeriesStructureType.Shoko_Groups;
        Type = episode.AniDB.Type;
        IsHidden = episode.IsHidden;
        IsMainEntry = isMainEntry;
        IsStandalone = tmdbMovie is not null || isMainEntry;
        SeasonNumber = null;
        EpisodeNumber = episode.AniDB.EpisodeNumber;
        ExtraType = Ordering.GetExtraType(episode.AniDB);
        Title = episode.Name;
        Titles = [
            ..episode.AniDB.Titles,
            ..(tmdbEntity?.Titles ?? []),
        ];
        Overview = episode.Description == episode.AniDB.Description
            ? Text.SanitizeAnidbDescription(episode.Description)
            : episode.Description;
        Overviews = [
            ..(!string.IsNullOrEmpty(episode.AniDB.Description) ? [
                new() {
                    IsDefault = true,
                    IsPreferred = string.Equals(episode.Description, episode.AniDB.Description),
                    LanguageCode = "en",
                    Source = "AniDB",
                    Value = Text.SanitizeAnidbDescription(episode.AniDB.Description),
                },
            ] : Array.Empty<TextOverview>()),
            ..(tmdbEntity?.Overviews ?? []),
        ];
        Studios = [];
        if (tmdbMovie is not null) {
            Runtime = tmdbMovie.Runtime ?? episode.AniDB.Duration;
            AiredAt = tmdbMovie.ReleasedAt?.ToDateTime(TimeOnly.Parse("00:00:00", CultureInfo.InvariantCulture), DateTimeKind.Local);
            TmdbMovieId = tmdbMovie.Id.ToString();
            CommunityRating = tmdbMovie.UserRating;
            Staff = tmdbMovie.Cast.Concat(tmdbMovie.Crew)
                .GroupBy(role => (role.Type, role.Staff.Id))
                .Select(roles => RoleToPersonInfo(roles.ToList(), MetadataProvider.Tmdb.ToString()))
                .OfType<PersonInfo>()
                .ToArray();
            productionLocationDict[ProviderName.TMDB] = tmdbMovie.ProductionCountries.Values.ToArray();
            contentRatings.AddRange(tmdbMovie.ContentRatings);
            Studios = tmdbMovie.Studios.Select(r => r.Name).ToArray();
            if (Plugin.Instance.Configuration.TagSources.HasFlag(TagFilter.TagSource.TmdbKeywords))
                tags.AddRange(tmdbMovie.Keywords);
            if (Plugin.Instance.Configuration.TagSources.HasFlag(TagFilter.TagSource.TmdbGenres))
                tags.AddRange(tmdbMovie.Genres);
            if (Plugin.Instance.Configuration.GenreSources.HasFlag(TagFilter.TagSource.TmdbKeywords))
                genres.AddRange(tmdbMovie.Keywords);
            if (Plugin.Instance.Configuration.GenreSources.HasFlag(TagFilter.TagSource.TmdbGenres))
                genres.AddRange(tmdbMovie.Genres);
        }
        else if (tmdbEpisode is not null) {
            TmdbEpisodeId = tmdbEpisode.Id.ToString();
            TvdbEpisodeId = tmdbEpisode.TvdbEpisodeId?.ToString();
            Runtime = tmdbEpisode.Runtime ?? episode.AniDB.Duration;
            AiredAt = tmdbEpisode.AiredAt?.ToDateTime(TimeOnly.Parse("00:00:00", CultureInfo.InvariantCulture), DateTimeKind.Local);
            CommunityRating = tmdbEpisode.UserRating;
            Staff = tmdbEpisode.Cast.Concat(tmdbEpisode.Crew)
                .GroupBy(role => (role.Type, role.Staff.Id))
                .Select(roles => RoleToPersonInfo(roles.ToList(), MetadataProvider.Tmdb.ToString()))
                .OfType<PersonInfo>()
                .ToArray();
            if (tmdbParentEntity is not null) {
                productionLocationDict[ProviderName.TMDB] = tmdbParentEntity.ProductionCountries.Values.ToArray();
                contentRatings.AddRange(tmdbParentEntity.ContentRatings);
                Studios = tmdbParentEntity.Studios.Select(r => r.Name).ToArray();
                if (Plugin.Instance.Configuration.TagSources.HasFlag(TagFilter.TagSource.TmdbKeywords))
                    tags.AddRange(tmdbParentEntity.Keywords);
                if (Plugin.Instance.Configuration.TagSources.HasFlag(TagFilter.TagSource.TmdbGenres))
                    tags.AddRange(tmdbParentEntity.Genres);
                if (Plugin.Instance.Configuration.GenreSources.HasFlag(TagFilter.TagSource.TmdbKeywords))
                    genres.AddRange(tmdbParentEntity.Keywords);
                if (Plugin.Instance.Configuration.GenreSources.HasFlag(TagFilter.TagSource.TmdbGenres))
                    genres.AddRange(tmdbParentEntity.Genres);
            }
        }
        else {
            Runtime = episode.AniDB.Duration;
            AiredAt = episode.AniDB.AirDate;
            CommunityRating = episode.AniDB.Rating;
            Staff = cast
                .GroupBy(role => (role.Type, role.Staff.Id))
                .Select(roles => RoleToPersonInfo(roles.ToList(), AnidbCreatorId.Name))
                .OfType<PersonInfo>()
                .ToArray();
            Studios = cast
                .Where(r => r.Type == CreatorRoleType.Studio)
                .Select(r => r.Staff.Name)
                .ToArray();
        }
        Genres = genres;
        Tags = tags;
        ProductionLocations = productionLocationDict;
        ContentRatings = contentRatings.Distinct().ToList();
        CrossReferences = episode.CrossReferences;
    }

    public EpisodeInfo(ShokoApiClient client, TmdbEpisode tmdbEpisode, TmdbShow tmdbShow) {
        var tags = new List<string>();
        var genres = new List<string>();
        if (Plugin.Instance.Configuration.TagSources.HasFlag(TagFilter.TagSource.TmdbKeywords))
            tags.AddRange(tmdbShow.Keywords);
        if (Plugin.Instance.Configuration.TagSources.HasFlag(TagFilter.TagSource.TmdbGenres))
            tags.AddRange(tmdbShow.Genres);
        if (Plugin.Instance.Configuration.GenreSources.HasFlag(TagFilter.TagSource.TmdbKeywords))
            genres.AddRange(tmdbShow.Keywords);
        if (Plugin.Instance.Configuration.GenreSources.HasFlag(TagFilter.TagSource.TmdbGenres))
            genres.AddRange(tmdbShow.Genres);

        _client = client;
        Id = IdPrefix.TmdbShow + tmdbEpisode.Id.ToString();
        SeasonId = IdPrefix.TmdbShow + tmdbEpisode.SeasonId;
        TmdbEpisodeId = tmdbEpisode.Id.ToString();
        TvdbEpisodeId = tmdbEpisode.TvdbEpisodeId?.ToString();
        StructureType = SeriesStructureType.TMDB_SeriesAndMovies;
        Type = tmdbEpisode.SeasonNumber is 0 ? EpisodeType.Special : EpisodeType.Normal;
        IsHidden = false;
        IsMainEntry = false;
        IsStandalone = false;
        SeasonNumber = tmdbEpisode.SeasonNumber;
        EpisodeNumber = tmdbEpisode.EpisodeNumber;
        Title = tmdbEpisode.Title;
        Titles = tmdbEpisode.Titles;
        Overview = tmdbEpisode.Overview;
        Overviews = tmdbEpisode.Overviews;
        OriginalLanguageCode = tmdbShow.OriginalLanguage;
        ExtraType = null;
        Runtime = tmdbEpisode.Runtime;
        AiredAt = tmdbEpisode.AiredAt?.ToDateTime(TimeOnly.Parse("00:00:00", CultureInfo.InvariantCulture), DateTimeKind.Local);
        CommunityRating = tmdbEpisode.UserRating;
        Genres = genres;
        Tags = tags;
        Studios = tmdbShow.Studios.Select(r => r.Name).ToArray();
        ProductionLocations = new Dictionary<ProviderName, IReadOnlyList<string>>() {
            { ProviderName.TMDB, tmdbShow.ProductionCountries.Values.ToArray() },
        };
        ContentRatings = tmdbShow.ContentRatings;
        Staff = tmdbEpisode.Cast.Concat(tmdbEpisode.Crew)
            .GroupBy(role => (role.Type, role.Staff.Id))
            .Select(roles => RoleToPersonInfo(roles.ToList(), MetadataProvider.Tmdb.ToString()))
            .OfType<PersonInfo>()
            .ToArray();
        CrossReferences = tmdbEpisode.FileCrossReferences
            .SelectMany(a => a.Episodes)
            .DistinctBy(a => (a.ED2K, a.FileSize))
            .ToList();
    }

    public EpisodeInfo(ShokoApiClient client, TmdbMovie tmdbMovie) {
        var tags = new List<string>();
        var genres = new List<string>();
        if (Plugin.Instance.Configuration.TagSources.HasFlag(TagFilter.TagSource.TmdbKeywords))
            tags.AddRange(tmdbMovie.Keywords);
        if (Plugin.Instance.Configuration.TagSources.HasFlag(TagFilter.TagSource.TmdbGenres))
            tags.AddRange(tmdbMovie.Genres);
        if (Plugin.Instance.Configuration.GenreSources.HasFlag(TagFilter.TagSource.TmdbKeywords))
            genres.AddRange(tmdbMovie.Keywords);
        if (Plugin.Instance.Configuration.GenreSources.HasFlag(TagFilter.TagSource.TmdbGenres))
            genres.AddRange(tmdbMovie.Genres);

        _client = client;
        Id = IdPrefix.TmdbMovie + tmdbMovie.Id.ToString();
        SeasonId = tmdbMovie.CollectionId.HasValue && Plugin.Instance.Configuration.SeparateMovies && Plugin.Instance.Configuration.CollectionGrouping is Ordering.CollectionCreationType.Movies
            ? IdPrefix.TmdbMovieCollection + tmdbMovie.CollectionId.Value.ToString()
            : IdPrefix.TmdbMovie + tmdbMovie.Id.ToString();
        TmdbMovieId = tmdbMovie.Id.ToString();
        StructureType = SeriesStructureType.TMDB_SeriesAndMovies;
        Type = EpisodeType.Normal;
        IsHidden = false;
        IsMainEntry = false;
        IsStandalone = true;
        SeasonNumber = null;
        EpisodeNumber = 1;
        Title = tmdbMovie.Title;
        Titles = tmdbMovie.Titles;
        Overview = tmdbMovie.Overview;
        Overviews = tmdbMovie.Overviews;
        OriginalLanguageCode = tmdbMovie.OriginalLanguage;
        ExtraType = null;
        Runtime = tmdbMovie.Runtime;
        AiredAt = tmdbMovie.ReleasedAt?.ToDateTime(TimeOnly.Parse("00:00:00", CultureInfo.InvariantCulture), DateTimeKind.Local);
        CommunityRating = tmdbMovie.UserRating;
        Genres = genres;
        Tags = tags;
        Studios = tmdbMovie.Studios.Select(r => r.Name).ToArray();
        ProductionLocations = new Dictionary<ProviderName, IReadOnlyList<string>>() {
            { ProviderName.TMDB, tmdbMovie.ProductionCountries.Values.ToArray() },
        };
        ContentRatings = tmdbMovie.ContentRatings;
        Staff = tmdbMovie.Cast.Concat(tmdbMovie.Crew)
            .GroupBy(role => (role.Type, role.Staff.Id))
            .Select(roles => RoleToPersonInfo(roles.ToList(), MetadataProvider.Tmdb.ToString()))
            .OfType<PersonInfo>()
            .ToArray();
        CrossReferences = tmdbMovie.FileCrossReferences
            .SelectMany(a => a.Episodes)
            .DistinctBy(a => (a.ED2K, a.FileSize))
            .ToList();
    }

    public async Task<EpisodeImages> GetImages(CancellationToken cancellationToken)
        => Id[0] switch {
            IdPrefix.TmdbShow => await _client.GetImagesForTmdbEpisode(Id[1..], cancellationToken).ConfigureAwait(false),
            IdPrefix.TmdbMovie => await _client.GetImagesForTmdbMovie(Id[1..], cancellationToken).ConfigureAwait(false),
            _ => await _client.GetImagesForShokoEpisode(Id, cancellationToken).ConfigureAwait(false),
        } ?? new();

    private static string? GetImagePath(Image image)
        => image != null && image.IsAvailable ? image.ToURLString(internalUrl: true) : null;

    private static PersonInfo? RoleToPersonInfo(IReadOnlyList<Role> roles, string roleProvider)
        => roles[0].Type switch {
            CreatorRoleType.Director => new PersonInfo {
                Type = PersonKind.Director,
                Name = roles[0].Staff.Name,
                Role = roles[0].Name,
                ImageUrl = GetImagePath(roles[0].Staff.Image),
                ProviderIds = new() {
                    { roleProvider, roles[0].Staff.Id!.Value.ToString() },
                },
            },
            CreatorRoleType.Producer => new PersonInfo {
                Type = PersonKind.Producer,
                Name = roles[0].Staff.Name,
                Role = roles[0].Name,
                ImageUrl = GetImagePath(roles[0].Staff.Image),
            },
            CreatorRoleType.Music => new PersonInfo {
                Type = PersonKind.Lyricist,
                Name = roles[0].Staff.Name,
                Role = roles[0].Name,
                ImageUrl = GetImagePath(roles[0].Staff.Image),
            },
            CreatorRoleType.SourceWork => new PersonInfo {
                Type = PersonKind.Writer,
                Name = roles[0].Staff.Name,
                Role = roles[0].Name,
                ImageUrl = GetImagePath(roles[0].Staff.Image),
            },
            CreatorRoleType.SeriesComposer => new PersonInfo {
                Type = PersonKind.Composer,
                Name = roles[0].Staff.Name,
                ImageUrl = GetImagePath(roles[0].Staff.Image),
            },
            CreatorRoleType.Seiyuu => new PersonInfo {
                Type = PersonKind.Actor,
                Name = roles[0].Staff.Name,
                Role = roles.Select(role => role.Character!.Name).Order().Join(" / "),
                ImageUrl = GetImagePath(roles[0].Staff.Image),
            },
            _ => null,
        };
}
