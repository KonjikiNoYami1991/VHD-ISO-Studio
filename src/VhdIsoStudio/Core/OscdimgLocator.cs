using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace VhdIsoStudio.Core;

/// <summary>Trova oscdimg.exe e i file di boot del Windows ADK (Deployment Tools).</summary>
public static class OscdimgLocator
{
    public static string ArchFolder => RuntimeInformation.OSArchitecture switch
    {
        Architecture.Arm64 => "arm64",
        Architecture.X86 => "x86",
        _ => "amd64"
    };

    /// <summary>Radice dei Windows Kits 10 dal registro (KitsRoot10), con ripiego sul percorso standard.</summary>
    public static IEnumerable<string> KitsRoots()
    {
        var list = new List<string>();
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var hk = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view)
                    .OpenSubKey(@"SOFTWARE\Microsoft\Windows Kits\Installed Roots");
                if (hk?.GetValue("KitsRoot10") is string s && s.Length > 0) list.Add(s);
            }
            catch { }
        }
        string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (pf86.Length > 0) list.Add(Path.Combine(pf86, @"Windows Kits\10"));
        if (pf.Length > 0) list.Add(Path.Combine(pf, @"Windows Kits\10"));
        return list.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    public static IEnumerable<string> OscdimgFolders()
    {
        foreach (var root in KitsRoots())
        {
            string dt = Path.Combine(root, @"Assessment and Deployment Kit\Deployment Tools");
            yield return Path.Combine(dt, ArchFolder, "Oscdimg");
            foreach (var a in new[] { "amd64", "x86", "arm64" })
                if (a != ArchFolder) yield return Path.Combine(dt, a, "Oscdimg");
        }
    }

    /// <summary>Cerca oscdimg.exe: percorso personalizzato, accanto all'exe, ADK, PATH.</summary>
    public static string? Find(string? customPath)
    {
        if (!string.IsNullOrWhiteSpace(customPath) && File.Exists(customPath)) return customPath;

        // 1) copia portabile accanto all'exe (tools\oscdimg, tools, cartella exe)
        foreach (var f in new[]
        {
            Path.Combine(AppInfo.ToolsDir, "oscdimg.exe"),
            Path.Combine(AppInfo.AppDir, "tools", "oscdimg.exe"),
            Path.Combine(AppInfo.AppDir, "oscdimg.exe")
        })
            if (File.Exists(f)) return f;

        // 2) Windows ADK installato
        if (FindInstalledAdk() is string adk) return adk;
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                string f = Path.Combine(dir.Trim('"'), "oscdimg.exe");
                if (File.Exists(f)) return f;
            }
            catch { }
        }
        return null;
    }

    /// <summary>oscdimg.exe del Windows ADK installato nel sistema (null se assente).</summary>
    public static string? FindInstalledAdk()
    {
        foreach (var dir in OscdimgFolders())
        {
            string f = Path.Combine(dir, "oscdimg.exe");
            if (File.Exists(f)) return f;
        }
        return null;
    }

    public static bool IsPortableCopy(string? path) =>
        path != null && Path.GetFullPath(path).StartsWith(Path.GetFullPath(AppInfo.AppDir), StringComparison.OrdinalIgnoreCase);

    /// <summary>File di boot forniti dall'ADK nella stessa cartella di oscdimg.exe.</summary>
    public static (string Bios, string Uefi, string UefiNoPrompt) AdkBootFiles(string? oscdimgPath)
    {
        string dir = oscdimgPath != null ? Path.GetDirectoryName(oscdimgPath) ?? "" : "";
        string F(string n) { string f = Path.Combine(dir, n); return File.Exists(f) ? f : ""; }
        return (F("etfsboot.com"), F("efisys.bin"), F("efisys_noprompt.bin"));
    }

    public static string FileVersion(string path)
    {
        try { return System.Diagnostics.FileVersionInfo.GetVersionInfo(path).FileVersion ?? "?"; }
        catch { return "?"; }
    }
}
