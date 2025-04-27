
namespace Shokofin.Configuration;

/// <summary>
/// Advanced title configuration with support for per structure type per base
/// item type configuration.
/// </summary>
public class AdvancedTitlesConfiguration {
    /// <summary>
    /// Title settings for Shoko collections.
    /// </summary>
    public TitlesConfiguration ShokoCollection { get; set; } = new();

    /// <summary>
    /// Title settings for TMDb collections.
    /// </summary>
    public TitlesConfiguration TmdbCollection { get; set; } = new();

    /// <summary>
    /// Title settings for AniDB movies.
    /// </summary>
    public TitlesConfiguration AnidbMovie { get; set; } = new();

    /// <summary>
    /// Title settings for Shoko movies.
    /// </summary>
    public TitlesConfiguration ShokoMovie { get; set; } = new();

    /// <summary>
    /// Title settings for TMDb movies.
    /// </summary>
    public TitlesConfiguration TmdbMovie { get; set; } = new();

    /// <summary>
    /// Title settings for AniDB anime.
    /// </summary>
    public TitlesConfiguration AnidbAnime { get; set; } = new();

    /// <summary>
    /// Title settings for Shoko series.
    /// </summary>
    public TitlesConfiguration ShokoSeries { get; set; } = new();

    /// <summary>
    /// Title settings for TMDb shows.
    /// </summary>
    public TitlesConfiguration TmdbShow { get; set; } = new();

    /// <summary>
    /// Title settings for AniDB seasons.
    /// </summary>
    public TitlesConfiguration AnidbSeason { get; set; } = new();

    /// <summary>
    /// Title settings for Shoko seasons.
    /// </summary>
    public TitlesConfiguration ShokoSeason { get; set; } = new();

    /// <summary>
    /// Title settings for TMDb seasons.
    /// </summary>
    public TitlesConfiguration TmdbSeason { get; set; } = new();

    /// <summary>
    /// Title settings for AniDB episodes.
    /// </summary>
    public TitlesConfiguration AnidbEpisode { get; set; } = new();

    /// <summary>
    /// Title settings for Shoko episodes.
    /// </summary>
    public TitlesConfiguration ShokoEpisode { get; set; } = new();

    /// <summary>
    /// Title settings for TMDb episodes.
    /// </summary>
    public TitlesConfiguration TmdbEpisode { get; set; } = new();
}