using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Jellyfin.Data.Enums;

namespace Shokofin.API.Models.TMDB;

/// <summary>
/// APIv3 The Movie DataBase (TMDB) Episode Data Transfer Object (DTO).
/// </summary>
public class TmdbEpisode : ITmdbEntity {
    /// <summary>
    /// TMDB Episode ID.
    /// </summary>
    [JsonPropertyName("ID")]
    public int Id { get; set; }

    /// <summary>
    /// TMDB Season ID.
    /// </summary>
    [JsonPropertyName("SeasonID")]
    public string SeasonId { get; set; } = string.Empty;

    /// <summary>
    /// TMDB Show ID.
    /// </summary>
    [JsonPropertyName("ShowID")]
    public int ShowId { get; set; }

    /// <summary>
    /// TVDB Episode ID, if available.
    /// </summary>
    [JsonPropertyName("TVDBEpisodeID")]
    public int? TvdbEpisodeId { get; set; }

    /// <summary>
    /// Preferred title based upon episode title preference.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// All available titles for the episode, if they should be included.
    /// </summary>
    public IReadOnlyList<Title> Titles { get; set; } = [];

    /// <summary>
    /// Preferred overview based upon episode title preference.
    /// </summary>
    public string Overview { get; set; } = string.Empty;

    /// <summary>
    /// All available overviews for the episode, if they should be included.
    /// </summary>
    public IReadOnlyList<Text> Overviews { get; set; } = [];

    /// <summary>
    /// The episode number for the main ordering or alternate ordering in use.
    /// </summary>
    public int EpisodeNumber { get; set; }

    /// <summary>
    /// The season number for the main ordering or alternate ordering in use.
    /// </summary>
    public int SeasonNumber { get; set; }

    /// <summary>
    /// User rating of the episode from TMDB users.
    /// </summary>
    public Rating UserRating { get; set; } = new();

    /// <summary>
    /// The episode run-time, if it is known.
    /// </summary>
    public TimeSpan? Runtime { get; set; }

    /// <summary>
    /// The cast that have worked on this show across all episodes and all seasons.
    /// </summary>
    public IReadOnlyList<Role> Cast { get; set; } = [];

    /// <summary>
    /// The crew that have worked on this show across all episodes and all seasons.
    /// </summary>
    public IReadOnlyList<Role> Crew { get; set; } = [];

    /// <summary>
    /// TMDB episode to file cross-references.
    /// </summary>
    public IReadOnlyList<CrossReference> FileCrossReferences { get; set; } = [];

    /// <summary>
    /// The date the episode first aired, if it is known.
    /// </summary>
    public DateOnly? AiredAt { get; set; }

    /// <summary>
    /// When the local metadata was first created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// When the local metadata was last updated with new changes from the
    /// remote.
    /// </summary>
    public DateTime LastUpdatedAt { get; set; }

    string ITmdbEntity.Id => Id.ToString();

    BaseItemKind ITmdbEntity.Kind => BaseItemKind.Episode;
}
