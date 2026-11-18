using System.IO;
using System.Reflection;

namespace VhdIsoStudio.Core;

/// <summary>Informazioni centrali sull'applicazione (nome, versione, percorsi).</summary>
public static class AppInfo
{
    public const string Name = "VHD ISO Studio";
    public const string Id = "VhdIsoStudio";
    public const string AppUserModelId = "VhdIsoStudio.App.1";
    public const string BuildDate = "2026-11-18";
    public const string RepoUrl = "https://github.com/";

    public static string Version
    {
        get
        {
            var v = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
            var plus = v.IndexOf('+');
            return plus > 0 ? v[..plus] : v;
        }
    }

    /// <summary>Cartella dell'eseguibile (per un exe single-file e' la cartella dell'exe, non quella temporanea).</summary>
    public static string AppDir =>
        Path.GetDirectoryName(Environment.ProcessPath ?? "") is { Length: > 0 } d ? d : AppContext.BaseDirectory;

    private static readonly Lazy<string?> _portableRoot = new(() =>
    {
        // Modalita' portabile (predefinita): tutto in <cartella exe>\Data se scrivibile.
        // Un file "no-portable.txt" accanto all'exe forza la modalita' installata (%ProgramData%/%LOCALAPPDATA%).
        try
        {
            if (File.Exists(Path.Combine(AppDir, "no-portable.txt"))) return null;
            string data = Path.Combine(AppDir, "Data");
            Directory.CreateDirectory(data);
            string probe = Path.Combine(data, $".probe_{Environment.ProcessId}");
            File.WriteAllText(probe, "x");
            File.Delete(probe);
            return data;
        }
        catch { return null; } // es. exe in Program Files o su supporto in sola lettura
    });

    /// <summary>true se dati, log e strumenti stanno accanto all'exe (nessuna scrittura nel profilo/sistema).</summary>
    public static bool IsPortable => _portableRoot.Value != null;

    /// <summary>Cartella dati macchina (log, download, lavoro): Data\ portabile oppure %ProgramData%\VhdIsoStudio.</summary>
    public static string MachineDataDir => _portableRoot.Value ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), Id);

    /// <summary>Cartella impostazioni: Data\ portabile oppure %LOCALAPPDATA%\VhdIsoStudio.</summary>
    public static string UserDataDir => _portableRoot.Value ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Id);

    public static string LogDir => Path.Combine(MachineDataDir, "Logs");
    public static string DownloadDir => Path.Combine(MachineDataDir, "Downloads");
    public static string WorkDir => Path.Combine(MachineDataDir, "Work");

    /// <summary>Strumenti portabili (oscdimg.exe + file di boot) accanto all'exe.</summary>
    public static string ToolsDir => Path.Combine(AppDir, "tools", "oscdimg");

    public static string ExePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, Id + ".exe");
}
