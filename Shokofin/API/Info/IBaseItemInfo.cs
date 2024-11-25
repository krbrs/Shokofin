using System.Collections.Generic;
using Shokofin.API.Models;

namespace Shokofin.API.Info;

public interface IBaseItemInfo {
    string Id { get; }

    string DefaultTitle { get; }

    IReadOnlyList<Title> Titles { get; }

    string? DefaultOverview { get; }

    IReadOnlyList<TextOverview> Overviews { get; }

    string? OriginalLanguageCode { get; }
}
