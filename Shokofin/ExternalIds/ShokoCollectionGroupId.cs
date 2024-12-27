using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Shokofin.ExternalIds;

public class ShokoCollectionGroupId : IExternalId {
    public const string Name = "ShokoCollectionGroup";

    public bool Supports(IHasProviderIds item)
        => item is BoxSet;

    public string ProviderName
        => "Shoko Group";

    public string Key
        => Name;

    public ExternalIdMediaType? Type
        => null;

    public string UrlFormatString
        => $"{Plugin.Instance.Configuration.PrettyUrl}/webui/collection/group/{{0}}";
}
