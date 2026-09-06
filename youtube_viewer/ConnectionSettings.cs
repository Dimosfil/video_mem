using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace YouTubeViewer;

public enum RoutingMode
{
    System,
    LocalProxy,
}

public sealed record ConnectionSettings(RoutingMode Mode, string ProxyUrl)
{
    public static ConnectionSettings Default { get; } =
        new(RoutingMode.System, ProxyConfiguration.DefaultAddress);

    public ProxyConfiguration? GetProxy() =>
        Mode == RoutingMode.LocalProxy ? ProxyConfiguration.Parse(ProxyUrl) : null;
}

public sealed class ConnectionSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;

    public ConnectionSettingsStore(string? path = null)
    {
        _path = path ?? Path.Combine(ViewerProfile.UserDataFolder(), "settings.json");
    }

    public ConnectionSettings Load()
    {
        if (!File.Exists(_path))
        {
            return ConnectionSettings.Default;
        }

        try
        {
            var settings = JsonSerializer.Deserialize<ConnectionSettings>(File.ReadAllText(_path), JsonOptions);
            if (settings is null)
            {
                return ConnectionSettings.Default;
            }

            _ = settings.GetProxy();
            return settings;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return ConnectionSettings.Default;
        }
    }

    public void Save(ConnectionSettings settings)
    {
        _ = settings.GetProxy();
        var directory = Path.GetDirectoryName(_path)
            ?? throw new InvalidOperationException("Не удалось определить каталог настроек.");
        Directory.CreateDirectory(directory);

        var temporaryPath = _path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temporaryPath, _path, overwrite: true);
    }
}
