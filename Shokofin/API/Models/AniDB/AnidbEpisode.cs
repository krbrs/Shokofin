using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Shokofin.API.Models.AniDB;

public class AnidbEpisode
{
    [JsonPropertyName("ID")]
    public int Id { get; set; }

    /// <summary>
    /// The duration of the episode.
    /// </summary>
    public TimeSpan Duration { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public EpisodeType Type { get; set; }

    public int EpisodeNumber { get; set; }

    public DateTime? AirDate { get; set; }

    public IReadOnlyList<Title> Titles { get; set; } = [];

    public string Description { get; set; } = string.Empty;

    public Rating Rating { get; set; } = new();
}
