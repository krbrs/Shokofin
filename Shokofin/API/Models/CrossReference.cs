using System.Collections.Generic;
using System.Text.Json.Serialization;
using Shokofin.API.Models.Shoko;

namespace Shokofin.API.Models;

public class CrossReference {
    /// <summary>
    /// The Series IDs
    /// </summary>
    [JsonPropertyName("SeriesID")]
    public SeriesCrossReferenceIDs Series { get; set; } = new();

    /// <summary>
    /// The Episode IDs
    /// </summary>
    [JsonPropertyName("EpisodeIDs")]
    public List<EpisodeCrossReferenceIDs> Episodes { get; set; } = [];

    /// <summary>
    /// File episode cross-reference for a series.
    /// </summary>
    public class EpisodeCrossReferenceIDs {
        /// <summary>
        /// The Shoko ID, if the local metadata has been created yet.
        /// </summary>
        [JsonPropertyName("ID")]
        public int? Shoko { get; set; }

        /// <summary>
        /// The AniDB ID.
        /// </summary>
        public int AniDB { get; set; }

        /// <summary>
        /// The Movie DataBase (TMDB) Cross-Reference IDs.
        /// </summary>
        public ShokoEpisode.TmdbEpisodeIDs TMDB { get; set; } = new();

        /// <summary>
        /// The Release Group ID.
        /// </summary>
        public int? ReleaseGroup { get; set; }

        /// <summary>
        /// ED2K hash.
        /// </summary>
        public string ED2K { get; set; } = string.Empty;

        /// <summary>
        /// File size.
        /// </summary>
        public long FileSize { get; set; }

        /// <summary>
        /// Percentage file is matched to the episode.
        /// </summary>
        public CrossReferencePercentage Percentage { get; set; } = new();
    }

    public class CrossReferencePercentage {
        /// <summary>
        /// File/episode cross-reference percentage range start.
        /// </summary>
        public int Start { get; set; }

        /// <summary>
        /// File/episode cross-reference percentage range end.
        /// </summary>
        public int End { get; set; }

        /// <summary>
        /// The raw percentage to "group" the cross-references by.
        /// </summary>
        public int Size { get; set; }

        /// <summary>
        /// The assumed number of groups in the release, to group the
        /// cross-references by.
        /// </summary>
        public int? Group { get; set; }
    }

    /// <summary>
    /// File series cross-reference.
    /// </summary>
    public class SeriesCrossReferenceIDs {
        /// <summary>
        /// The Shoko ID, if the local metadata has been created yet.
        /// /// </summary>
        [JsonPropertyName("ID")]

        public int? Shoko { get; set; }

        /// <summary>
        /// The AniDB ID.
        /// </summary>
        public int AniDB { get; set; }

        /// <summary>
        /// The Movie DataBase (TMDB) Cross-Reference IDs.
        /// </summary>
        public ShokoSeries.TmdbSeriesIDs TMDB { get; set; } = new();
    }
}
