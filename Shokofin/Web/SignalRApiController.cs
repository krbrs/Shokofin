using System;
using System.Net.Mime;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Shokofin.SignalR;

namespace Shokofin.Web;

/// <summary>
/// Shoko SignalR Control Web Controller.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="SignalRApiController"/> class.
/// </remarks>
[ApiController]
[Route("Plugin/Shokofin/SignalR")]
[Produces(MediaTypeNames.Application.Json)]
public class SignalRApiController(ILogger<SignalRApiController> logger, SignalRConnectionManager connectionManager) : ControllerBase {
    private readonly ILogger<SignalRApiController> Logger = logger;

    private readonly SignalRConnectionManager ConnectionManager = connectionManager;

    /// <summary>
    /// Get the current status of the connection to Shoko Server.
    /// </summary>
    [HttpGet("Status")]
    public ShokoSignalRStatus GetStatus() {
        return new() {
            IsUsable = ConnectionManager.IsUsable,
            IsActive = ConnectionManager.IsActive,
            State = ConnectionManager.State,
        };
    }

    /// <summary>
    /// Connect or reconnect to Shoko Server.
    /// </summary>
    [HttpPost("Connect")]
    public async Task<ActionResult> ConnectAsync() {
        try {
            await ConnectionManager.ResetConnectionAsync().ConfigureAwait(false);
            return Ok();
        }
        catch (Exception ex) {
            Logger.LogError(ex, "Failed to connect to server.");
            return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// Disconnect from Shoko Server.
    /// </summary>
    [HttpPost("Disconnect")]
    public async Task<ActionResult> DisconnectAsync() {
        try {
            await ConnectionManager.DisconnectAsync().ConfigureAwait(false);
            return Ok();
        }
        catch (Exception ex) {
            Logger.LogError(ex, "Failed to disconnect from server.");
            return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
}

public class ShokoSignalRStatus {
    /// <summary>
    /// Determines if we can establish a connection to the server.
    /// </summary>
    public bool IsUsable { get; set; }

    /// <summary>
    /// Determines if the connection manager is currently active.
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// The current state of the connection.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public HubConnectionState State { get; set; }
}