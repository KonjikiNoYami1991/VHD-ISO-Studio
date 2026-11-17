using System.Net.Http;

namespace VhdIsoStudio.Core;

/// <summary>
/// Installa solo la funzionalita' "Deployment Tools" del Windows ADK (contiene oscdimg.exe,
/// etfsboot.com, efisys.bin). Scarica adksetup.exe da Microsoft, ne verifica la firma Authenticode
/// e lo esegue in modalita' silenziosa. Ripiego: winget (Microsoft.WindowsADK).
/// Fonti: manifest winget-pkgs Microsoft.WindowsADK 10.1.26100.9457 (InstallerUrl fwlink 2289980),
/// Microsoft Learn "Install the Windows ADK offline" (/quiet /features OptionId.DeploymentTools).
/// </summary>
public static class AdkInstaller
{
    public const string DefaultUrl = "https://go.microsoft.com/fwlink/?linkid=2289980";
    public const string FallbackUrl = "https://download.microsoft.com/download/2/d/9/2d9c8902-3fcd-48a6-a22a-432b08bed61e/ADK/adksetup.exe";
    public const string SilentArgs = "/quiet /norestart /ceip off /features OptionId.DeploymentTools";

    public static async Task<bool> InstallAsync(string? url, IProgress<JobProgress> progress, CancellationToken ct)
    {
        Directory.CreateDirectory(AppInfo.DownloadDir);
        string setup = Path.Combine(AppInfo.DownloadDir, "adksetup.exe");
        bool downloaded = false;

        foreach (var u in new[] { string.IsNullOrWhiteSpace(url) ? DefaultUrl : url!, FallbackUrl }.Distinct())
        {
            try
            {
                await DownloadAsync(u, setup, progress, ct).ConfigureAwait(false);
                if (!SignatureVerifier.IsTrustedMicrosoftFile(setup, out string who))
                {
                    Logger.Error($"Firma di adksetup.exe NON valida o non Microsoft ({who}). File eliminato.");
                    TryDelete(setup);
                    continue;
                }
                Logger.Success("adksetup.exe firmato da: " + who);
                downloaded = true;
                break;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Logger.Warn($"Download da {u} non riuscito: {ex.Message}");
            }
        }

        if (downloaded)
        {
            progress.Report(new JobProgress(Loc.T("StageAdkInstall"), null, Loc.T("StageAdkInstallDetail")));
            string log = Path.Combine(AppInfo.LogDir, $"adksetup_{DateTime.Now:yyyyMMdd_HHmmss}.log");
            var r = await ProcessRunner.RunAsync(setup, $"{SilentArgs} /log \"{log}\"", null, ct,
                timeout: TimeSpan.FromMinutes(60)).ConfigureAwait(false);
            if (r.Cancelled) throw new OperationCanceledException(ct);
            if (r.ExitCode is 0 or 3010)
            {
                if (r.ExitCode == 3010) Logger.Warn("ADK installato: Windows segnala un riavvio in sospeso (non verra' eseguito).");
                return true;
            }
            Logger.Error($"adksetup.exe ha restituito {r.ExitCode}. Log: {log}");
        }

        // ripiego winget
        string? winget = FindWinget();
        if (winget == null) return false;
        progress.Report(new JobProgress(Loc.T("StageAdkWinget"), null, null));
        var w = await ProcessRunner.RunAsync(winget,
            $"install --id Microsoft.WindowsADK -e --source winget --silent --accept-package-agreements --accept-source-agreements --override \"{SilentArgs}\"",
            l => progress.Report(new JobProgress(Loc.T("StageAdkWinget"), ProcessRunner.ParsePercent(l), l)), ct,
            System.Text.Encoding.UTF8, timeout: TimeSpan.FromMinutes(60)).ConfigureAwait(false);
        if (w.Cancelled) throw new OperationCanceledException(ct);
        return w.ExitCode == 0;
    }

    private static string? FindWinget()
    {
        string la = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string f = Path.Combine(la, @"Microsoft\WindowsApps\winget.exe");
        if (File.Exists(f)) return f;
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            try { string p = Path.Combine(dir.Trim('"'), "winget.exe"); if (File.Exists(p)) return p; } catch { }
        }
        return null;
    }

    private static async Task DownloadAsync(string url, string dest, IProgress<JobProgress> progress, CancellationToken ct)
    {
        Logger.Info("Download: " + url);
        using var handler = new HttpClientHandler { UseProxy = true, DefaultProxyCredentials = System.Net.CredentialCache.DefaultCredentials };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"{AppInfo.Id}/{AppInfo.Version}");
        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        long? total = resp.Content.Headers.ContentLength;
        string tmp = dest + ".part";
        await using (var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
        await using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
        {
            var buf = new byte[81920];
            long done = 0; int n;
            while ((n = await src.ReadAsync(buf, ct).ConfigureAwait(false)) > 0)
            {
                await fs.WriteAsync(buf.AsMemory(0, n), ct).ConfigureAwait(false);
                done += n;
                progress.Report(new JobProgress(Loc.T("StageAdkDownload"),
                    total > 0 ? done * 100.0 / total.Value : null, Format.Bytes(done)));
            }
        }
        File.Move(tmp, dest, true);
    }

    private static void TryDelete(string f) { try { File.Delete(f); } catch { } }
}
