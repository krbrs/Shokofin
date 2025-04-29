using System.Collections.Generic;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Shokofin.ExternalIds;

public class AnidbEpisodeId : IExternalId, IExternalUrlProvider {
    #region IExternalId Implementation

    string IExternalId.ProviderName => ProviderNames.Anidb;

    string IExternalId.Key => ProviderNames.Anidb;

    ExternalIdMediaType? IExternalId.Type => ExternalIdMediaType.Episode;

    string? IExternalId.UrlFormatString => null;

    public bool Supports(IHasProviderIds item) => item is Episode;

    #endregion

    #region IExternalUrlProvider Implementation

    string IExternalUrlProvider.Name => ProviderNames.Anidb;

    IEnumerable<string> IExternalUrlProvider.GetExternalUrls(BaseItem item)
    {
        if (Supports(item) && item.TryGetProviderId(ProviderNames.Anidb, out var episodeId))
            yield return $"https://anidb.net/episode/{episodeId}";
    }

    #endregion
}
