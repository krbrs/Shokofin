using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net.Mime;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Logging;
using Shokofin.API;
using Shokofin.Configuration;
using Shokofin.Resolvers;
using Shokofin.Web.Models;

namespace Shokofin.Web;

/// <summary>
/// Shoko Utility Web Controller.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="UtilityApiController"/> class.
/// </remarks>
[ApiController]
[Route("Plugin/Shokofin/Utility")]
[Produces(MediaTypeNames.Application.Json)]
public class UtilityApiController(
    ILogger<UtilityApiController> logger,
    ShokoApiClient apiClient,
    SeriesConfigurationService seriesConfigurationService,
    VirtualFileSystemService virtualFileSystemService
) : ControllerBase {
    private readonly ILogger<UtilityApiController> Logger = logger;

    private readonly SeriesConfigurationService SeriesConfigurationService = seriesConfigurationService;

    private readonly VirtualFileSystemService VirtualFileSystemService = virtualFileSystemService;

    /// <summary>
    /// Previews the VFS structure for the given library.
    /// </summary>
    /// <param name="libraryId">The id of the library to preview.</param>
    /// <returns>A <see cref="VfsLibraryPreview"/> or <see cref="ValidationProblemDetails"/> if the library is not found.</returns>
    [HttpPost("VFS/Library/{libraryId}/Preview")]
    public async Task<ActionResult<VfsLibraryPreview>> PreviewVFS(Guid libraryId) {
        var trackerId = Plugin.Instance.Tracker.Add("Preview VFS");
        try {
            var (filesBefore, filesAfter, virtualFolder, result, vfsPath) = await VirtualFileSystemService.PreviewChangesForLibrary(libraryId).ConfigureAwait(false);
            if (virtualFolder is null)
                return NotFound("Unable to find library with the given id.");

            return new VfsLibraryPreview(filesBefore, filesAfter, virtualFolder, result, vfsPath);
        }
        finally {
            Plugin.Instance.Tracker.Remove(trackerId);
        }
    }

    /// <summary>
    /// Retrieves a simple series list.
    /// </summary>
    /// <returns>The series list.</returns>
    [HttpGet("Series")]
    public async Task<ActionResult<List<SimpleSeries>>> GetSeriesList() {
        var trackerId = Plugin.Instance.Tracker.Add($"Get Series List");
        try {
            const int PageSize = 100;
            var simpleList = new List<SimpleSeries>();
            var firstPage = await apiClient.GetAllAnidbAnime(pageSize: PageSize);
            foreach (var anime in firstPage.List) {
                if (anime.ShokoId.HasValue)
                    simpleList.Add(new SimpleSeries() {
                        Id = anime.ShokoId.Value,
                        AnidbId = anime.Id,
                        Title = anime.Title,
                        DefaultTitle = anime.Titles?.FirstOrDefault(title => title.Type is API.Models.TitleType.Main)?.Value ?? anime.Title,
                    });
            }
            if (firstPage.Total > PageSize) {
                var total = firstPage.Total;
                var page = 2;
                while (total > 0) {
                    var nextPage = await apiClient.GetAllAnidbAnime(page, PageSize);
                    foreach (var anime in nextPage.List) {
                        if (anime.ShokoId.HasValue)
                            simpleList.Add(new SimpleSeries() {
                                Id = anime.ShokoId.Value,
                                AnidbId = anime.Id,
                                Title = anime.Title,
                                DefaultTitle = anime.Titles?.FirstOrDefault(title => title.Type is API.Models.TitleType.Main)?.Value ?? anime.Title,
                            });
                    }
                    total -= PageSize;
                    page++;
                }
            }
            return simpleList;
        }
        finally {
            Plugin.Instance.Tracker.Remove(trackerId);
        }
    }

    /// <summary>
    /// Retrieves the series configuration for the given series id.
    /// </summary>
    /// <param name="seriesId">Shoko series ID.</param>
    /// <returns>The series configuration, if found.</returns>
    [HttpGet("Series/{seriesId}/Configuration")]
    public async Task<ActionResult<SeriesConfiguration>> GetSeriesConfigurationForId(
        [FromRoute, Range(1, int.MaxValue)] int seriesId
    ) {
        var trackerId = Plugin.Instance.Tracker.Add($"Get Series Configuration for {seriesId}");
        try {
            var config = await SeriesConfigurationService.GetSeriesConfigurationForId(seriesId).ConfigureAwait(false);
            if (config is null)
                return NotFound("Unable to find series with the given id.");

            return config;
        }
        finally {
            Plugin.Instance.Tracker.Remove(trackerId);
        }
    }

    /// <summary>
    /// Updates the series configuration for the given series id.
    /// </summary>
    /// <param name="seriesId">Shoko series ID.</param>
    /// <param name="seriesConfiguration">The series configuration.</param>
    /// <returns>The updated series configuration.</returns>
    [HttpPost("Series/{seriesId}/Configuration")]
    public async Task<ActionResult<SeriesConfiguration>> UpdateSeriesConfigurationForId(
        [FromRoute, Range(1, int.MaxValue)] int seriesId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] NullableSeriesConfiguration seriesConfiguration
    ) {
        var trackerId = Plugin.Instance.Tracker.Add($"Update Series Configuration for {seriesId} (Add)");
        try {
            return await SeriesConfigurationService.UpdateSeriesConfigurationForId(seriesId, seriesConfiguration).ConfigureAwait(false);
        }
        finally {
            Plugin.Instance.Tracker.Remove(trackerId);
        }
    }

    /// <summary>
    /// Updates the series configuration for the given series id.
    /// </summary>
    /// <param name="seriesId">Shoko series ID.</param>
    /// <param name="seriesConfiguration">The series configuration.</param>
    /// <returns>The updated series configuration.</returns>
    [HttpPut("Series/{seriesId}/Configuration")]
    public async Task<ActionResult<SeriesConfiguration>> UpdateSeriesConfigurationForId(
        [FromRoute, Range(1, int.MaxValue)] int seriesId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] SeriesConfiguration seriesConfiguration
    ) {
        var trackerId = Plugin.Instance.Tracker.Add($"Update Series Configuration for {seriesId} (Replace)");
        try {
            return await SeriesConfigurationService.UpdateSeriesConfigurationForId(seriesId, seriesConfiguration).ConfigureAwait(false);
        }
        finally {
            Plugin.Instance.Tracker.Remove(trackerId);
        }
    }
}
