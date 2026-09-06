using System.IO;
using System.Text.Json;

namespace YouTubeViewer;

public sealed record BrowserSession(string[] Addresses, int SelectedIndex)
{
    public static BrowserSession Empty { get; } = new(Array.Empty<string>(), 0);

    public BrowserSession Normalize()
    {
        var addresses = new List<string>();
        var selected = 0;
        for (var index = 0; index < (Addresses?.Length ?? 0); index++)
        {
            if (!IsWebAddress(Addresses![index])) continue;
            if (index <= SelectedIndex) selected = addresses.Count;
            addresses.Add(Addresses[index]);
        }
        return new BrowserSession(addresses.ToArray(), selected);
    }

    public static bool IsWebAddress(string? address) =>
        Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";
}

public sealed class BrowserSessionStore
{
    private readonly string _path;

    public BrowserSessionStore(string? path = null) =>
        _path = path ?? Path.Combine(ViewerProfile.UserDataFolder(), "session.json");

    public BrowserSession Load() => Read(_path) ?? Read(_path + ".bak") ?? BrowserSession.Empty;

    private static BrowserSession? Read(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<BrowserSession>(File.ReadAllText(path))?.Normalize();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public void Save(BrowserSession session)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(_path))!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $"session-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(session.Normalize()));
            if (File.Exists(_path))
            {
                // Keep a readable backup if the current file was damaged.
                File.Replace(temporary, _path, Read(_path) is null ? null : _path + ".bak");
            }
            else
            {
                File.Move(temporary, _path);
            }
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
