using System.Collections.Generic;
using System.Linq;
using Shokofin.API.Models;

namespace Shokofin.API.Info;

public class FileInfo(File file, string seriesId, IReadOnlyList<(EpisodeInfo Episode, CrossReference.EpisodeCrossReferenceIDs CrossReference, string Id)> episodeList) {
    public string Id { get; init; } = file.Id.ToString();

    public string SeriesId { get; init; } = seriesId;

    public MediaBrowser.Model.Entities.ExtraType? ExtraType { get; init; } = episodeList.FirstOrDefault(tuple => tuple.Episode.ExtraType != null).Episode?.ExtraType;

    public File Shoko { get; init; } = file;

    public IReadOnlyList<(EpisodeInfo Episode, CrossReference.EpisodeCrossReferenceIDs CrossReference, string Id)> EpisodeList { get; init; } = episodeList;
}
