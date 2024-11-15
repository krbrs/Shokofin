using System.Text.Json.Serialization;

namespace Shokofin.API.Models;

public class TextOverview
{
    /// <summary>
    /// The title.
    /// </summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// 3-digit language code (x-jat, etc. are exceptions)
    /// </summary>
    [JsonPropertyName("Language")]
    public string LanguageCode { get; set; } = "unk";

    /// <summary>
    /// True if this is the default title for the entry.
    /// </summary>
    [JsonPropertyName("Default")]
    public bool IsDefault { get; set; }

    /// <summary>
    /// True if this is the preferred title for the entry.
    /// </summary>
    [JsonPropertyName("Preferred")]
    public bool IsPreferred { get; set; }

    /// <summary>
    /// AniDB, TMDB, AniList, etc.
    /// </summary>
    public string Source { get; set; } = "Unknown";
}
