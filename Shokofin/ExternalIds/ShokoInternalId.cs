using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Shokofin.ExternalIds;

public class ShokoInternalId : IExternalId {
    public static string Name => MetadataProvider.Custom.ToString();

    public const string Namespace = "shoko://series/";

    public bool Supports(IHasProviderIds item)
        => item is Series or Season;

    public string ProviderName
        => "Shokofin Internal";

    public string Key
        => Name;

    public ExternalIdMediaType? Type
        => null;

    public string? UrlFormatString => null;
}
