
using System;
using System.IO;
using MediaBrowser.Controller.Entities;
using Shokofin.Configuration;

namespace Shokofin.Resolvers.Models;

public class ShokoWatcher(Folder mediaFolder, MediaFolderConfiguration configuration, FileSystemWatcher watcher, IDisposable lease) {
    public Folder MediaFolder = mediaFolder;

    public MediaFolderConfiguration Configuration = configuration;

    public FileSystemWatcher Watcher = watcher;

    public IDisposable SubmitterLease = lease;
}
