using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Shokofin.ExternalIds;

public class AnidbCreatorId : IExternalId {
    public static string Name => "AniDB";

    public bool Supports(IHasProviderIds item)
        => item is Person;

    public string ProviderName
        => Name;

    public string Key
        => Name;

    public ExternalIdMediaType? Type
        => null;

    public virtual string? UrlFormatString => "https://anidb.net/creator/{0}";
}
