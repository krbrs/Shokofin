using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using Shokofin.API;
using Shokofin.ExternalIds;

using RatingType = MediaBrowser.Model.Dto.RatingType;

namespace Shokofin.Providers;

public class ImageProvider(IHttpClientFactory _httpClientFactory, ILogger<ImageProvider> _logger, ShokoApiManager _apiManager, ShokoIdLookup _lookup) : IRemoteImageProvider, IHasOrder {
    public string Name => Plugin.MetadataProviderName;

    public int Order => 0;

    public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken) {
        var list = new List<RemoteImageInfo>();
        var metadataLanguage = item.GetPreferredMetadataLanguage();
        var baseKind = item.GetBaseItemKind();
        var trackerId = Plugin.Instance.Tracker.Add($"Providing images for {baseKind} \"{item.Name}\". (Path=\"{item.Path}\")");
        try {
            switch (item) {
                case Episode episode: {
                    var (fileInfo, seasonInfo, _) = await _apiManager.GetFileInfoByPath(episode.Path).ConfigureAwait(false);
                    if (fileInfo is not { EpisodeList.Count: > 0 } || seasonInfo is null)
                        break;

                    var episodeInfo = fileInfo.EpisodeList[0].Episode;
                    var sortPreferred = Plugin.Instance.Configuration.RespectPreferredImagePerStructureType.Contains(seasonInfo.StructureType);
                    if (await episodeInfo.GetImages(cancellationToken).ConfigureAwait(false) is { } episodeImages)
                        AddImagesForEpisode(ref list, episodeImages, metadataLanguage, sortPreferred);

                    _logger.LogInformation("Getting {Count} images for episode {EpisodeName} (Episode={EpisodeId},Language={MetadataLanguage})", list.Count, episode.Name, episodeInfo.Id, metadataLanguage);
                    break;
                }
                case Series series: {
                    if (_lookup.TryGetSeasonIdFor(series, out var seasonId)) {
                        if (await _apiManager.GetShowInfoBySeasonId(seasonId).ConfigureAwait(false) is { } showInfo) {
                            var images = await showInfo.GetImages(cancellationToken).ConfigureAwait(false);
                            var sortPreferred = Plugin.Instance.Configuration.RespectPreferredImagePerStructureType.Contains(showInfo.DefaultSeason.StructureType);
                            AddImagesForSeries(ref list, images, metadataLanguage, sortPreferred);
                            sortPreferred = false;

                            // Also attach any images linked to the "seasons" if it's not a standalone series.
                            if (!showInfo.IsStandalone) {
                                foreach (var seasonInfo in showInfo.SeasonList) {
                                    var seriesImages = await seasonInfo.GetImages(cancellationToken).ConfigureAwait(false);
                                    if (seriesImages is not null) {
                                        AddImagesForSeries(ref list, seriesImages, metadataLanguage, sortPreferred);
                                        sortPreferred = false;
                                    }
                                }
                            }
                        }

                        _logger.LogInformation("Getting {Count} images for series {SeriesName} (MainSeason={MainSeasonId},Language={MetadataLanguage})", list.Count, series.Name, seasonId, metadataLanguage);
                    }
                    break;
                }
                case Season season: {
                    if (_lookup.TryGetSeasonIdFor(season, out var seasonId)) {
                        if (await _apiManager.GetSeasonInfo(seasonId).ConfigureAwait(false) is not { } seasonInfo)
                            break;

                        var seriesImages = await seasonInfo.GetImages(cancellationToken).ConfigureAwait(false);
                        var sortPreferred = Plugin.Instance.Configuration.RespectPreferredImagePerStructureType.Contains(seasonInfo.StructureType);
                        AddImagesForSeries(ref list, seriesImages, metadataLanguage, sortPreferred);
                        _logger.LogInformation("Getting {Count} images for season {SeasonNumber} in {SeriesName} (Season={SeasonId},Language={MetadataLanguage})", list.Count, season.IndexNumber, season.SeriesName, seasonId, metadataLanguage);
                    }
                    break;
                }
                case Movie movie: {
                    var (fileInfo, seasonInfo, _) = await _apiManager.GetFileInfoByPath(movie.Path).ConfigureAwait(false);
                    if (fileInfo is not { EpisodeList.Count: > 0 } || seasonInfo is null)
                        break;

                    var episodeInfo = fileInfo.EpisodeList[0].Episode;
                    var sortPreferred = Plugin.Instance.Configuration.RespectPreferredImagePerStructureType.Contains(seasonInfo.StructureType);
                    if (await episodeInfo.GetImages(cancellationToken).ConfigureAwait(false) is { } episodeImages)
                        AddImagesForSeries(ref list, episodeImages, metadataLanguage, sortPreferred, BaseItemKind.Movie);

                    _logger.LogInformation("Getting {Count} images for movie {MovieName} (Episode={EpisodeId},Language={MetadataLanguage})", list.Count, movie.Name, episodeInfo.Id, metadataLanguage);
                    break;
                }
                case BoxSet collection: {
                    string? collectionId = null;
                    if (!collection.TryGetProviderId(ProviderNames.ShokoCollectionForSeries, out var seasonId) &&
                        collection.TryGetProviderId(ProviderNames.ShokoCollectionForGroup, out collectionId) &&
                        await _apiManager.GetCollectionInfo(collectionId).ConfigureAwait(false) is { } collectionInfo)
                        seasonId = collectionInfo.MainSeasonId;

                    if (!string.IsNullOrEmpty(seasonId) && await _apiManager.GetShowInfoBySeasonId(seasonId).ConfigureAwait(false) is { } showInfo) {
                        var showImages = await showInfo.GetImages(cancellationToken).ConfigureAwait(false);
                        var sortPreferred = Plugin.Instance.Configuration.RespectPreferredImagePerStructureType.Contains(showInfo.DefaultSeason.StructureType);
                        AddImagesForSeries(ref list, showImages, metadataLanguage, sortPreferred);
                    }

                    _logger.LogInformation("Getting {Count} images for collection {CollectionName} (Collection={CollectionId},Season={SeasonId},Language={MetadataLanguage})", list.Count, collection.Name, collectionId, collectionId is null ? seasonId : null, metadataLanguage);
                    break;
                }
            }
            list =  list
                .DistinctBy(image => image.Url)
                .ToList();
            return list;
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Threw unexpectedly for {BaseKind} {Name}; {Message}", baseKind, item.Name, ex.Message);
            return list;
        }
        finally {
            Plugin.Instance.Tracker.Remove(trackerId);
        }
    }

    public static void AddImagesForEpisode(ref List<RemoteImageInfo> list, API.Models.EpisodeImages images, string metadataLanguage, bool sortList) {
        IEnumerable<API.Models.Image> imagesList = sortList
            ? images.Thumbnails.Concat(images.Backdrops).OrderByDescending(image => image.IsPreferred).ThenByDescending(image => image.Type is API.Models.ImageType.Thumbnail)
            : images.Thumbnails.Concat(images.Backdrops).OrderByDescending(image => image.Type is API.Models.ImageType.Thumbnail);
        foreach (var image in imagesList)
            AddImage(ref list, ImageType.Primary, image, metadataLanguage, overridePreferred: sortList);
    }

    private static void AddImagesForSeries(ref List<RemoteImageInfo> list, API.Models.Images images, string metadataLanguage, bool sortList, BaseItemKind baseKind = BaseItemKind.Series) {
        IEnumerable<API.Models.Image> imagesList = sortList
            ? images.Posters.OrderByDescending(image => image.IsPreferred)
            : images.Posters;
        foreach (var image in imagesList)
            AddImage(ref list, ImageType.Primary, image, sortList ? metadataLanguage : null, baseKind, sortList);

        imagesList = sortList
            ? images.Backdrops.OrderByDescending(image => image.IsPreferred)
            : images.Backdrops;
        foreach (var image in imagesList)
            AddImage(ref list, ImageType.Backdrop, image, sortList ? metadataLanguage : null, baseKind, sortList);

        imagesList = sortList
            ? images.Banners.OrderByDescending(image => image.IsPreferred)
            : images.Banners;
        foreach (var image in imagesList)
            AddImage(ref list, ImageType.Banner, image, metadataLanguage, baseKind, sortList);

        imagesList = sortList
            ? images.Logos.OrderByDescending(image => image.IsPreferred)
            : images.Logos;
        foreach (var image in imagesList)
            AddImage(ref list, ImageType.Logo, image, metadataLanguage, baseKind, sortList);
    }

    private static void AddImage(ref List<RemoteImageInfo> list, ImageType imageType, API.Models.Image? image, string? metadataLanguage, BaseItemKind baseKind = BaseItemKind.Series, bool overridePreferred = false) {
        if (image is not { IsAvailable: true })
            return;

        var imageDto = new RemoteImageInfo {
            ProviderName = $"{image.Source.ToString().Replace("TMDB", "TheMovieDb")} ({Plugin.MetadataProviderName})",
            Type = imageType,
            Width = image.Width,
            Height = image.Height,
            Url = image.ToURLString(),
        };
        if (UseLanguageCode(imageType, baseKind))
            imageDto.Language = !string.IsNullOrEmpty(metadataLanguage) && overridePreferred && image.IsPreferred ? metadataLanguage : image.LanguageCode;

        if (UseCommunityRating(imageType, baseKind)) {
            if (overridePreferred && image.IsPreferred) {
                imageDto.CommunityRating = 10;
                imageDto.VoteCount = 1337;
                imageDto.RatingType = RatingType.Score;
            }
            else if (image.CommunityRating is { } rating) {
                imageDto.CommunityRating = rating.ToFloat();
                imageDto.VoteCount = rating.Votes;
                imageDto.RatingType = RatingType.Score;
            }
        }

        list.Add(imageDto);
    }

    private static bool UseLanguageCode(ImageType imageType, BaseItemKind baseKind) {
        var array = baseKind switch {
            BaseItemKind.Movie => Plugin.Instance.Configuration.AddImageLanguageCodeForMovies,
            BaseItemKind.Series => Plugin.Instance.Configuration.AddImageLanguageCodeForShows,
            _ => [],
        };
        return array.Contains(imageType);
    }

    private static bool UseCommunityRating(ImageType imageType, BaseItemKind baseKind) {
        var array = baseKind switch {
            BaseItemKind.Movie => Plugin.Instance.Configuration.AddImageCommunityRatingForMovies,
            BaseItemKind.Series => Plugin.Instance.Configuration.AddImageCommunityRatingForShows,
            _ => [],
        };
        return array.Contains(imageType);
    }

    public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
        => [ImageType.Primary, ImageType.Backdrop, ImageType.Banner, ImageType.Logo];

    public bool Supports(BaseItem item)
        => item is Series or Season or Episode or Movie or BoxSet;

    public async Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken) {
        var index = url.IndexOf("Plugin/Shokofin/Host");
        if (index is -1)
            return new HttpResponseMessage(System.Net.HttpStatusCode.BadRequest);
        url = $"{Plugin.Instance.Configuration.Url}/api/v3{url[(index + 20)..]}";
        return await _httpClientFactory.CreateClient().GetAsync(url, cancellationToken).ConfigureAwait(false);
    }
}

