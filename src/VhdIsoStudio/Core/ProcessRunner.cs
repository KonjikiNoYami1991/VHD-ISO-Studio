using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace VhdIsoStudio.Core;

public sealed record ProcessResult(int ExitCode, string Output, bool Cancelled);

/// <summary>
/// Esegue processi esterni senza finestra, legge stdout/stderr carattere per carattere
/// (oscdimg e DISM aggiornano la percentuale con '\r'), supporta annullamento con kill dell'albero.
/// </summary>
public static class ProcessRunner
{
    static ProcessRunner()
    {
        // abilita le code page OEM (cp850/cp437) per decodificare l'output dei tool di console
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static Encoding OemEncoding
    {
        get
        {
            try { return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage); }
            catch { return Encoding.UTF8; }
        }
    }

    public static string SystemExe(string name) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), name);

    public static async Task<ProcessResult> RunAsync(
        string exe, string arguments, Action<string>? onLine, CancellationToken ct,
        Encoding? encoding = null, string? workingDir = null, TimeSpan? timeout = null, string? logArgs = null)
    {
        var psi = new ProcessStartInfo(exe, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = encoding ?? OemEncoding,
            StandardErrorEncoding = encoding ?? OemEncoding,
            WorkingDirectory = workingDir ?? Path.GetTempPath()
        };
        Logger.Info($"> {exe} {logArgs ?? arguments}");

        using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var all = new StringBuilder();
        var sync = new object();
        void Emit(string line)
        {
            line = line.TrimEnd();
            if (line.Length == 0) return;
            lock (sync) all.AppendLine(line);
            try { onLine?.Invoke(line); } catch { }
        }

        if (!p.Start()) throw new InvalidOperationException("Impossibile avviare " + exe);

        var tOut = Pump(p.StandardOutput, Emit);
        var tErr = Pump(p.StandardError, Emit);

        bool cancelled = false;
        using var timeoutCts = timeout.HasValue ? new CancellationTokenSource(timeout.Value) : new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        try
        {
            await p.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
            try { p.Kill(entireProcessTree: true); } catch { }
            try { await p.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false); } catch { }
            if (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
                Logger.Warn($"Timeout: {Path.GetFileName(exe)} terminato.");
        }
        try { await Task.WhenAll(tOut, tErr).WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false); } catch { }

        int code = cancelled ? -1 : p.ExitCode;
        Logger.Info($"< {Path.GetFileName(exe)} exit {code}{(cancelled ? " (annullato)" : "")}");
        lock (sync) return new ProcessResult(code, all.ToString(), cancelled);
    }

    private static async Task Pump(StreamReader reader, Action<string> emit)
    {
        var buf = new char[4096];
        var line = new StringBuilder();
        int n;
        while ((n = await reader.ReadAsync(buf, 0, buf.Length).ConfigureAwait(false)) > 0)
        {
            for (int i = 0; i < n; i++)
            {
                char c = buf[i];
                if (c == '\r' || c == '\n')
                {
                    if (line.Length > 0) { emit(line.ToString()); line.Clear(); }
                }
                else if (c != '\0') line.Append(c);
            }
        }
        if (line.Length > 0) emit(line.ToString());
    }

    /// <summary>Estrae una percentuale (es. "45% complete", "[=== 45.0% ===]") da una riga.</summary>
    public static double? ParsePercent(string line)
    {
        var m = System.Text.RegularExpressions.Regex.Match(line, @"(\d{1,3}(?:[.,]\d+)?)\s*%");
        if (!m.Success) return null;
        if (double.TryParse(m.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
            && v >= 0 && v <= 100) return v;
        return null;
    }
}
