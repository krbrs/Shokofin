using System.Text.Json.Serialization;
using Shokofin.API.Models;
using Shokofin.Utils;

namespace Shokofin.Configuration;

/// <summary>
/// Per series configuration.
/// </summary>
public class SeriesConfiguration {
    /// <summary>
    /// The series type.
    /// </summary>
    public SeriesType Type { get; set; }

    /// <summary>
    /// The series structure type to use.
    /// </summary>
    public SeriesStructureType StructureType { get; set; }

    /// <summary>
    /// Determines how the merging should be handled for the series, if at all.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public SeriesMergingOverride MergeOverride { get; set; }

    /// <summary>
    /// Determines how episodes should be converted, if at all.
    /// </summary>
    public SeriesEpisodeConversion EpisodeConversion { get; set; }

    /// <summary>
    /// Whether to order episodes by airdate instead of episode number.
    /// </summary>
    public bool OrderByAirdate { get; set; }
}

/// <summary>
/// Nullable per series configuration.
/// </summary>
public class NullableSeriesConfiguration {
    /// <summary>
    /// The series type.
    /// </summary>
    public SeriesType? Type { get; set; }

    /// <summary>
    /// The series structure type to use.
    /// </summary>
    public SeriesStructureType? StructureType { get; set; }

    /// <summary>
    /// Determines how the merging should be handled for the series, if at all.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public SeriesMergingOverride? MergeOverride { get; set; }

    /// <summary>
    /// Determines how episodes should be converted, if at all.
    /// </summary>
    public SeriesEpisodeConversion? EpisodeConversion { get; set; }

    /// <summary>
    /// Whether to order episodes by airdate instead of episode number.
    /// </summary>
    public bool? OrderByAirdate { get; set; }
}
