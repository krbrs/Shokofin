using System.Text.Json.Serialization;

namespace Shokofin.API.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SeriesType {
    /// <summary>
    /// The series type is unknown.
    /// </summary>
    Unknown,
    /// <summary>
    /// A catch-all type for future extensions when a provider can't use a current episode type, but knows what the future type should be.
    /// </summary>
    Other,
    /// <summary>
    /// Standard TV series.
    /// </summary>
    TV,
    /// <summary>
    /// TV special.
    /// </summary>
    TVSpecial,
    /// <summary>
    /// Web series.
    /// </summary>
    Web,
    /// <summary>
    /// All movies, regardless of source (e.g. web or theater)
    /// </summary>
    Movie,
    /// <summary>
    /// Original Video Animations, AKA standalone releases that don't air on TV or the web.
    /// </summary>
    OVA,
}
