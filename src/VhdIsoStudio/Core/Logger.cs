using System.IO;
using System.Text;

namespace VhdIsoStudio.Core;

public enum LogLevel { Debug, Info, Warn, Error, Success }

/// <summary>
/// Log thread-safe su file giornaliero (%ProgramData%\VhdIsoStudio\Logs) con conservazione
/// di 14 giorni, piu' evento per mostrare le righe nella UI.
/// </summary>
public static class Logger
{
    private static readonly object Sync = new();
    private static string? _file;
    private const int KeepDays = 14;

    public static event Action<DateTime, LogLevel, string>? LineWritten;

    public static string CurrentFile => _file ?? "";

    public static void Init()
    {
        string dir = AppInfo.LogDir;
        try
        {
            Directory.CreateDirectory(dir);
            // verifica scrivibilita'
            string probe = Path.Combine(dir, ".probe");
            File.WriteAllText(probe, "x");
            File.Delete(probe);
        }
        catch
        {
            dir = Path.Combine(AppInfo.UserDataDir, "Logs");
            Directory.CreateDirectory(dir);
        }
        _file = Path.Combine(dir, $"{AppInfo.Id}_{DateTime.Now:yyyyMMdd}.log");
        Cleanup(dir);
    }

    private static void Cleanup(string dir)
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(dir, AppInfo.Id + "_*.log"))
            {
                if (File.GetLastWriteTime(f) < DateTime.Now.AddDays(-KeepDays))
                    File.Delete(f);
            }
        }
        catch { /* pulizia best-effort */ }
    }

    public static void Write(LogLevel level, string message)
    {
        var now = DateTime.Now;
        string line = $"{now:yyyy-MM-dd HH:mm:ss.fff} [{level.ToString().ToUpperInvariant(),-7}] {message}";
        lock (Sync)
        {
            try
            {
                if (_file != null)
                    File.AppendAllText(_file, line + Environment.NewLine, Encoding.UTF8);
            }
            catch { /* il log non deve mai far crashare l'app */ }
        }
        try { LineWritten?.Invoke(now, level, message); } catch { }
    }

    public static void Debug(string m) => Write(LogLevel.Debug, m);
    public static void Info(string m) => Write(LogLevel.Info, m);
    public static void Warn(string m) => Write(LogLevel.Warn, m);
    public static void Error(string m) => Write(LogLevel.Error, m);
    public static void Success(string m) => Write(LogLevel.Success, m);
    public static void Error(string m, Exception ex) => Write(LogLevel.Error, m + Environment.NewLine + ex);
}
