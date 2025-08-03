using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Tasks;
using Shokofin.Collections;
using Shokofin.Utils;

namespace Shokofin.Tasks;

/// <summary>
/// Reconstruct all Shoko collections outside a Library Scan. For debugging and troubleshooting. DO NOT RUN THIS TASK WHILE A LIBRARY SCAN IS RUNNING.
/// </summary>
public class ReconstructCollectionsTask(CollectionManager _collectionManager, LibraryScanWatcher _libraryScanWatcher) : IScheduledTask, IConfigurableScheduledTask {
    /// <inheritdoc />
    public string Name => "Reconstruct Collections";

    /// <inheritdoc />
    public string Description => "Reconstruct all Shoko collections outside a Library Scan. For debugging and troubleshooting. DO NOT RUN THIS TASK WHILE A LIBRARY SCAN IS RUNNING.";

    /// <inheritdoc />
    public string Category => "Shokofin";

    /// <inheritdoc />
    public string Key => "ShokoReconstructCollections";

    /// <inheritdoc />
    public bool IsHidden => !Plugin.Instance.Configuration.AdvancedMode;

    /// <inheritdoc />
    public bool IsEnabled => Plugin.Instance.Configuration.AdvancedMode;

    /// <inheritdoc />
    public bool IsLogged => true;

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        => [];

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken) {
        if (_libraryScanWatcher.IsScanRunning)
            return;

        using (Plugin.Instance.Tracker.Enter("Reconstruct Collections Task")) {
            await _collectionManager.ReconstructCollections(progress, cancellationToken).ConfigureAwait(false);
        }
    }
}
