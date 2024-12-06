using System.IO;
using MediaBrowser.Controller.Entities;

namespace Shokofin.Extensions;

public static class FolderExtensions {
    public static string GetVirtualRoot(this Folder libraryFolder)
        => Path.Join(Plugin.Instance.VirtualRoot, libraryFolder.Id.ToString());
}