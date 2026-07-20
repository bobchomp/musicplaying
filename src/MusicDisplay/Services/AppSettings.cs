using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MusicDisplay.Services;

public sealed class AppSettings
{
    public string? MonitorDeviceName { get; set; }
    public bool DisplayVisible { get; set; }
    public bool StartWithWindows { get; set; }
    public DisplayLayout Layout { get; set; } = DisplayLayout.Centered;
    public bool ShowClock { get; set; }
    public bool ShowLyrics { get; set; }
    public bool NetworkFeedEnabled { get; set; }
    public string NetworkFeedName { get; set; } = "Music Display";
}

public static class SettingsService
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MusicDisplay",
        "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (settings != null)
                {
                    return settings;
                }
            }
        }
        catch (Exception)
        {
            // Corrupt or unreadable settings file: fall back to defaults.
        }

        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(FilePath, json);
        }
        catch (Exception)
        {
            // Best-effort persistence; nothing actionable if this fails.
        }
    }

    public static void OpenSettingsFolder()
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath);
            if (string.IsNullOrEmpty(dir))
            {
                return;
            }

            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
        }
        catch (Exception)
        {
            // Best-effort; nothing actionable if Explorer can't be launched.
        }
    }
}
