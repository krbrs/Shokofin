using System;

namespace Shokofin.Configuration;

/// <summary>
/// Determine how to handle series merging.
/// </summary>
[Flags]
public enum SeriesMergingOverride {
    /// <summary>
    /// Follow the global series merging settings.
    /// </summary>
    None = 0,

    /// <summary>
    /// Do not merge this series with any other.
    /// </summary>
    NoMerge = 1,

    /// <summary>
    /// Attempt to merge this series with the sequel series.
    /// </summary>
    MergeForward = 2,

    /// <summary>
    /// Attempt to merge this series with the prequel series.
    /// </summary>
    MergeBackward = 4,

    /// <summary>
    /// Attempt to merge this series with the main story series.
    /// </summary>
    MergeWithMainStory = 8,
}
