using System.IO;

namespace YouTubeViewer;

public static class ViewerProfile
{
    public static string UserDataFolder(string? localApplicationData = null)
    {
        var baseDirectory = string.IsNullOrWhiteSpace(localApplicationData)
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : localApplicationData;

        // WebView2 creates its own EBWebView directory below this stable root.
        return Path.Combine(baseDirectory, "VideoMem", "YouTubeViewer");
    }
}
