
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Shokofin.API;
using Shokofin.API.Models;
using Shokofin.Utils;

namespace Shokofin.Configuration;

public class SeriesConfigurationService(ILogger<SeriesConfigurationService> logger, ShokoApiClient apiClient, ShokoApiManager apiManager) {

    private readonly GuardedMemoryCache _cache = new(logger, new() { ExpirationScanFrequency = TimeSpan.FromMinutes(25) }, new() { SlidingExpiration = new(2, 30, 0) });

    private const string ManagedBy = "This tag is managed by Shokofin and should not be edited manually.";

    private readonly IReadOnlyList<SimpleTag> _simpleTags = [
        new() {
            NameRegex = new(@"^Series Type/Unknowns?$", RegexOptions.ECMAScript | RegexOptions.IgnoreCase | RegexOptions.Compiled),
            Name = "Series Type/Unknown",
            Description = $"Override the series type as an unknown type series. {ManagedBy}",
        },
        new() {
            NameRegex = new(@"^Series Type/Others?$", RegexOptions.ECMAScript | RegexOptions.IgnoreCase | RegexOptions.Compiled),
            Name = "Series Type/Other",
            Description = $"Override the series type as an other type series. {ManagedBy}",
        },
        new() {
            NameRegex = new(@"^Series Type/TVs?$", RegexOptions.ECMAScript | RegexOptions.IgnoreCase | RegexOptions.Compiled),
            Name = "Series Type/TV",
            Description = $"Override the series type as a TV series. {ManagedBy}",
        },
        new() {
            NameRegex = new(@"^Series Type/TV ?Specials?$", RegexOptions.ECMAScript | RegexOptions.IgnoreCase | RegexOptions.Compiled),
            Name = "Series Type/TV Special",
            Description = $"Override the series type as a TV special. {ManagedBy}",
        },
        new() {
            NameRegex = new(@"^Series Type/Webs?$", RegexOptions.ECMAScript | RegexOptions.IgnoreCase | RegexOptions.Compiled),
            Name = "Series Type/Web",
            Description = $"Override the series type as a web series. {ManagedBy}",
        },
        new() {
            NameRegex = new(@"^Series Type/Movies?$", RegexOptions.ECMAScript | RegexOptions.IgnoreCase | RegexOptions.Compiled),
            Name = "Series Type/Movie",
            Description = $"Override the series type as a movie series. {ManagedBy}",
        },
        new() {
            NameRegex = new(@"^Series Type/OVAs?$", RegexOptions.ECMAScript | RegexOptions.IgnoreCase | RegexOptions.Compiled),
            Name = "Series Type/OVA",
            Description = $"Override the series type as an original video animation series. {ManagedBy}",
        },
        new() {
            NameRegex = new(@"^Series Type/Music ?Videos?$", RegexOptions.ECMAScript | RegexOptions.IgnoreCase | RegexOptions.Compiled),
            Name = "Series Type/Music Video",
            Description = $"Override the series type as a music video series. {ManagedBy}",
        },

        new() {
            Name = "Shokofin/AniDB Structure",
            Description = $"Use an AniDB based structure for this Shoko series in Jellyfin. {ManagedBy}",
        },
        new() {
            Name = "Shokofin/Shoko Structure",
            Description = $"Use a Shoko Group based structure for this Shoko series in Jellyfin. {ManagedBy}",
        },
        new() {
            Name = "Shokofin/TMDb Structure",
            Description = $"Use a TMDb based structure for this Shoko series in Jellyfin. {ManagedBy}",
        },

        new() {
            Name = "Shokofin/Default Season Ordering",
            Description = $"Let the server decide the season ordering. {ManagedBy}",
        },
        new() {
            Name = "Shokofin/Release Based Season Ordering",
            Description = $"Order seasons based on release date. {ManagedBy}",
        },
        new() {
            Name = "Shokofin/Chronological Season Ordering",
            Description = $"Order seasons in chronological order with indirect relations weighting in on the position of each season. {ManagedBy}",
        },
        new() {
            Name = "Shokofin/",
            Description = $"Order seasons in chronological order while ignoring indirect relations. {ManagedBy}",
        },

        new() {
            Name = "Shokofin/No Merge",
            Description = $"Never merge this series with other series when deciding on what to merge for seasons in Jellyfin. {ManagedBy}",
        },
        new() {
            Name = "Shokofin/Merge Forward",
            Description = $"Merge the current series with the sequel series in Jellyfin. {ManagedBy}",
        },
        new() {
            Name = "Shokofin/Merge Backward",
            Description = $"Merge the current series with the prequel series in Jellyfin. {ManagedBy}",
        },
        new() {
            Name = "Shokofin/Merge with Main Story",
            Description = $"Merge the current side-story with the main-story in Jellyfin. {ManagedBy}",
        },

        new() {
            Name = "Shokofin/Episodes as Specials",
            Description = $"Converts normal episodes to specials in Jellyfin. {ManagedBy}",
        },
        new() {
            Name = "Shokofin/Specials as Episodes",
            Description = $"Converts specials to normal episodes in Jellyfin. {ManagedBy}",
        },
        new() {
            Name = "Shokofin/Specials As Extra Featurettes",
            Description = $"Always convert specials to extra featurettes in Jellyfin. {ManagedBy}",
        },

        new() {
            Name = "Shokofin/Order by AirDate",
            Description = $"Order episodes by air date instead of episode number in Jellyfin. {ManagedBy}",
        },
    ];

    private Task<IReadOnlyDictionary<string, int>> CreatOrGetRequiredTags()
        => _cache.GetOrCreateAsync<IReadOnlyDictionary<string, int>>("tags", async () => {
            var allCustomTags = await apiClient.GetCustomTags().ConfigureAwait(false);
            var outputDict = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var simpleTag in _simpleTags) {
                var localTags = allCustomTags
                    .Where(x => simpleTag.NameRegex is not null
                        ? simpleTag.NameRegex.IsMatch(x.Name) 
                        : string.Equals(simpleTag.Name, x.Name, StringComparison.OrdinalIgnoreCase)
                    )
                    .ToList();
                if (localTags.Count == 0) {
                    var newTag = await apiClient.CreateCustomTag(simpleTag.Name, simpleTag.Description).ConfigureAwait(false);
                    outputDict[simpleTag.Key] = newTag.Id;
                    continue;
                }

                var existingTag = localTags[0];
                if (
                    !string.Equals(simpleTag.Name, existingTag.Name, StringComparison.Ordinal) ||
                    !string.Equals(simpleTag.Description, existingTag.Description, StringComparison.Ordinal)
                ) {
                    existingTag = await apiClient.UpdateCustomTag(existingTag.Id, simpleTag.Name, simpleTag.Description).ConfigureAwait(false);
                }

                if (localTags.Skip(1).ToList() is {Count: > 0 } otherTags) {
                    var seriesIds = await apiClient.GetSeriesIdsWithCustomTag(otherTags.Select(x => x.Id)).ConfigureAwait(false);
                    foreach (var otherTag in otherTags) {
                        await apiClient.RemoveCustomTag(otherTag.Id).ConfigureAwait(false);
                    }
                    foreach (var seriesId in seriesIds) {
                        await apiClient.AddCustomTagToShokoSeries(seriesId, existingTag.Id).ConfigureAwait(false);
                    }
                }

                outputDict[simpleTag.Key] = existingTag.Id;
            }
            return outputDict;
        });

    public async Task<SeriesConfiguration?> GetSeriesConfigurationForId(int shokoSeriesId) {
        if (await apiClient.GetShokoSeries(shokoSeriesId.ToString()).ConfigureAwait(false) is not { })
            return null;

        return await apiManager.GetInternalSeriesConfiguration(shokoSeriesId.ToString());
    }

    public async Task<SeriesConfiguration> UpdateSeriesConfigurationForId(int shokoSeriesId, NullableSeriesConfiguration seriesConfiguration) {
        var config = await GetSeriesConfigurationForId(shokoSeriesId).ConfigureAwait(false) ??
            throw new InvalidOperationException("Series not found.");

        if (seriesConfiguration.Type is not null)
            config.Type = seriesConfiguration.Type.Value;
        if (seriesConfiguration.StructureType is not null)
            config.StructureType = seriesConfiguration.StructureType.Value;
        if (seriesConfiguration.SeasonOrdering is not null)
            config.SeasonOrdering = seriesConfiguration.SeasonOrdering.Value;
        if (seriesConfiguration.MergeOverride is not null)
            config.MergeOverride = seriesConfiguration.MergeOverride.Value;
        if (seriesConfiguration.EpisodeConversion is not null)
            config.EpisodeConversion = seriesConfiguration.EpisodeConversion.Value;
        if (seriesConfiguration.OrderByAirdate is not null)
            config.OrderByAirdate = seriesConfiguration.OrderByAirdate.Value;

        return await UpdateSeriesConfigurationForId(shokoSeriesId, config).ConfigureAwait(false);
    }

    public async Task<SeriesConfiguration> UpdateSeriesConfigurationForId(int shokoSeriesId, SeriesConfiguration seriesConfiguration) {
        if (await apiClient.GetShokoSeries(shokoSeriesId.ToString()).ConfigureAwait(false) is not { } series)
            throw new InvalidOperationException("Series not found.");

        var toAddSet = new HashSet<int>();
        var toRemoveSet = new HashSet<int>();
        var knownTagDict = await CreatOrGetRequiredTags().ConfigureAwait(false);
        var currentTagSet = await apiClient.GetCustomTagsForShokoSeries(shokoSeriesId)
            .ContinueWith(x => x.Result.Select(x => x.Id).ToHashSet())
            .ConfigureAwait(false);

        var seriesTypes = knownTagDict.Where(x => x.Key.StartsWith("/series type/")).ToDictionary(x => x.Key, x => x.Value);
        foreach (var (_, id) in seriesTypes)
            toRemoveSet.Add(id);
        switch (seriesConfiguration.Type) {
            case SeriesType.None:
                break;
            case SeriesType.TVSpecial:
                toRemoveSet.Remove(knownTagDict["/series type/tv special"]);
                toAddSet.Add(knownTagDict["/series type/tv special"]);
                break;
            case SeriesType.MusicVideo:
                toRemoveSet.Remove(knownTagDict["/series type/music video"]);
                toAddSet.Add(knownTagDict["/series type/music video"]);
                break;
            default:
                toRemoveSet.Remove(knownTagDict[$"/series type/{seriesConfiguration.Type.ToString().ToLower()}"]);
                toAddSet.Add(knownTagDict[$"/series type/{seriesConfiguration.Type.ToString().ToLower()}"]);
                break;
        }

        var structureTypes = knownTagDict.Where(x => x.Key.Contains("structure")).ToDictionary(x => x.Key, x => x.Value);
        foreach (var (_, id) in structureTypes)
            toRemoveSet.Add(id);
        switch (seriesConfiguration.StructureType) {
            case SeriesStructureType.TMDB_SeriesAndMovies:
                toRemoveSet.Remove(knownTagDict["/shokofin/tmdb structure"]);
                toAddSet.Add(knownTagDict["/shokofin/tmdb structure"]);
                break;
            case SeriesStructureType.AniDB_Anime:
                toRemoveSet.Remove(knownTagDict["/shokofin/anidb structure"]);
                toAddSet.Add(knownTagDict["/shokofin/anidb structure"]);
                break;
            case SeriesStructureType.Shoko_Groups:
                toRemoveSet.Remove(knownTagDict["/shokofin/shoko structure"]);
                toAddSet.Add(knownTagDict["/shokofin/shoko structure"]);
                break;
        }
        
        var seasonOrderingTypes = knownTagDict.Where(x => x.Key.Contains("season ordering")).ToDictionary(x => x.Key, x => x.Value);
        foreach (var (_, id) in seasonOrderingTypes)
            toRemoveSet.Add(id);
        switch (seriesConfiguration.SeasonOrdering) {
            case Ordering.OrderType.Default:
                toRemoveSet.Remove(knownTagDict["/shokofin/default season ordering"]);
                toAddSet.Add(knownTagDict["/shokofin/default season ordering"]);
                break;
            case Ordering.OrderType.ReleaseDate:
                toRemoveSet.Remove(knownTagDict["/shokofin/release based season ordering"]);
                toAddSet.Add(knownTagDict["/shokofin/release based season ordering"]);
                break;
            case Ordering.OrderType.Chronological:
                toRemoveSet.Remove(knownTagDict["/shokofin/chronological season ordering"]);
                toAddSet.Add(knownTagDict["/shokofin/chronological season ordering"]);
                break;
            case Ordering.OrderType.ChronologicalIgnoreIndirect:
                toRemoveSet.Remove(knownTagDict["/shokofin/simplified chronological season ordering"]);
                toAddSet.Add(knownTagDict["/shokofin/simplified chronological season ordering"]);
                break;
        }

        var mergeTypes = knownTagDict.Where(x => x.Key.Contains("merge")).ToDictionary(x => x.Key, x => x.Value);
        foreach (var (_, id) in mergeTypes)
            toRemoveSet.Add(id);
        switch (seriesConfiguration.MergeOverride) {
            case SeriesMergingOverride.NoMerge:
                toRemoveSet.Remove(knownTagDict["/shokofin/no merge"]);
                toAddSet.Add(knownTagDict["/shokofin/no merge"]);
                break;
            case SeriesMergingOverride.MergeWithMainStory:
                toRemoveSet.Remove(knownTagDict["/shokofin/merge with main story"]);
                toAddSet.Add(knownTagDict["/shokofin/merge with main story"]);
                break;
            case SeriesMergingOverride.MergeForward | SeriesMergingOverride.MergeBackward:
                toRemoveSet.Remove(knownTagDict["/shokofin/merge forward"]);
                toAddSet.Add(knownTagDict["/shokofin/merge forward"]);
                toRemoveSet.Remove(knownTagDict["/shokofin/merge backward"]);
                toAddSet.Add(knownTagDict["/shokofin/merge backward"]);
                break;
            case SeriesMergingOverride.MergeForward:
                toRemoveSet.Remove(knownTagDict["/shokofin/merge forward"]);
                toAddSet.Add(knownTagDict["/shokofin/merge forward"]);
                break;
            case SeriesMergingOverride.MergeBackward:
                toRemoveSet.Remove(knownTagDict["/shokofin/merge backward"]);
                toAddSet.Add(knownTagDict["/shokofin/merge backward"]);
                break;
        }

        var episodeConversions = knownTagDict.Where(x => x.Key.Contains(" as ")).ToDictionary(x => x.Key, x => x.Value);
        foreach (var (_, id) in episodeConversions)
            toRemoveSet.Add(id);
        switch (seriesConfiguration.EpisodeConversion) {
            case SeriesEpisodeConversion.EpisodesAsSpecials:
                toRemoveSet.Remove(knownTagDict["/shokofin/episodes as specials"]);
                toAddSet.Add(knownTagDict["/shokofin/episodes as specials"]);
                break;
            case SeriesEpisodeConversion.SpecialsAsEpisodes:
                toRemoveSet.Remove(knownTagDict["/shokofin/specials as episodes"]);
                toAddSet.Add(knownTagDict["/shokofin/specials as episodes"]);
                break;
            case SeriesEpisodeConversion.SpecialsAsExtraFeaturettes:
                toRemoveSet.Remove(knownTagDict["/shokofin/specials as extra featurettes"]);
                toAddSet.Add(knownTagDict["/shokofin/specials as extra featurettes"]);
                break;
        }

        if (seriesConfiguration.OrderByAirdate) {
            toAddSet.Add(knownTagDict["/shokofin/order by airdate"]);
        }
        else {
            toRemoveSet.Add(knownTagDict["/shokofin/order by airdate"]);
        }

        toAddSet.ExceptWith(currentTagSet);
        toRemoveSet.IntersectWith(currentTagSet);

        foreach (var tagToRemove in toRemoveSet)
            await apiClient.RemoveCustomTagFromShokoSeries(shokoSeriesId, tagToRemove).ConfigureAwait(false);
        foreach (var tagToAdd in toAddSet)
            await apiClient.AddCustomTagToShokoSeries(shokoSeriesId, tagToAdd).ConfigureAwait(false);

        return seriesConfiguration;
    }

    /// <summary>
    /// A simple tag with a name and description. Used to determine which tags
    /// exist in Shoko, and which tags need to be created.
    /// </summary>
    class SimpleTag {
        /// <summary>
        /// Regular expression to match against the name, if any.
        /// </summary>
        public Regex? NameRegex { get; init; }

        /// <summary>
        /// Properly cased name of the tag.
        /// </summary>
        public required string Name { get; init; }

        /// <summary>
        /// Proper description of the tag.
        /// </summary>
        public required string Description { get; init; }

        /// <summary>
        /// The namespaced key for the tag.
        /// </summary>
        public string Key => $"/{Name.ToLower()}";
    }
}
