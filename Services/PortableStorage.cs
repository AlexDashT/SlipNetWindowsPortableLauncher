using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using SlipNetPortableLauncher.Models;

namespace SlipNetPortableLauncher.Services;

internal sealed class PortableStorage
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string dataDirectory;
    private readonly string profilesPath;
    private readonly string settingsPath;

    public PortableStorage()
    {
        dataDirectory = Path.Combine(AppContext.BaseDirectory, "data");
        profilesPath = Path.Combine(dataDirectory, "profiles.json");
        settingsPath = Path.Combine(dataDirectory, "settings.json");
    }

    public IReadOnlyList<SlipNetProfile> LoadProfiles()
    {
        Directory.CreateDirectory(dataDirectory);
        if (!File.Exists(profilesPath))
        {
            return [];
        }

        var json = File.ReadAllText(profilesPath);
        return JsonSerializer.Deserialize<List<SlipNetProfile>>(json, JsonOptions) ?? [];
    }

    public void SaveProfiles(IEnumerable<SlipNetProfile> profiles)
    {
        Directory.CreateDirectory(dataDirectory);
        var json = JsonSerializer.Serialize(profiles, JsonOptions);
        File.WriteAllText(profilesPath, json);
    }

    public AppSettings LoadSettings()
    {
        Directory.CreateDirectory(dataDirectory);
        if (!File.Exists(settingsPath))
        {
            return new AppSettings();
        }

        var json = File.ReadAllText(settingsPath);
        return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
    }

    public void SaveSettings(AppSettings settings)
    {
        Directory.CreateDirectory(dataDirectory);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(settingsPath, json);
    }
}
