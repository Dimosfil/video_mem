namespace YouTubeViewer;

public static class BrowserAddress
{
    public const string Home = "https://www.youtube.com/";

    public static string Resolve(string input)
    {
        var value = input.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return Home;
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out var absolute) &&
            absolute.Scheme is "http" or "https")
        {
            return absolute.AbsoluteUri;
        }

        if (!value.Contains(' ') && value.Contains('.') &&
            Uri.TryCreate($"https://{value}", UriKind.Absolute, out var hostAddress))
        {
            return hostAddress.AbsoluteUri;
        }

        return $"https://www.youtube.com/results?search_query={Uri.EscapeDataString(value)}";
    }
}
