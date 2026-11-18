using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VhdIsoStudio.Core;

/// <summary>Impostazioni utente persistite in JSON (scrittura atomica).</summary>
public sealed class AppSettings
{
    public string Theme { get; set; } = "System";      // System | Dark | Light | Medium
    public string Language { get; set; } = "";          // "" = automatica, "it", "en"
    public bool ReduceMotion { get; set; }
    public string OscdimgPath { get; set; } = "";        // vuoto = ricerca automatica
    public string AdkDownloadUrl { get; set; } = AdkInstaller.DefaultUrl;
    public string LastVhdFolder { get; set; } = "";
    public string LastOutputFolder { get; set; } = "";
    public string BaseMediaPath { get; set; } = "";
    public bool AutoOpenFolder { get; set; } = true;
    public bool AutoComputeHash { get; set; } = true;
    public bool MountReadOnly { get; set; } = true;
    public double WindowWidth { get; set; } = 1180;
    public double WindowHeight { get; set; } = 820;
    public bool WindowMaximized { get; set; }
    public OscdimgOptions Oscdimg { get; set; } = OscdimgOptions.WindowsBootPreset();

    [JsonIgnore]
    public static string FilePath => Path.Combine(AppInfo.UserDataDir, "settings.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOpts) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            Logger.Warn("Impostazioni illeggibili, uso i valori predefiniti: " + ex.Message);
            try { File.Copy(FilePath, FilePath + ".bad", true); } catch { }
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppInfo.UserDataDir);
            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonOpts));
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            Logger.Warn("Impossibile salvare le impostazioni: " + ex.Message);
        }
    }
}
