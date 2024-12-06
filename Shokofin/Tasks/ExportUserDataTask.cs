using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Tasks;
using Shokofin.Sync;

namespace Shokofin.Tasks;

public class ExportUserDataTask(UserDataSyncManager _userSyncManager) : IScheduledTask, IConfigurableScheduledTask {
    /// <inheritdoc />
    public string Name => "Export User Data";

    /// <inheritdoc />
    public string Description => "Export the user-data stored in Jellyfin to Shoko. Will not import user-data from Shoko.";

    /// <inheritdoc />
    public string Category => "Shokofin";

    /// <inheritdoc />
    public string Key => "ShokoExportUserData";

    /// <inheritdoc />
    public bool IsHidden => false;

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <inheritdoc />
    public bool IsLogged => true;

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        => [];

    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        => _userSyncManager.ScanAndSync(SyncDirection.Export, progress, cancellationToken);
}
