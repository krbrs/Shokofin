using System;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using Shokofin.Collections;
using Shokofin.MergeVersions;

namespace Shokofin.Tasks;

public class PostScanTask(MergeVersionsManager _mergeVersionsManager, CollectionManager _collectionManager) : ILibraryPostScanTask {
    /// <inheritdoc />
    public async Task Run(IProgress<double> progress, CancellationToken token) {
        // Merge versions now if the setting is enabled.
        if (Plugin.Instance.Configuration.AutoMergeVersions) {
            // Setup basic progress tracking
            var baseProgress = 0d;
            var simpleProgress = new Progress<double>(value => progress.Report(baseProgress + (value / 2d)));

            // Merge versions.
            await _mergeVersionsManager.SplitAndMergeAll(simpleProgress, token).ConfigureAwait(false);

            // Reconstruct collections.
            baseProgress = 50;
            await _collectionManager.ReconstructCollections(simpleProgress, token).ConfigureAwait(false);

            progress.Report(100d);
        }
        else {
            // Reconstruct collections.
            await _collectionManager.ReconstructCollections(progress, token).ConfigureAwait(false);
        }
    }
}
