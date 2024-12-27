using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Shokofin.ExternalIds;

public class AnidbAnimeId : IExternalId {
    public static string Name => "AniDB";

    public bool Supports(IHasProviderIds item)
        => item is Series or Season or Movie;

    public string ProviderName
        => Name;

    public string Key
        => Name;

    public ExternalIdMediaType? Type
        => null;

    public string? UrlFormatString => "https://anidb.net/anime/{0}";
}
