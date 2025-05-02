using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Shokofin.API;
using Shokofin.API.Info;
using Shokofin.API.Models;
using Shokofin.Configuration;
using Shokofin.Extensions;

namespace Shokofin.Utils;

public static partial class Text {
    private static readonly HashSet<char> PunctuationMarks = [
        // Common punctuation marks
        '.',   // period
        ',',   // comma
        ';',   // semicolon
        ':',   // colon
        '!',   // exclamation point
        '?',   // question mark
        ')',   // right parenthesis
        ']',   // right bracket
        '}',   // right brace
        '"',  // double quote
        '\'',   // single quote
        '，',  // Chinese comma
        '、',  // Chinese enumeration comma
        '！',  // Chinese exclamation point
        '？',  // Chinese question mark
        '“',  // Chinese double quote
        '”',  // Chinese double quote
        '‘',  // Chinese single quote
        '’',  // Chinese single quote
        '】',  // Chinese right bracket
        '》',  // Chinese right angle bracket
        '）',  // Chinese right parenthesis
        '・',  // Japanese middle dot

        // Less common punctuation marks
        '‽',    // interrobang
        '❞',   // double question mark
        '❝',   // double exclamation mark
        '⁇',   // question mark variation
        '⁈',   // exclamation mark variation
        '❕',   // white exclamation mark
        '❔',   // white question mark
        '⁉',   // exclamation mark
        '※',   // reference mark
        '⟩',   // right angle bracket
        '❯',   // right angle bracket
        '❭',   // right angle bracket
        '〉',   // right angle bracket
        '⌉',   // right angle bracket
        '⌋',   // right angle bracket
        '⦄',   // right angle bracket
        '⦆',   // right angle bracket
        '⦈',   // right angle bracket
        '⦊',   // right angle bracket
        '⦌',   // right angle bracket
        '⦎',   // right angle bracket
    ];

    internal static readonly HashSet<string> IgnoredSubTitles = new(StringComparer.InvariantCultureIgnoreCase) {
        "Complete Movie",
        "Music Video",
        "OAD",
        "OVA",
        "Short Movie",
        "Special",
        "TV Special",
        "Web",
    };

    /// <summary>
    /// Determines which provider to use to provide the descriptions.
    /// </summary>
    public enum DescriptionProvider {
        /// <summary>
        /// Provide the Shoko Group description for the show, if the show is
        /// constructed using Shoko's groups feature.
        /// </summary>
        Shoko = 1,

        /// <summary>
        /// Provide the description from AniDB.
        /// </summary>
        AniDB = 2,

        /// <summary>
        /// Deprecated, but kept until the next major release for backwards compatibility.
        /// TODO: REMOVE THIS IN 6.0
        /// </summary>
        TvDB = 3,

        /// <summary>
        /// Provide the description from TMDB.
        /// </summary>
        TMDB = 4
    }

    /// <summary>
    /// Determines how to convert the description.
    /// </summary>
    public enum DescriptionConversionMode {
        /// <summary>
        /// Don't convert the description.
        /// </summary>
        Disabled = 0,

        /// <summary>
        /// Convert the description to plain text.
        /// </summary>
        PlainText = 1,

        /// <summary>
        /// Convert the description to markdown.
        /// </summary>
        Markdown = 2,
    }

    /// <summary>
    /// Determines which provider and method to use to look-up the title.
    /// </summary>
    public enum TitleProvider {
        /// <summary>
        /// Let Shoko decide what to display.
        /// </summary>
        Shoko_Default = 1,

        /// <summary>
        /// Use the default title as provided by AniDB.
        /// </summary>
        AniDB_Default = 2,

        /// <summary>
        /// Use the selected metadata language for the library as provided by
        /// AniDB.
        /// </summary>
        AniDB_LibraryLanguage = 3,

        /// <summary>
        /// Use the title in the origin language as provided by AniDB.
        /// </summary>
        AniDB_CountryOfOrigin = 4,

        /// <summary>
        /// Use the default title as provided by TMDB.
        /// </summary>
        TMDB_Default = 5,

        /// <summary>
        /// Use the selected metadata language for the library as provided by
        /// TMDB.
        /// </summary>
        TMDB_LibraryLanguage = 6,

        /// <summary>
        /// Use the title in the origin language as provided by TMDB.
        /// </summary>
        TMDB_CountryOfOrigin = 7,
    }

    /// <summary>
    /// Determines which type of title to look-up.
    /// </summary>
    public enum TitleProviderType {
        /// <summary>
        /// The main title used for metadata entries.
        /// </summary>
        Main = 0,

        /// <summary>
        /// The secondary title used for metadata entries.
        /// </summary>
        Alternate = 1,
    }

    public static string GetDescription(IBaseItemInfo baseInfo, string? metadataLanguage) {
        foreach (var provider in GetOrderedDescriptionProviders()) {
            var overview = provider switch {
                DescriptionProvider.Shoko =>
                    baseInfo.Overview,
                DescriptionProvider.AniDB =>
                    baseInfo.Overviews.Where(o => o.Source is "AniDB" && string.Equals(o.LanguageCode, metadataLanguage, StringComparison.InvariantCultureIgnoreCase)).FirstOrDefault()?.Value,
                DescriptionProvider.TMDB =>
                    baseInfo.Overviews.Where(o => o.Source is "TMDB" && string.Equals(o.LanguageCode, metadataLanguage, StringComparison.InvariantCultureIgnoreCase)).FirstOrDefault()?.Value,
                _ => null
            };
            if (!string.IsNullOrEmpty(overview))
                return overview;
        }
        return string.Empty;
    }

    public static string GetDescription(IEnumerable<IBaseItemInfo> baseInfoList, string? metadataLanguage)
        => JoinText(baseInfoList.Select(baseInfo => GetDescription(baseInfo, metadataLanguage))) ?? string.Empty;

    public static string GetMovieDescription(EpisodeInfo episodeInfo, SeasonInfo seasonInfo, string? metadataLanguage) {
        // TMDB movies have a proper "episode" description.
        if (episodeInfo.Id[0] is IdPrefix.TmdbMovie)
            return GetDescription(episodeInfo, metadataLanguage);

        return seasonInfo.IsMultiEntry && !episodeInfo.IsMainEntry
            ? GetDescription(episodeInfo, metadataLanguage)
            : GetDescription(seasonInfo, metadataLanguage);
    }

    /// <summary>
    /// Returns a list of the description providers to check, and in what order
    /// </summary>
    private static DescriptionProvider[] GetOrderedDescriptionProviders()
        => Plugin.Instance.Configuration.DescriptionSourceOrder.Where((t) => Plugin.Instance.Configuration.DescriptionSourceList.Contains(t)).ToArray();

    /// <summary>
    /// Sanitize the AniDB entry description to something usable by Jellyfin.
    /// </summary>
    /// <remarks>
    /// Based on ShokoMetadata's summary sanitizer which in turn is based on HAMA's summary sanitizer.
    /// </remarks>
    /// <param name="summary">The raw AniDB description.</param>
    /// <returns>The sanitized AniDB description.</returns>
    public static string SanitizeAnidbDescription(string summary) {
        if (string.IsNullOrWhiteSpace(summary))
            return string.Empty;

        var config = Plugin.Instance.Configuration;
        if (config.SynopsisCleanLinks)
            summary = summary.Replace(SynopsisCleanLinks, match => config.SynopsisEnableMarkdown ? $"[{match.Groups[2].Value}]({match.Groups[1].Value})" : match.Groups[2].Value);

        if (config.SynopsisCleanMiscLines)
            summary = summary.Replace(SynopsisCleanMiscLines, string.Empty);

        if (config.SynopsisRemoveSummary)
            summary = summary
                .Replace(SynopsisRemoveSummary1, match => config.SynopsisEnableMarkdown ? $"**{match.Groups[1].Value}**: " : "")
                .Replace(SynopsisRemoveSummary2, string.Empty);

        if (config.SynopsisCleanMultiEmptyLines)
            summary = summary
                .Replace(SynopsisConvertNewLines, "\n")
                .Replace(SynopsisCleanMultiEmptyLines, "\n");

        return summary.Trim();
    }

    private static readonly Regex SynopsisCleanLinks = new(@"(https?:\/\/\w+.\w+(?:\/?\w+)?) \[([^\]]+)\]", RegexOptions.Compiled);

    private static readonly Regex SynopsisCleanMiscLines = new(@"^(\*|--|~)\s*", RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex SynopsisRemoveSummary1 = new(@"\b(Note|Summary):\s*", RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex SynopsisRemoveSummary2 = new(@"\bSource: [^ ]+", RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex SynopsisConvertNewLines = new(@"\r\n|\r", RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex SynopsisCleanMultiEmptyLines = new(@"\n{2,}", RegexOptions.Singleline | RegexOptions.Compiled);

    public static string? JoinText(IEnumerable<string?> textList) {
        var filteredList = textList
            .Where(title => !string.IsNullOrWhiteSpace(title))
            .Select(title => title!.Trim())
            // We distinct the list because some episode entries contain the **exact** same description.
            .Distinct()
            .ToList();

        if (filteredList.Count == 0)
            return null;

        var index = 1;
        var outputText = filteredList[0];
        while (index < filteredList.Count) {
            var lastChar = outputText[^1];
            outputText += PunctuationMarks.Contains(lastChar) ? " " : ". ";
            outputText += filteredList[index++];
        }

        if (filteredList.Count > 1)
            outputText = outputText.TrimEnd();

        return outputText;
    }

    public static (string? displayTitle, string? alternateTitle) GetEpisodeTitles(EpisodeInfo episodeInfo, SeasonInfo seasonInfo, string? metadataLanguage)
        => seasonInfo.StructureType switch {
            SeriesStructureType.AniDB_Anime => Plugin.Instance.Configuration.AdvancedTitlesConfiguration.AnidbEpisode.Enabled ? (
                GetEpisodeTitleByType(episodeInfo, seasonInfo, Plugin.Instance.Configuration.AdvancedTitlesConfiguration.AnidbEpisode.MainTitle, metadataLanguage),
                JoinTitles(Plugin.Instance.Configuration.AdvancedTitlesConfiguration.AnidbEpisode.AlternateTitles.Select(t => GetEpisodeTitleByType(episodeInfo, seasonInfo, t, metadataLanguage)))
            ) : (
                GetEpisodeTitleByType(episodeInfo, seasonInfo, Plugin.Instance.Configuration.MainTitle, metadataLanguage),
                JoinTitles(Plugin.Instance.Configuration.AlternateTitles.Select(t => GetEpisodeTitleByType(episodeInfo, seasonInfo, t, metadataLanguage)))
            ),
            SeriesStructureType.TMDB_SeriesAndMovies => Plugin.Instance.Configuration.AdvancedTitlesConfiguration.TmdbEpisode.Enabled ? (
                GetEpisodeTitleByType(episodeInfo, seasonInfo, Plugin.Instance.Configuration.AdvancedTitlesConfiguration.TmdbEpisode.MainTitle, metadataLanguage),
                JoinTitles(Plugin.Instance.Configuration.AdvancedTitlesConfiguration.TmdbEpisode.AlternateTitles.Select(t => GetEpisodeTitleByType(episodeInfo, seasonInfo, t, metadataLanguage)))
            ) : (
                GetEpisodeTitleByType(episodeInfo, seasonInfo, Plugin.Instance.Configuration.MainTitle, metadataLanguage),
                JoinTitles(Plugin.Instance.Configuration.AlternateTitles.Select(t => GetEpisodeTitleByType(episodeInfo, seasonInfo, t, metadataLanguage)))
            ),
            _ => Plugin.Instance.Configuration.AdvancedTitlesConfiguration.ShokoEpisode.Enabled ? (
                GetEpisodeTitleByType(episodeInfo, seasonInfo, Plugin.Instance.Configuration.AdvancedTitlesConfiguration.ShokoEpisode.MainTitle, metadataLanguage),
                JoinTitles(Plugin.Instance.Configuration.AdvancedTitlesConfiguration.ShokoEpisode.AlternateTitles.Select(t => GetEpisodeTitleByType(episodeInfo, seasonInfo, t, metadataLanguage)))
            ) : (
                GetEpisodeTitleByType(episodeInfo, seasonInfo, Plugin.Instance.Configuration.MainTitle, metadataLanguage),
                JoinTitles(Plugin.Instance.Configuration.AlternateTitles.Select(t => GetEpisodeTitleByType(episodeInfo, seasonInfo, t, metadataLanguage)))
            ),
        };

    public static (string? displayTitle, string? alternateTitle) GetSeasonTitles(SeasonInfo seasonInfo, int baseSeasonOffset, string? metadataLanguage) {
        var (displayTitle, alternateTitle) = seasonInfo.StructureType switch {
            SeriesStructureType.AniDB_Anime => Plugin.Instance.Configuration.AdvancedTitlesConfiguration.AnidbSeason.Enabled ? (
                GetSeriesTitleByType(seasonInfo, Plugin.Instance.Configuration.AdvancedTitlesConfiguration.AnidbSeason.MainTitle, metadataLanguage),
                JoinTitles(Plugin.Instance.Configuration.AdvancedTitlesConfiguration.AnidbSeason.AlternateTitles.Select(t => GetSeriesTitleByType(seasonInfo, t, metadataLanguage)))
            ) : (
                GetSeriesTitleByType(seasonInfo, Plugin.Instance.Configuration.MainTitle, metadataLanguage),
                JoinTitles(Plugin.Instance.Configuration.AlternateTitles.Select(t => GetSeriesTitleByType(seasonInfo, t, metadataLanguage)))
            ),
            SeriesStructureType.TMDB_SeriesAndMovies => Plugin.Instance.Configuration.AdvancedTitlesConfiguration.TmdbSeason.Enabled ? (
                GetSeriesTitleByType(seasonInfo, Plugin.Instance.Configuration.AdvancedTitlesConfiguration.TmdbSeason.MainTitle, metadataLanguage),
                JoinTitles(Plugin.Instance.Configuration.AdvancedTitlesConfiguration.TmdbSeason.AlternateTitles.Select(t => GetSeriesTitleByType(seasonInfo, t, metadataLanguage)))
            ) : (
                GetSeriesTitleByType(seasonInfo, Plugin.Instance.Configuration.MainTitle, metadataLanguage),
                JoinTitles(Plugin.Instance.Configuration.AlternateTitles.Select(t => GetSeriesTitleByType(seasonInfo, t, metadataLanguage)))
            ),
            _ => Plugin.Instance.Configuration.AdvancedTitlesConfiguration.ShokoSeason.Enabled ? (
                GetSeriesTitleByType(seasonInfo, Plugin.Instance.Configuration.AdvancedTitlesConfiguration.ShokoSeason.MainTitle, metadataLanguage),
                JoinTitles(Plugin.Instance.Configuration.AdvancedTitlesConfiguration.ShokoSeason.AlternateTitles.Select(t => GetSeriesTitleByType(seasonInfo, t, metadataLanguage)))
            ) : (
                GetSeriesTitleByType(seasonInfo, Plugin.Instance.Configuration.MainTitle, metadataLanguage),
                JoinTitles(Plugin.Instance.Configuration.AlternateTitles.Select(t => GetSeriesTitleByType(seasonInfo, t, metadataLanguage)))
            ),
        };

        if (baseSeasonOffset > 0) {
            string type = string.Empty;
            switch (baseSeasonOffset) {
                default:
                    break;
                case 1:
                    type = "Alternate Version";
                    break;
            }
            if (!string.IsNullOrEmpty(type)) {
                if (!string.IsNullOrEmpty(displayTitle))
                    displayTitle += $" ({type})";
                if (!string.IsNullOrEmpty(alternateTitle))
                    alternateTitle += $" ({type})";
            }
        }

        return (displayTitle, alternateTitle);
    }

    public static (string? displayTitle, string? alternateTitle) GetShowTitles(ShowInfo showInfo, string? metadataLanguage)
        => showInfo.DefaultSeason.StructureType switch {
            SeriesStructureType.AniDB_Anime => Plugin.Instance.Configuration.AdvancedTitlesConfiguration.AnidbAnime.Enabled ? (
                GetSeriesTitleByType(showInfo, Plugin.Instance.Configuration.AdvancedTitlesConfiguration.AnidbAnime.MainTitle, metadataLanguage),
                JoinTitles(Plugin.Instance.Configuration.AdvancedTitlesConfiguration.AnidbAnime.AlternateTitles.Select(t => GetSeriesTitleByType(showInfo, t, metadataLanguage)))
            ) : (
                GetSeriesTitleByType(showInfo, Plugin.Instance.Configuration.MainTitle, metadataLanguage),
                JoinTitles(Plugin.Instance.Configuration.AlternateTitles.Select(t => GetSeriesTitleByType(showInfo, t, metadataLanguage)))
            ),
            SeriesStructureType.TMDB_SeriesAndMovies => Plugin.Instance.Configuration.AdvancedTitlesConfiguration.TmdbShow.Enabled ? (
                GetSeriesTitleByType(showInfo, Plugin.Instance.Configuration.AdvancedTitlesConfiguration.TmdbShow.MainTitle, metadataLanguage),
                JoinTitles(Plugin.Instance.Configuration.AdvancedTitlesConfiguration.TmdbShow.AlternateTitles.Select(t => GetSeriesTitleByType(showInfo, t, metadataLanguage)))
            ) : (
                GetSeriesTitleByType(showInfo, Plugin.Instance.Configuration.MainTitle, metadataLanguage),
                JoinTitles(Plugin.Instance.Configuration.AlternateTitles.Select(t => GetSeriesTitleByType(showInfo, t, metadataLanguage)))
            ),
            _ => Plugin.Instance.Configuration.AdvancedTitlesConfiguration.ShokoSeries.Enabled ? (
                GetSeriesTitleByType(showInfo, Plugin.Instance.Configuration.AdvancedTitlesConfiguration.ShokoSeries.MainTitle, metadataLanguage),
                JoinTitles(Plugin.Instance.Configuration.AdvancedTitlesConfiguration.ShokoSeries.AlternateTitles.Select(t => GetSeriesTitleByType(showInfo, t, metadataLanguage)))
            ) : (
                GetSeriesTitleByType(showInfo, Plugin.Instance.Configuration.MainTitle, metadataLanguage),
                JoinTitles(Plugin.Instance.Configuration.AlternateTitles.Select(t => GetSeriesTitleByType(showInfo, t, metadataLanguage)))
            ),
        };

    public static (string? displayTitle, string? alternateTitle) GetMovieTitles(EpisodeInfo episodeInfo, SeasonInfo seasonInfo, string? metadataLanguage)
        => seasonInfo.StructureType switch {
            SeriesStructureType.AniDB_Anime => Plugin.Instance.Configuration.AdvancedTitlesConfiguration.AnidbSeason.Enabled ? (
                GetMovieTitleByType(episodeInfo, seasonInfo, Plugin.Instance.Configuration.AdvancedTitlesConfiguration.AnidbSeason.MainTitle, metadataLanguage),
                JoinTitles(Plugin.Instance.Configuration.AdvancedTitlesConfiguration.AnidbSeason.AlternateTitles.Select(t => GetMovieTitleByType(episodeInfo, seasonInfo, t, metadataLanguage)))
            ) : (
                GetMovieTitleByType(episodeInfo, seasonInfo, Plugin.Instance.Configuration.MainTitle, metadataLanguage),
                JoinTitles(Plugin.Instance.Configuration.AlternateTitles.Select(t => GetMovieTitleByType(episodeInfo, seasonInfo, t, metadataLanguage)))
            ),
            SeriesStructureType.TMDB_SeriesAndMovies => Plugin.Instance.Configuration.AdvancedTitlesConfiguration.TmdbSeason.Enabled ? (
                GetMovieTitleByType(episodeInfo, seasonInfo, Plugin.Instance.Configuration.AdvancedTitlesConfiguration.TmdbSeason.MainTitle, metadataLanguage),
                JoinTitles(Plugin.Instance.Configuration.AdvancedTitlesConfiguration.TmdbSeason.AlternateTitles.Select(t => GetMovieTitleByType(episodeInfo, seasonInfo, t, metadataLanguage)))
            ) : (
                GetMovieTitleByType(episodeInfo, seasonInfo, Plugin.Instance.Configuration.MainTitle, metadataLanguage),
                JoinTitles(Plugin.Instance.Configuration.AlternateTitles.Select(t => GetMovieTitleByType(episodeInfo, seasonInfo, t, metadataLanguage)))
            ),
            _ => Plugin.Instance.Configuration.AdvancedTitlesConfiguration.ShokoSeason.Enabled ? (
                GetMovieTitleByType(episodeInfo, seasonInfo, Plugin.Instance.Configuration.AdvancedTitlesConfiguration.ShokoSeason.MainTitle, metadataLanguage),
                JoinTitles(Plugin.Instance.Configuration.AdvancedTitlesConfiguration.ShokoSeason.AlternateTitles.Select(t => GetMovieTitleByType(episodeInfo, seasonInfo, t, metadataLanguage)))
            ) : (
                GetMovieTitleByType(episodeInfo, seasonInfo, Plugin.Instance.Configuration.MainTitle, metadataLanguage),
                JoinTitles(Plugin.Instance.Configuration.AlternateTitles.Select(t => GetMovieTitleByType(episodeInfo, seasonInfo, t, metadataLanguage)))
            ),
        };

    public static (string? displayTitle, string? alternateTitle) GetCollectionTitles(SeasonInfo seasonInfo, string? metadataLanguage)
        => seasonInfo.StructureType switch {
            SeriesStructureType.TMDB_SeriesAndMovies => Plugin.Instance.Configuration.AdvancedTitlesConfiguration.TmdbCollection.Enabled ? (
                GetSeriesTitleByType(seasonInfo, Plugin.Instance.Configuration.AdvancedTitlesConfiguration.TmdbCollection.MainTitle, metadataLanguage),
                JoinTitles(Plugin.Instance.Configuration.AdvancedTitlesConfiguration.TmdbCollection.AlternateTitles.Select(t => GetSeriesTitleByType(seasonInfo, t, metadataLanguage)))
            ) : (
                GetSeriesTitleByType(seasonInfo, Plugin.Instance.Configuration.MainTitle, metadataLanguage),
                JoinTitles(Plugin.Instance.Configuration.AlternateTitles.Select(t => GetSeriesTitleByType(seasonInfo, t, metadataLanguage)))
            ),
            _ => Plugin.Instance.Configuration.AdvancedTitlesConfiguration.ShokoCollection.Enabled ? (
                GetSeriesTitleByType(seasonInfo, Plugin.Instance.Configuration.AdvancedTitlesConfiguration.ShokoCollection.MainTitle, metadataLanguage),
                JoinTitles(Plugin.Instance.Configuration.AdvancedTitlesConfiguration.ShokoCollection.AlternateTitles.Select(t => GetSeriesTitleByType(seasonInfo, t, metadataLanguage)))
            ) : (
                GetSeriesTitleByType(seasonInfo, Plugin.Instance.Configuration.MainTitle, metadataLanguage),
                JoinTitles(Plugin.Instance.Configuration.AlternateTitles.Select(t => GetSeriesTitleByType(seasonInfo, t, metadataLanguage)))
            ),
        };

    public static (string? displayTitle, string? alternateTitle) GetCollectionTitles(CollectionInfo collectionInfo, string? metadataLanguage)
        =>  Plugin.Instance.Configuration.AdvancedTitlesConfiguration.ShokoCollection.Enabled ? (
                GetSeriesTitleByType(collectionInfo, Plugin.Instance.Configuration.AdvancedTitlesConfiguration.ShokoCollection.MainTitle, metadataLanguage),
                JoinTitles(Plugin.Instance.Configuration.AdvancedTitlesConfiguration.ShokoCollection.AlternateTitles.Select(t => GetSeriesTitleByType(collectionInfo, t, metadataLanguage)))
            ) : (
                GetSeriesTitleByType(collectionInfo, Plugin.Instance.Configuration.MainTitle, metadataLanguage),
                JoinTitles(Plugin.Instance.Configuration.AlternateTitles.Select(t => GetSeriesTitleByType(collectionInfo, t, metadataLanguage)))
            );

    private static string? GetMovieTitleByType(EpisodeInfo episodeInfo, SeasonInfo seasonInfo, TitleConfiguration configuration, string? metadataLanguage) {
        if (episodeInfo.Id[0] is IdPrefix.TmdbMovie)
            return GetEpisodeTitleByType(episodeInfo, seasonInfo, configuration, metadataLanguage);

        var mainTitle = GetSeriesTitleByType(seasonInfo, configuration, metadataLanguage);
        var subTitle = GetEpisodeTitleByType(episodeInfo, seasonInfo, configuration, metadataLanguage);

        if (!string.IsNullOrEmpty(subTitle))
            return $"{mainTitle}: {subTitle}".Trim();
        else if (episodeInfo.EpisodeNumber > 1)
            return $"{mainTitle} {NumericToRoman(episodeInfo.EpisodeNumber)}".Trim();
        return mainTitle?.Trim();
    }

    private static string? GetEpisodeTitleByType(EpisodeInfo episodeInfo, SeasonInfo seasonInfo, TitleConfiguration configuration, string? metadataLanguage) {
        foreach (var provider in configuration.GetOrderedTitleProviders()) {
            var title = provider switch {
                TitleProvider.Shoko_Default =>
                    episodeInfo.Title,
                TitleProvider.AniDB_Default =>
                    episodeInfo.Titles.FirstOrDefault(title => title.Source is "AniDB" && title.LanguageCode is "en")?.Value,
                TitleProvider.AniDB_LibraryLanguage =>
                    GetTitlesForLanguage(episodeInfo.Titles.Where(t => t.Source is "AniDB").ToList(), false, configuration.AllowAny, metadataLanguage),
                TitleProvider.AniDB_CountryOfOrigin =>
                    GetTitlesForLanguage(episodeInfo.Titles.Where(t => t.Source is "AniDB").ToList(), false, configuration.AllowAny, GuessOriginLanguage(GetMainLanguage(seasonInfo.Titles.Where(t => t.Source is "AniDB").ToList()))),
                TitleProvider.TMDB_Default =>
                    episodeInfo.Titles.FirstOrDefault(title => title.Source is "TMDB" && title.LanguageCode is "en")?.Value,
                TitleProvider.TMDB_LibraryLanguage =>
                    GetTitlesForLanguage(episodeInfo.Titles.Where(t => t.Source is "TMDB").ToList(), false, configuration.AllowAny, metadataLanguage),
                TitleProvider.TMDB_CountryOfOrigin =>
                    GetTitlesForLanguage(episodeInfo.Titles.Where(t => t.Source is "TMDB").ToList(), false, configuration.AllowAny, episodeInfo.OriginalLanguageCode),
                _ => null,
            };
            if (!string.IsNullOrEmpty(title) && !InvalidEpisodeTitleRegex().IsMatch(title))
                return title.Trim();
        }
        return null;
    }

    private static string? GetSeriesTitleByType(IBaseItemInfo baseInfo, TitleConfiguration configuration, string? metadataLanguage) {
        foreach (var provider in configuration.GetOrderedTitleProviders()) {
            var title = provider switch {
                TitleProvider.Shoko_Default =>
                    baseInfo.Title,
                TitleProvider.AniDB_Default =>
                    baseInfo.Titles.Where(t => t.Source is "AniDB").FirstOrDefault(title => title.IsDefault)?.Value,
                TitleProvider.AniDB_LibraryLanguage =>
                    GetTitlesForLanguage(baseInfo.Titles.Where(t => t.Source is "AniDB").ToList(), true, configuration.AllowAny, metadataLanguage),
                TitleProvider.AniDB_CountryOfOrigin =>
                    GetTitlesForLanguage(baseInfo.Titles.Where(t => t.Source is "AniDB").ToList(), true, configuration.AllowAny, GuessOriginLanguage(GetMainLanguage(baseInfo.Titles.Where(t => t.Source is "AniDB").ToList()))),
                TitleProvider.TMDB_Default =>
                    baseInfo.Titles.Where(t => t.Source is "TMDB").FirstOrDefault(title => title.IsDefault)?.Value,
                TitleProvider.TMDB_LibraryLanguage =>
                    GetTitlesForLanguage(baseInfo.Titles.Where(t => t.Source is "TMDB").ToList(), true, configuration.AllowAny, metadataLanguage),
                TitleProvider.TMDB_CountryOfOrigin =>
                    GetTitlesForLanguage(baseInfo.Titles.Where(t => t.Source is "TMDB").ToList(), true, configuration.AllowAny, baseInfo.OriginalLanguageCode),
                _ => null,
            };
            if (!string.IsNullOrEmpty(title))
                return title.Trim();
        }
        return null;
    }

    [GeneratedRegex(@"^(?:Special|Episode|Volume|OVA|OAD|Web) \d+$|^Part \d+ of \d+$|^Episode [COPRST]\d+$|^(?:OVA|OAD|Movie|Complete Movie|Short Movie|TV Special|Music Video|Web|Volume)$", RegexOptions.IgnoreCase | RegexOptions.Compiled, "en-US")]
    private static partial Regex InvalidEpisodeTitleRegex();

    /// <summary>
    /// Get the first title available for the language, optionally using types
    /// to filter the list in addition to the metadata languages provided.
    /// </summary>
    /// <param name="titles">Title list to search.</param>
    /// <param name="usingTypes">Search using titles</param>
    /// <param name="allowAny">Allow any title to be returned.</param>
    /// <param name="metadataLanguages">The metadata languages to search for.</param>
    /// <returns>The first found title in any of the provided metadata languages, or null.</returns>
    public static string? GetTitlesForLanguage(IReadOnlyList<Title> titles, bool usingTypes, bool allowAny, params string?[] metadataLanguages) {
        foreach (var lang in metadataLanguages) {
            if (string.IsNullOrEmpty(lang))
                continue;

            var titleList = titles.Where(t => t.LanguageCode == lang).ToList();
            if (titleList.Count == 0)
                continue;

            string? title = null;
            if (usingTypes) {
                title = titleList.FirstOrDefault(t => t.Type == TitleType.Official)?.Value;
                if (string.IsNullOrEmpty(title) && allowAny)
                    title = titleList.FirstOrDefault()?.Value;
            }
            else {
                title = titles.FirstOrDefault()?.Value;
            }
            if (!string.IsNullOrWhiteSpace(title) && !InvalidEpisodeTitleRegex().IsMatch(title))
                return title;
        }
        return null;
    }

    /// <summary>
    /// Get the main title language from the title list.
    /// </summary>
    /// <param name="titles">Title list.</param>
    /// <returns>The main title language code.</returns>
    private static string GetMainLanguage(IEnumerable<Title> titles)
        => titles.FirstOrDefault(t => t?.Type == TitleType.Main)?.LanguageCode ?? titles.FirstOrDefault()?.LanguageCode ?? "x-other";

    /// <summary>
    /// Guess the origin language based on the main title language.
    /// </summary>
    /// <param name="langCode">The main title language code.</param>
    /// <returns>The list of origin language codes to try and use.</returns>
    private static string[] GuessOriginLanguage(string langCode)
        => langCode switch {
            "x-other" => ["ja"],
            "x-jat" => ["ja"],
            "x-zht" => ["zn-hans", "zn-hant", "zn-c-mcm", "zn"],
            _ => [langCode],
        };
    
    private static string NumericToRoman(int number) =>
        number switch {
            1 => "I",
            2 => "II",
            3 => "III",
            4 => "IV",
            5 => "V",
            6 => "VI",
            7 => "VII",
            8 => "VIII",
            9 => "IX",
            10 => "X",
            11 => "XI",
            12 => "XII",
            13 => "XIII",
            14 => "XIV",
            15 => "XV",
            16 => "XVI",
            17 => "XVII",
            18 => "XVIII",
            19 => "XIX",
            20 => "XX",
            21 => "XXI",
            22 => "XXII",
            23 => "XXIII",
            24 => "XXIV",
            _ => number.ToString(),
        };

    private static string? JoinTitles(IEnumerable<string?> titleList)
        => titleList
            .Where(title => !string.IsNullOrWhiteSpace(title))
            .Select(title => title!.Trim())
            .Distinct(StringComparer.Ordinal)
            .Join(" | ") is { Length: > 0 } result ? result : null;
}
