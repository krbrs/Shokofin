using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Shokofin.API.Models;
using Shokofin.API.Models.Shoko;
using Shokofin.API.Models.TMDB;
using Shokofin.Utils;

namespace Shokofin.API;

/// <summary>
/// All API calls to Shoko needs to go through this gateway.
/// </summary>
public class ShokoApiClient : IDisposable {
    private readonly HttpClient _httpClient;

    private readonly ILogger<ShokoApiClient> _logger;

    private readonly GuardedMemoryCache _cache;

    public ShokoApiClient(ILogger<ShokoApiClient> logger) {
        _httpClient = new HttpClient {
            Timeout = TimeSpan.FromMinutes(10),
        };
        _logger = logger;
        _cache = new(logger, new() { ExpirationScanFrequency = TimeSpan.FromMinutes(25) }, new() { SlidingExpiration = new(2, 30, 0) });
        Plugin.Instance.Tracker.Stalled += OnTrackerStalled;
    }

    ~ShokoApiClient() {
        Plugin.Instance.Tracker.Stalled -= OnTrackerStalled;
    }

    private void OnTrackerStalled(object? sender, EventArgs eventArgs)
        => Clear();

    public void Clear() {
        _logger.LogDebug("Clearing data…");
        _cache.Clear();
    }

    public void Dispose() {
        GC.SuppressFinalize(this);
        _httpClient.Dispose();
        _cache.Dispose();
    }

    #region Base Implementation

    private async Task<ReturnType?> GetOrNull<ReturnType>(string url, string? apiKey = null, bool skipCache = false, CancellationToken cancellationToken = default) {
        try {
            return await Get<ReturnType>(url, HttpMethod.Get, apiKey, skipCache, cancellationToken).ConfigureAwait(false);
        }
        catch (ApiException e) when (e.StatusCode == HttpStatusCode.NotFound) {
            return default;
        }
    }

    private Task<ReturnType> Get<ReturnType>(string url, string? apiKey = null, bool skipCache = false, CancellationToken cancellationToken = default)
        => Get<ReturnType>(url, HttpMethod.Get, apiKey, skipCache, cancellationToken);

    private async Task<ReturnType> Get<ReturnType>(string url, HttpMethod method, string? apiKey = null, bool skipCache = false, CancellationToken cancellationToken = default) {
        if (skipCache) {
            _logger.LogTrace("Creating object for {Method} {URL}", method, url);
            var response = await Get(url, method, apiKey, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK)
                throw ApiException.FromResponse(response);
            var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            responseStream.Seek(0, System.IO.SeekOrigin.Begin);
            var value = await JsonSerializer.DeserializeAsync<ReturnType>(responseStream, cancellationToken: cancellationToken).ConfigureAwait(false) ??
                throw new ApiException(response.StatusCode, nameof(ShokoApiClient), "Unexpected null return value.");
            return value;
        }

        return await _cache.GetOrCreateAsync(
            $"apiKey={apiKey ?? "default"},method={method},url={url},object",
            (_) => _logger.LogTrace("Reusing object for {Method} {URL}", method, url),
            async () => {
                _logger.LogTrace("Creating object for {Method} {URL}", method, url);
                var response = await Get(url, method, apiKey).ConfigureAwait(false);
                if (response.StatusCode != HttpStatusCode.OK)
                    throw ApiException.FromResponse(response);
                var responseStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                responseStream.Seek(0, System.IO.SeekOrigin.Begin);
                var value = await JsonSerializer.DeserializeAsync<ReturnType>(responseStream).ConfigureAwait(false) ??
                    throw new ApiException(response.StatusCode, nameof(ShokoApiClient), "Unexpected null return value.");
                return value;
            }
        ).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> Get(string url, HttpMethod method, string? apiKey = null, bool skipApiKey = false, CancellationToken cancellationToken = default) {
        // Use the default key if no key was provided.
        apiKey ??= Plugin.Instance.Configuration.ApiKey;

        // Check if we have a key to use.
        if (string.IsNullOrEmpty(apiKey) && !skipApiKey)
            throw new HttpRequestException("Unable to call the API before an connection is established to Shoko Server!", null, HttpStatusCode.BadRequest);

        var version = Plugin.Instance.Configuration.ServerVersion;
        if (version == null) {
            version = await GetVersion().ConfigureAwait(false)
                ?? throw new HttpRequestException("Unable to call the API before an connection is established to Shoko Server!", null, HttpStatusCode.BadRequest);

            Plugin.Instance.Configuration.ServerVersion = version;
            Plugin.Instance.UpdateConfiguration();
        }

        try {
            _logger.LogTrace("Trying to {Method} {URL}", method, url);
            var remoteUrl = string.Concat(Plugin.Instance.Configuration.Url, url);

            using var requestMessage = new HttpRequestMessage(method, remoteUrl);
            requestMessage.Content = new StringContent(string.Empty);
            if (!string.IsNullOrEmpty(apiKey))
                requestMessage.Headers.Add("apikey", apiKey);
            var timeStart = DateTime.UtcNow;
            var response = await _httpClient.SendAsync(requestMessage, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                throw new HttpRequestException("Invalid or expired API Token. Please reconnect the plugin to Shoko Server by resetting the connection or deleting and re-adding the user in the plugin settings.", null, HttpStatusCode.Unauthorized);
            _logger.LogTrace("API returned response with status code {StatusCode} for {Method} {URL} in {Elapsed}", response.StatusCode, method, url, DateTime.UtcNow - timeStart);
            return response;
        }
        catch (HttpRequestException ex) {
            _logger.LogWarning(ex, "Unable to connect to complete the request to Shoko.");
            throw;
        }
    }

    private Task<ReturnType> Post<Type, ReturnType>(string url, Type body, string? apiKey = null)
        => Post<Type, ReturnType>(url, HttpMethod.Post, body, apiKey);

    private async Task<ReturnType> Post<Type, ReturnType>(string url, HttpMethod method, Type body, string? apiKey = null) {
        var response = await Post(url, method, body, apiKey).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.OK)
            throw ApiException.FromResponse(response);
        var responseStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        var value = await JsonSerializer.DeserializeAsync<ReturnType>(responseStream).ConfigureAwait(false) ??
            throw new ApiException(response.StatusCode, nameof(ShokoApiClient), "Unexpected null return value.");
        return value;
    }

    private async Task<HttpResponseMessage> Post<Type>(string url, HttpMethod method, Type body, string? apiKey = null) {
        // Use the default key if no key was provided.
        apiKey ??= Plugin.Instance.Configuration.ApiKey;

        // Check if we have a key to use.
        if (string.IsNullOrEmpty(apiKey))
            throw new HttpRequestException("Unable to call the API before an connection is established to Shoko Server!", null, HttpStatusCode.BadRequest);

        var version = Plugin.Instance.Configuration.ServerVersion;
        if (version == null) {
            version = await GetVersion().ConfigureAwait(false)
                ?? throw new HttpRequestException("Unable to call the API before an connection is established to Shoko Server!", null, HttpStatusCode.BadRequest);

            Plugin.Instance.Configuration.ServerVersion = version;
            Plugin.Instance.UpdateConfiguration();
        }

        try {
            _logger.LogTrace("Trying to get {URL}", url);
            var remoteUrl = string.Concat(Plugin.Instance.Configuration.Url, url);

            if (method == HttpMethod.Get)
                throw new HttpRequestException("Get requests cannot contain a body.");

            if (method == HttpMethod.Head)
                throw new HttpRequestException("Head requests cannot contain a body.");

            using var requestMessage = new HttpRequestMessage(method, remoteUrl);
            requestMessage.Content = new StringContent(JsonSerializer.Serialize<Type>(body), Encoding.UTF8, "application/json");
            requestMessage.Headers.Add("apikey", apiKey);
            var response = await _httpClient.SendAsync(requestMessage).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                throw new HttpRequestException("Invalid or expired API Token. Please reconnect the plugin to Shoko Server by resetting the connection or deleting and re-adding the user in the plugin settings.", null, HttpStatusCode.Unauthorized);
            _logger.LogTrace("API returned response with status code {StatusCode}", response.StatusCode);
            return response;
        }
        catch (HttpRequestException ex) {
            _logger.LogWarning(ex, "Unable to connect to complete the request to Shoko.");
            throw;
        }
    }

    #endregion Base Implementation

    #region Authentication

    public async Task<ApiKey?> GetApiKey(string username, string password, bool forUser = false) {
        var version = Plugin.Instance.Configuration.ServerVersion;
        if (version == null) {
            version = await GetVersion().ConfigureAwait(false)
                ?? throw new HttpRequestException("Unable to connect to Shoko Server to read the version.", null, HttpStatusCode.BadGateway);

            Plugin.Instance.Configuration.ServerVersion = version;
            Plugin.Instance.UpdateConfiguration();
        }

        var postData = JsonSerializer.Serialize(new Dictionary<string, string> { {"user", username}, {"pass", password}, {"device", forUser ? "Shoko Jellyfin Plugin (Shokofin) - User Key" : "Shoko Jellyfin Plugin (Shokofin)"},
        });
        var apiBaseUrl = Plugin.Instance.Configuration.Url;
        var response = await _httpClient.PostAsync($"{apiBaseUrl}/api/auth", new StringContent(postData, Encoding.UTF8, "application/json")).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.OK)
            return null;

        return await JsonSerializer.DeserializeAsync<ApiKey>(response.Content.ReadAsStreamAsync().Result).ConfigureAwait(false);
    }

    #endregion

    #region Version

    public async Task<ComponentVersion?> GetVersion() {
        try {
            var apiBaseUrl = Plugin.Instance.Configuration.Url;
            var source = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var response = await _httpClient.GetAsync($"{apiBaseUrl}/api/v3/Init/Version", source.Token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.OK) {
                var componentVersionSet = await JsonSerializer.DeserializeAsync<ComponentVersionSet>(response.Content.ReadAsStreamAsync().Result).ConfigureAwait(false);
                return componentVersionSet?.Server;
            }
        }
        catch (Exception e) {
            _logger.LogTrace(e, "Unable to connect to Shoko Server to read the version. Exception; {e}", e.Message);
            return null;
        }

        return null;
    }

    #endregion

    #region Image

    public Task<HttpResponseMessage> GetImageAsync(ImageSource imageSource, ImageType imageType, int imageId)
        => Get($"/api/v3/Image/{imageSource}/{imageType}/{imageId}", HttpMethod.Get, null, true);

    #endregion

    #region Import Folder

    public Task<ImportFolder?> GetImportFolder(int importFolderId)
        => GetOrNull<ImportFolder>($"/api/v3/ImportFolder/{importFolderId}");

    public async Task<ListResult<File>> GetFilesInImportFolder(int importFolderId, string subPath, int page = 1)
        => await GetOrNull<ListResult<File>>($"/api/v3/ImportFolder/{importFolderId}/File?pageSize=1000&page={page}&include=XRefs&folderPath={Uri.EscapeDataString(subPath)}").ConfigureAwait(false) ?? new();

    #endregion

    #region File

    public Task<File?> GetFile(string fileId)
        => GetOrNull<File>($"/api/v3/File/{fileId}?include=XRefs&includeDataFrom=AniDB");

    public Task<File?> GetFileByEd2kAndFileSize(string ed2k, long fileSize)
        => GetOrNull<File>($"/api/v3/File/Hash/ED2K?hash={Uri.EscapeDataString(ed2k)}&size={fileSize}&includeDataFrom=AniDB");

    public Task<IReadOnlyList<File>> GetFileByPath(string relativePath)
        => Get<IReadOnlyList<File>>($"/api/v3/File/PathEndsWith?path={Uri.EscapeDataString(relativePath)}&includeDataFrom=AniDB&limit=1");

    #region File User Stats

    public async Task<File.UserStats?> GetFileUserStats(string fileId, string? apiKey = null) {
        try {
            return await Get<File.UserStats>($"/api/v3/File/{fileId}/UserStats", apiKey, true).ConfigureAwait(false);
        }
        catch (ApiException e) when (e.StatusCode is HttpStatusCode.NotFound) {
            // File user stats were not found.
            if (!e.Message.Contains("FileUserStats"))
                _logger.LogWarning("Unable to find user stats for a file that doesn't exist. (File={FileID})", fileId);
            return null;
        }
    }

    public Task<File.UserStats> PutFileUserStats(string fileId, File.UserStats userStats, string? apiKey = null)
        => Post<File.UserStats, File.UserStats>($"/api/v3/File/{fileId}/UserStats", HttpMethod.Put, userStats, apiKey);

    public async Task<bool> ScrobbleFile(string fileId, string episodeId, string eventName, bool watched, string apiKey)
        => await Get($"/api/v3/File/{fileId}/Scrobble?event={eventName}&episodeID={episodeId}&watched={watched}", HttpMethod.Patch, apiKey).ConfigureAwait(false) is { } response &&
            response.StatusCode is HttpStatusCode.OK or HttpStatusCode.NoContent;

    public async Task<bool> ScrobbleFile(string fileId, string episodeId, string eventName, long progress, string apiKey)
        => await Get($"/api/v3/File/{fileId}/Scrobble?event={eventName}&episodeID={episodeId}&resumePosition={Math.Round(new TimeSpan(progress).TotalMilliseconds)}", HttpMethod.Patch, apiKey).ConfigureAwait(false) is { } response &&
            response.StatusCode is HttpStatusCode.OK or HttpStatusCode.NoContent;

    public async Task<bool> ScrobbleFile(string fileId, string episodeId, string eventName, long? progress, bool watched, string apiKey)
        => !progress.HasValue
            ? await ScrobbleFile(fileId, episodeId, eventName, watched, apiKey).ConfigureAwait(false)
            : await Get($"/api/v3/File/{fileId}/Scrobble?event={eventName}&episodeID={episodeId}&resumePosition={Math.Round(new TimeSpan(progress.Value).TotalMilliseconds)}&watched={watched}", HttpMethod.Patch, apiKey).ConfigureAwait(false) is { } response && 
                response.StatusCode is HttpStatusCode.OK or HttpStatusCode.NoContent;

    #endregion

    #endregion

    #region Shoko Episode

    public Task<ShokoEpisode?> GetShokoEpisode(string episodeId)
        => GetOrNull<ShokoEpisode>($"/api/v3/Episode/{episodeId}?includeDataFrom=AniDB&includeXRefs=true");

    public async Task<IReadOnlyList<ShokoEpisode>> GetShokoEpisodesInShokoSeries(string seriesId)
        => (await GetOrNull<ListResult<ShokoEpisode>>($"/api/v3/Series/{seriesId}/Episode?pageSize=0&includeHidden=true&includeMissing=true&includeUnaired=true&includeDataFrom=AniDB&includeXRefs=true").ConfigureAwait(false))?.List ?? [];

    public async Task<EpisodeImages?> GetImagesForShokoEpisode(string episodeId, CancellationToken cancellationToken = default) {
        var episodeImages = await GetOrNull<EpisodeImages>($"/api/v3/Episode/{episodeId}/Images", cancellationToken: cancellationToken).ConfigureAwait(false);
        if (episodeImages is null)
            return null;

        // If the episode has no 'movie' images, get the series images to compensate.
        if (episodeImages.Posters.Count is 0) {
            var episode1 = await GetShokoEpisode(episodeId).ConfigureAwait(false);
            var seriesImages1 = await GetImagesForShokoSeries(episode1!.IDs.ParentSeries.ToString(), cancellationToken: cancellationToken).ConfigureAwait(false) ?? new();

            episodeImages.Posters = seriesImages1.Posters;
            episodeImages.Logos = seriesImages1.Logos;
            episodeImages.Banners = seriesImages1.Banners;
            episodeImages.Backdrops = seriesImages1.Backdrops;
        }

        return episodeImages;
    }

    #endregion

    #region Shoko Series

    public Task<ShokoSeries?> GetShokoSeries(string seriesId)
        => GetOrNull<ShokoSeries>($"/api/v3/Series/{seriesId}?includeDataFrom=AniDB");

    public Task<ShokoSeries?> GetShokoSeriesForAnidbAnime(string animeId)
        => GetOrNull<ShokoSeries>($"/api/v3/Series/AniDB/{animeId}/Series?includeDataFrom=AniDB");

    public Task<ShokoSeries?> GetShokoSeriesForShokoEpisode(string episodeId)
        => GetOrNull<ShokoSeries>($"/api/v3/Episode/{episodeId}/Series?includeDataFrom=AniDB");

    public async Task<IReadOnlyList<ShokoSeries>> GetShokoSeriesForDirectory(string directoryName)
        => await GetOrNull<IReadOnlyList<ShokoSeries>>($"/api/v3/Series/PathEndsWith/{Uri.EscapeDataString(directoryName)}").ConfigureAwait(false) ?? [];

    public async Task<IReadOnlyList<ShokoSeries>> GetShokoSeriesForTmdbMovie(string movieId)
        => await GetOrNull<IReadOnlyList<ShokoSeries>>($"/api/v3/TMDB/Movie/{movieId}/Shoko/Series?includeDataFrom=AniDB").ConfigureAwait(false) ?? [];

    public async Task<IReadOnlyList<ShokoSeries>> GetShokoSeriesForTmdbShow(string showId)
        => await GetOrNull<IReadOnlyList<ShokoSeries>>($"/api/v3/TMDB/Show/{showId}/Shoko/Series?includeDataFrom=AniDB").ConfigureAwait(false) ?? [];

    public async Task<IReadOnlyList<ShokoSeries>> GetShokoSeriesInGroup(string groupId, int filterId = 0, bool recursive = false)
        => await GetOrNull<IReadOnlyList<ShokoSeries>>($"/api/v3/Filter/{filterId}/Group/{groupId}/Series?recursive={recursive}&includeMissing=true&includeIgnored=false&includeDataFrom=AniDB").ConfigureAwait(false) ?? [];

    public async Task<IReadOnlyList<Role>> GetCastForShokoSeries(string seriesId)
        => await GetOrNull<IReadOnlyList<Role>>($"/api/v3/Series/{seriesId}/Cast?includeDataFrom=AniDB").ConfigureAwait(false) ?? [];

    public async Task<IReadOnlyList<Relation>> GetRelationsForShokoSeries(string seriesId)
        => await GetOrNull<IReadOnlyList<Relation>>($"/api/v3/Series/{seriesId}/Relations").ConfigureAwait(false) ?? [];

    public async Task<IReadOnlyList<Tag>> GetTagsForShokoSeries(string seriesId)
        => await GetOrNull<IReadOnlyList<Tag>>($"/api/v3/Series/{seriesId}/Tags?filter=0&excludeDescriptions=true").ConfigureAwait(false) ?? [];

    public Task<Images?> GetImagesForShokoSeries(string seriesId, CancellationToken cancellationToken = default)
        => GetOrNull<Images>($"/api/v3/Series/{seriesId}/Images", cancellationToken: cancellationToken);

    public async Task<IReadOnlyList<File>> GetFilesForShokoSeries(string seriesId)
        => (await GetOrNull<ListResult<File>>($"/api/v3/Series/{seriesId}/File?pageSize=0&include=XRefs&includeDataFrom=AniDB").ConfigureAwait(false))?.List ?? [];

    public async Task<IReadOnlyList<TmdbEpisodeCrossReference>> GetTmdbCrossReferencesForShokoSeries(string seriesId)
        => (await GetOrNull<ListResult<TmdbEpisodeCrossReference>>($"/api/v3/Series/{seriesId}/TMDB/Show/CrossReferences/Episode?pageSize=0").ConfigureAwait(false))?.List ?? [];

    #endregion

    #region Shoko Group

    public Task<ShokoGroup?> GetShokoGroup(string groupId)
        => GetOrNull<ShokoGroup>($"/api/v3/Group/{groupId}");

    public Task<ShokoGroup?> GetShokoGroupForShokoSeries(string seriesId)
        => GetOrNull<ShokoGroup>($"/api/v3/Series/{seriesId}/Group");

    public async Task<IReadOnlyList<ShokoGroup>> GetShokoGroupsInShokoGroup(string groupId)
        => await GetOrNull<IReadOnlyList<ShokoGroup>>($"/api/v3/Group/{groupId}/Group?includeEmpty=true").ConfigureAwait(false) ?? [];

    #endregion

    #region TMDB Episode

    public Task<TmdbEpisode?> GetTmdbEpisode(string episodeId)
        => GetOrNull<TmdbEpisode>($"/api/v3/TMDB/Episode/{episodeId}?include=Titles,Overviews,Cast,Crew,FileCrossReferences");

    public async Task<IReadOnlyList<TmdbEpisode>> GetTmdbEpisodesInTmdbSeason(string seasonId)
        => (await GetOrNull<ListResult<TmdbEpisode>>($"/api/v3/TMDB/Season/{seasonId}/Episode?pageSize=0&include=Titles,Overviews,Cast,Crew,FileCrossReferences").ConfigureAwait(false))?.List ?? [];

    public async Task<IReadOnlyList<TmdbEpisode>> GetTmdbEpisodesInTmdbShow(string showId)
        => (await GetOrNull<ListResult<TmdbEpisode>>($"/api/v3/TMDB/Show/{showId}/Episode?pageSize=0&include=Titles,Overviews,Cast,Crew,FileCrossReferences").ConfigureAwait(false))?.List ?? [];

    public Task<EpisodeImages?> GetImagesForTmdbEpisode(string episodeId, CancellationToken cancellationToken = default)
        => GetOrNull<EpisodeImages>($"/api/v3/TMDB/Episode/{episodeId}/Images", cancellationToken: cancellationToken);

    public async Task<IReadOnlyList<File>> GetFilesForTmdbEpisode(string episodeId)
        => (await GetOrNull<ListResult<File>>($"/api/v3/TMDB/Episode/{episodeId}/Shoko/File?pageSize=0&include=XRefs&includeDataFrom=AniDB").ConfigureAwait(false))?.List ?? [];

    #endregion

    #region TMDB Season

    public Task<TmdbSeason?> GetTmdbSeasonForTmdbEpisode(string episodeId)
        => GetOrNull<TmdbSeason>($"/api/v3/TMDB/Episode/{episodeId}/Season?include=Titles,Overviews");

    public Task<TmdbSeason?> GetTmdbSeason(string seasonId)
        => GetOrNull<TmdbSeason>($"/api/v3/TMDB/Season/{seasonId}?include=Titles,Overviews");

    public async Task<IReadOnlyList<TmdbSeason>> GetTmdbSeasonsInTmdbShow(string showId)
        => (await GetOrNull<ListResult<TmdbSeason>>($"/api/v3/TMDB/Show/{showId}/Season?pageSize=0&include=Titles,Overviews").ConfigureAwait(false))?.List ?? [];

    public Task<Images?> GetImagesForTmdbSeason(string seasonId, CancellationToken cancellationToken = default)
        => GetOrNull<Images>($"/api/v3/TMDB/Season/{seasonId}/Images", cancellationToken: cancellationToken);

    public async Task<IReadOnlyList<File>> GetFilesForTmdbSeason(string seasonId)
        => (await GetOrNull<ListResult<File>>($"/api/v3/TMDB/Season/{seasonId}/Shoko/File?pageSize=0&include=XRefs&includeDataFrom=AniDB").ConfigureAwait(false))?.List ?? [];

    #endregion

    #region TMDB Show

    public Task<TmdbShow?> GetTmdbShowForSeason(string seasonId)
        => GetOrNull<TmdbShow>($"/api/v3/TMDB/Season/{seasonId}/Show?include=Titles,Overviews,Keywords,Studios,ContentRatings,ProductionCountries");

    public Task<Images?> GetImagesForTmdbShow(string showId, CancellationToken cancellationToken = default)
        => GetOrNull<Images>($"/api/v3/TMDB/Show/{showId}/Images", cancellationToken: cancellationToken);

    public async Task<IReadOnlyList<File>> GetFilesForTmdbShow(string showId)
        => (await GetOrNull<ListResult<File>>($"/api/v3/TMDB/Show/{showId}/Shoko/File?pageSize=0&include=XRefs&includeDataFrom=AniDB").ConfigureAwait(false))?.List ?? [];

    public async Task<IReadOnlyList<TmdbEpisodeCrossReference>> GetTmdbCrossReferencesForTmdbShow(string showId)
        => (await GetOrNull<ListResult<TmdbEpisodeCrossReference>>($"/api/v3/TMDB/Show/{showId}/Episode/CrossReferences?pageSize=0").ConfigureAwait(false))?.List ?? [];

    #endregion

    #region TMDB Movie

    public Task<TmdbMovie?> GetTmdbMovie(string movieId)
        => GetOrNull<TmdbMovie>($"/api/v3/TMDB/Movie/{movieId}?include=Titles,Overviews,Keywords,Studios,ContentRatings,ProductionCountries,Cast,Crew,FileCrossReferences");

    public async Task<IReadOnlyList<TmdbMovie>> GetTmdbMoviesInMovieCollection(string collectionId)
        => await GetOrNull<IReadOnlyList<TmdbMovie>>($"/api/v3/TMDB/Movie/Collection/{collectionId}/Movie?include=Titles,Overviews,Keywords,Studios,ContentRatings,ProductionCountries,Cast,Crew,FileCrossReferences").ConfigureAwait(false) ?? [];

    public Task<EpisodeImages?> GetImagesForTmdbMovie(string movieId, CancellationToken cancellationToken = default)
        => GetOrNull<EpisodeImages>($"/api/v3/TMDB/Movie/{movieId}/Images", cancellationToken: cancellationToken);

    public async Task<IReadOnlyList<File>> GetFilesForTmdbMovie(string movieId)
        => (await GetOrNull<ListResult<File>>($"/api/v3/TMDB/Movie/{movieId}/Shoko/File?pageSize=0&include=XRefs&includeDataFrom=AniDB").ConfigureAwait(false))?.List ?? [];

    public async Task<IReadOnlyList<TmdbMovieCrossReference>> GetTmdbCrossReferencesForTmdbMovie(string showId)
        => await GetOrNull<IReadOnlyList<TmdbMovieCrossReference>>($"/api/v3/TMDB/Movie/{showId}/CrossReferences").ConfigureAwait(false) ?? [];

    #endregion

    #region TMDB Movie Collection

    public Task<TmdbMovieCollection?> GetTmdbMovieCollection(string collectionId)
        => GetOrNull<TmdbMovieCollection>($"/api/v3/TMDB/Movie/Collection/{collectionId}");

    public Task<Images?> GetImagesForTmdbMovieCollection(string collectionId, CancellationToken cancellationToken = default)
        => GetOrNull<Images>($"/api/v3/TMDB/Movie/Collection/{collectionId}/Images", cancellationToken: cancellationToken);

    #endregion

    #region Custom Tags

    public Task<List<Tag>> GetCustomTags()
        => Get<List<Tag>>($"/api/v3/Tag/User");

    public Task<Tag> CreateCustomTag(string name, string? description = null)
        => Post<Dictionary<string, string?>, Tag>($"/api/v3/Tag/User", HttpMethod.Post, new() { { "name", name }, { "description", description } });

    #endregion
}
