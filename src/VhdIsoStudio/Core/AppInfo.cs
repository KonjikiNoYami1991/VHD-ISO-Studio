using System.IO;
using System.Reflection;

namespace VhdIsoStudio.Core;

/// <summary>Informazioni centrali sull'applicazione (nome, versione, percorsi).</summary>
public static class AppInfo
{
    public const string Name = "VHD ISO Studio";
    public const string Id = "VhdIsoStudio";
    public const string AppUserModelId = "VhdIsoStudio.App.1";
    public const string BuildDate = "2026-11-17";
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

    /// <summary>Cartella dati macchina (log): %ProgramData%\VhdIsoStudio.</summary>
    public static string MachineDataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), Id);

    /// <summary>Cartella dati utente (impostazioni): %LOCALAPPDATA%\VhdIsoStudio.</summary>
    public static string UserDataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Id);

    public static string LogDir => Path.Combine(MachineDataDir, "Logs");
    public static string DownloadDir => Path.Combine(MachineDataDir, "Downloads");
    public static string WorkDir => Path.Combine(MachineDataDir, "Work");

    public static string ExePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, Id + ".exe");
}
