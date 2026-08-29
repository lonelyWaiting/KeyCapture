using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace KeyCapture.Services;

public sealed class AppSettings
{
    public bool SpecialKeysOnly { get; set; } = true;

    /// <summary>
    /// Double clicking empty space in a Windows Explorer folder navigates to the parent folder.
    /// </summary>
    public bool FolderUpOnDoubleClick { get; set; } = true;

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "KeyCapture",
        "settings.json");

    public static AppSettings Load()
    {
        try
        {
            var path = SettingsPath;
            if (File.Exists(path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AppSettings] Load failed: {ex.Message}");
        }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            var path = SettingsPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AppSettings] Save failed: {ex.Message}");
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
}
