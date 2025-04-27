using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Shokofin;

/// <inheritdoc />
public class PluginServiceRegistrator : IPluginServiceRegistrator {
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost) {
        serviceCollection.AddSingleton<Utils.UsageTracker>();
        serviceCollection.AddSingleton<Utils.LibraryScanWatcher>();
        serviceCollection.AddSingleton<API.ShokoApiClient>();
        serviceCollection.AddSingleton<API.ShokoApiManager>();
        serviceCollection.AddSingleton<Configuration.MediaFolderConfigurationService>();
        serviceCollection.AddSingleton<Configuration.SeriesConfigurationService>();
        serviceCollection.AddSingleton<IIdLookup, IdLookup>();
        serviceCollection.AddSingleton<Sync.UserDataSyncManager>();
        serviceCollection.AddSingleton<MergeVersions.MergeVersionsManager>();
        serviceCollection.AddSingleton<Collections.CollectionManager>();
        serviceCollection.AddSingleton<Resolvers.VirtualFileSystemService>();
        serviceCollection.AddSingleton<Events.EventDispatchService>();
        serviceCollection.AddSingleton<SignalR.SignalRConnectionManager>();
        serviceCollection.AddHostedService<SignalR.SignalREntryPoint>();
        serviceCollection.AddHostedService<Resolvers.ShokoLibraryMonitor>();
        serviceCollection.AddControllers(options => options.Filters.Add<Web.ImageHostUrl>());
    }
}
