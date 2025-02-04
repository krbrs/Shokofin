
using System;

namespace Shokofin.Configuration;

[Flags]
public enum SeriesMergingOverride {
    None = 0,
    NoMerge = 1,
    MergeForward = 2,
    MergeBackward = 4,
    MergeWithMainStory = 8,
}