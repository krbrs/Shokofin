
using System;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Shokofin.Resolvers.Models;

public class LinkGenerationResult {
    private DateTime CreatedAt { get; init; } = DateTime.Now;

    public ConcurrentBag<string> Paths { get; init; } = [];

    public ConcurrentBag<string> RemovedPaths { get; init; } = [];

    public int Total =>
        TotalVideos + TotalSubtitles + TotalTrickplayDirectories;

    public int Created =>
        CreatedVideos + CreatedSubtitles + CreatedTrickplayDirectories;

    public int Fixed =>
        FixedVideos + FixedSubtitles + FixedTrickplayDirectories;

    public int Skipped =>
        SkippedVideos + SkippedSubtitles + SkippedTrickplayDirectories;

    public int Removed =>
        RemovedVideos + RemovedSubtitles + RemovedNfos + RemovedTrickplayDirectories;

    public int TotalVideos =>
        CreatedVideos + FixedVideos + SkippedVideos;

    public int CreatedVideos { get; set; }

    public int FixedVideos { get; set; }

    public int SkippedVideos { get; set; }

    public int RemovedVideos { get; set; }

    public int TotalSubtitles =>
        CreatedSubtitles + FixedSubtitles + SkippedSubtitles;

    public int CreatedSubtitles { get; set; }

    public int FixedSubtitles { get; set; }

    public int SkippedSubtitles { get; set; }

    public int RemovedSubtitles { get; set; }

    public int TotalTrickplayDirectories =>
        CreatedTrickplayDirectories + FixedTrickplayDirectories + SkippedTrickplayDirectories;

    public int CreatedTrickplayDirectories { get; set; }

    public int FixedTrickplayDirectories { get; set; }

    public int SkippedTrickplayDirectories { get; set; }

    public int RemovedTrickplayDirectories { get; set; }

    public int RemovedNfos { get; set; }

    public void Print(ILogger logger, string path) {
        var timeSpent = DateTime.Now - CreatedAt;
        logger.LogInformation(
            "Created {CreatedTotal} ({CreatedMedia},{CreatedSubtitles},{CreatedTrickplay}), fixed {FixedTotal} ({FixedMedia},{FixedSubtitles},{FixedTrickplay}), skipped {SkippedTotal} ({SkippedMedia},{SkippedSubtitles},{SkippedTrickplay}), and removed {RemovedTotal} ({RemovedMedia},{RemovedSubtitles},{RemovedTrickplay},{RemovedNFO}) entries in folder at {Path} in {TimeSpan} (Total={Total})",
            Created,
            CreatedVideos,
            CreatedSubtitles,
            CreatedTrickplayDirectories,
            Fixed,
            FixedVideos,
            FixedSubtitles,
            FixedTrickplayDirectories,
            Skipped,
            SkippedVideos,
            SkippedSubtitles,
            SkippedTrickplayDirectories,
            Removed,
            RemovedVideos,
            RemovedSubtitles,
            RemovedTrickplayDirectories,
            RemovedNfos,
            path,
            timeSpent,
            Total
        );
    }

    public static LinkGenerationResult operator +(LinkGenerationResult a, LinkGenerationResult b) {
        // Re-use the same instance so the parallel execution will share the same bag.
        var paths = a.Paths;
        foreach (var path in b.Paths)
            paths.Add(path);

        var removedPaths = a.RemovedPaths;
        foreach (var path in b.RemovedPaths)
            removedPaths.Add(path);

        return new() {
            CreatedAt = a.CreatedAt,
            Paths = paths,
            RemovedPaths = removedPaths,
            CreatedVideos = a.CreatedVideos + b.CreatedVideos,
            FixedVideos = a.FixedVideos + b.FixedVideos,
            SkippedVideos = a.SkippedVideos + b.SkippedVideos,
            RemovedVideos = a.RemovedVideos + b.RemovedVideos,
            CreatedSubtitles = a.CreatedSubtitles + b.CreatedSubtitles,
            FixedSubtitles = a.FixedSubtitles + b.FixedSubtitles,
            SkippedSubtitles = a.SkippedSubtitles + b.SkippedSubtitles,
            RemovedSubtitles = a.RemovedSubtitles + b.RemovedSubtitles,
            CreatedTrickplayDirectories = a.CreatedTrickplayDirectories + b.CreatedTrickplayDirectories,
            FixedTrickplayDirectories = a.FixedTrickplayDirectories + b.FixedTrickplayDirectories,
            SkippedTrickplayDirectories = a.SkippedTrickplayDirectories + b.SkippedTrickplayDirectories,
            RemovedTrickplayDirectories = a.RemovedTrickplayDirectories + b.RemovedTrickplayDirectories,
            RemovedNfos = a.RemovedNfos + b.RemovedNfos,
        };
    }
}