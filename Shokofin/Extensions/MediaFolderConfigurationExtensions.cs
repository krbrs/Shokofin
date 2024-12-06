using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Controller.Entities;
using Shokofin.Configuration;

namespace Shokofin.Extensions;

public static class MediaFolderConfigurationExtensions {
    public static Folder GetFolderForPath(this string mediaFolderPath)
        => BaseItem.LibraryManager.FindByPath(mediaFolderPath, true) as Folder ??
            throw new Exception($"Unable to find folder by path \"{mediaFolderPath}\".");

    public static IReadOnlyList<(int importFolderId, string importFolderSubPath, IReadOnlyList<string> mediaFolderPaths)> ToImportFolderList(this IEnumerable<MediaFolderConfiguration> mediaConfigs)
        => mediaConfigs
            .GroupBy(a => (a.ImportFolderId, a.ImportFolderRelativePath))
            .Select(g => (g.Key.ImportFolderId, g.Key.ImportFolderRelativePath, g.Select(a => a.MediaFolderPath).ToList() as IReadOnlyList<string>))
            .ToList();

    public static IReadOnlyList<(string importFolderSubPath, bool vfsEnabled, IReadOnlyList<string> mediaFolderPaths)> ToImportFolderList(this IEnumerable<MediaFolderConfiguration> mediaConfigs, int importFolderId, string relativePath)
        => mediaConfigs
            .Where(a => a.ImportFolderId == importFolderId && a.IsEnabledForPath(relativePath))
            .GroupBy(a => (a.ImportFolderId, a.ImportFolderRelativePath, a.IsVirtualFileSystemEnabled))
            .Select(g => (g.Key.ImportFolderRelativePath, g.Key.IsVirtualFileSystemEnabled, g.Select(a => a.MediaFolderPath).ToList() as IReadOnlyList<string>))
            .ToList();
}
