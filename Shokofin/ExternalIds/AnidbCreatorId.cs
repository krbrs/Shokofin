using System.Collections.Generic;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Shokofin.ExternalIds;

public class AnidbCreatorId : IExternalId, IExternalUrlProvider {
    #region IExternalId Implementation

    string IExternalId.ProviderName => ProviderNames.Anidb;

    string IExternalId.Key => ProviderNames.Anidb;

    ExternalIdMediaType? IExternalId.Type => ExternalIdMediaType.Person;

    string? IExternalId.UrlFormatString => null;

    public bool Supports(IHasProviderIds item) => item is Person;

    #endregion

    #region IExternalUrlProvider Implementation

    string IExternalUrlProvider.Name => ProviderNames.Anidb;

    IEnumerable<string> IExternalUrlProvider.GetExternalUrls(BaseItem item)
    {
        if (Supports(item) && item.TryGetProviderId(ProviderNames.Anidb, out var episodeId))
            yield return $"https://anidb.net/creator/{episodeId}";
    }

    #endregion
}
