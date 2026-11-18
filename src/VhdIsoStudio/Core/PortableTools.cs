using System.IO;

namespace VhdIsoStudio.Core;

/// <summary>
/// Rende oscdimg.exe (e i file di boot etfsboot.com / efisys*.bin) disponibile in modo PORTABILE,
/// in &lt;cartella exe&gt;\tools\oscdimg, senza installare nulla nel sistema.
///
/// Strategie, in ordine:
///  1. se il Windows ADK e' gia' installato, copia i file dalla sua cartella Oscdimg;
///  2. altrimenti scarica adksetup.exe da Microsoft (firma verificata), crea un "layout" offline
///     dei soli Deployment Tools (/layout ... /features OptionId.DeploymentTools) ed estrae l'MSI
///     "Oscdimg (DesktopEditions)" con un'installazione amministrativa (msiexec /a), che copia i file
///     in una cartella senza registrare il prodotto nel sistema.
/// Il risultato viene verificato (firma Microsoft di oscdimg.exe) e i file temporanei eliminati.
///
/// Licenza: oscdimg e' un componente Microsoft del Windows ADK; non e' incluso nel repository ne'
/// nelle release dell'app. Viene scaricato da Microsoft sul PC dell'utente.
/// </summary>
public static class PortableTools
{
    private static readonly string[] BootFiles = { "etfsboot.com", "efisys.bin", "efisys_noprompt.bin" };

    public static string PortableOscdimg => Path.Combine(AppInfo.ToolsDir, "oscdimg.exe");
    public static bool HasPortableOscdimg => File.Exists(PortableOscdimg);

    public static async Task<bool> AcquireAsync(string? adkUrl, IProgress<JobProgress> p, CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(AppInfo.ToolsDir);
            string probe = Path.Combine(AppInfo.ToolsDir, ".probe");
            File.WriteAllText(probe, "x");
            File.Delete(probe);
        }
        catch (Exception ex)
        {
            throw new UserFacingException("ErrPortableNotWritable", $"{AppInfo.ToolsDir} ({ex.Message})");
        }

        // 1) ADK gia' installato: copia veloce
        if (OscdimgLocator.FindInstalledAdk() is string installed)
        {
            p.Report(new JobProgress(Loc.T("StagePortableCopy"), null, installed));
            Logger.Info("Copia portabile dall'ADK installato: " + installed);
            if (CopyFrom(Path.GetDirectoryName(installed)!) && Verify()) return true;
        }

        // 2) layout offline + estrazione amministrativa dell'MSI
        string? setup = await AdkInstaller.DownloadVerifiedAsync(adkUrl, p, ct).ConfigureAwait(false);
        if (setup == null) return false;

        string work = Path.Combine(AppInfo.WorkDir, $"adk_{DateTime.Now:yyyyMMdd_HHmmss}");
        string layout = Path.Combine(work, "layout");
        string extract = Path.Combine(work, "extract");
        Directory.CreateDirectory(work);
        try
        {
            p.Report(new JobProgress(Loc.T("StagePortableLayout"), null, Loc.T("StagePortableLayoutDetail")));
            string log = Path.Combine(AppInfo.LogDir, $"adksetup_layout_{DateTime.Now:yyyyMMdd_HHmmss}.log");
            var r = await ProcessRunner.RunAsync(setup,
                $"/quiet /norestart /ceip off /layout \"{layout}\" /features OptionId.DeploymentTools /log \"{log}\"",
                null, ct, timeout: TimeSpan.FromMinutes(90)).ConfigureAwait(false);
            if (r.Cancelled) throw new OperationCanceledException(ct);
            if (r.ExitCode is not (0 or 3010)) Logger.Warn($"adksetup /layout ha restituito {r.ExitCode} (log: {log}); provo comunque a cercare l'MSI.");

            string? msi = FindOscdimgMsi(layout);
            if (msi == null)
            {
                Logger.Error("MSI di Oscdimg non trovato nel layout ADK: " + layout);
                return false;
            }
            Logger.Info("MSI Oscdimg: " + msi);

            p.Report(new JobProgress(Loc.T("StagePortableExtract"), null, Path.GetFileName(msi)));
            Directory.CreateDirectory(extract);
            string msiLog = Path.Combine(AppInfo.LogDir, $"msiexec_oscdimg_{DateTime.Now:yyyyMMdd_HHmmss}.log");
            var m = await ProcessRunner.RunAsync(ProcessRunner.SystemExe("msiexec.exe"),
                $"/a \"{msi}\" /qn TARGETDIR=\"{extract}\" /L*v \"{msiLog}\"",
                null, ct, timeout: TimeSpan.FromMinutes(15)).ConfigureAwait(false);
            if (m.Cancelled) throw new OperationCanceledException(ct);
            if (m.ExitCode is not (0 or 3010))
            {
                Logger.Error($"msiexec /a ha restituito {m.ExitCode}. Log: {msiLog}");
                return false;
            }

            string? exe = FindExtractedOscdimg(extract);
            if (exe == null)
            {
                Logger.Error("oscdimg.exe non trovato dopo l'estrazione: " + extract);
                return false;
            }
            p.Report(new JobProgress(Loc.T("StagePortableCopy"), null, exe));
            return CopyFrom(Path.GetDirectoryName(exe)!) && Verify();
        }
        finally
        {
            await Task.Run(() =>
            {
                try { Directory.Delete(work, true); } catch (Exception ex) { Logger.Warn("Pulizia non riuscita: " + ex.Message); }
                AdkInstaller.TryDelete(setup);
            }).ConfigureAwait(false);
        }
    }

    private static string? FindOscdimgMsi(string layout)
    {
        if (!Directory.Exists(layout)) return null;
        var all = Directory.EnumerateFiles(layout, "*.msi", SearchOption.AllDirectories)
            .Where(f => Path.GetFileName(f).Contains("Oscdimg", StringComparison.OrdinalIgnoreCase)).ToList();
        return all.FirstOrDefault(f => Path.GetFileName(f).Contains("DesktopEditions", StringComparison.OrdinalIgnoreCase))
               ?? all.FirstOrDefault();
    }

    /// <summary>Preferisce la cartella dell'architettura del sistema (amd64/arm64), poi x86 (gira ovunque).</summary>
    private static string? FindExtractedOscdimg(string root)
    {
        var all = Directory.EnumerateFiles(root, "oscdimg.exe", SearchOption.AllDirectories).ToList();
        string Arch(string f) => Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(f)) ?? "") ?? "";
        return all.FirstOrDefault(f => Arch(f).Equals(OscdimgLocator.ArchFolder, StringComparison.OrdinalIgnoreCase))
               ?? all.FirstOrDefault(f => Arch(f).Equals("x86", StringComparison.OrdinalIgnoreCase))
               ?? all.FirstOrDefault();
    }

    private static bool CopyFrom(string dir)
    {
        try
        {
            File.Copy(Path.Combine(dir, "oscdimg.exe"), PortableOscdimg, overwrite: true);
            foreach (var b in BootFiles)
            {
                string src = Path.Combine(dir, b);
                if (File.Exists(src)) File.Copy(src, Path.Combine(AppInfo.ToolsDir, b), overwrite: true);
                else Logger.Warn($"File di boot non presente nella sorgente: {b}");
            }
            File.WriteAllText(Path.Combine(AppInfo.ToolsDir, "SOURCE.txt"),
                $"oscdimg.exe and boot files copied from: {dir}{Environment.NewLine}" +
                $"Date: {DateTime.Now:yyyy-MM-dd HH:mm}{Environment.NewLine}" +
                "Microsoft Windows ADK component - see Microsoft license terms. Not redistributed by VHD ISO Studio.");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("Copia degli strumenti portabili non riuscita: " + ex.Message);
            return false;
        }
    }

    private static bool Verify()
    {
        if (!File.Exists(PortableOscdimg)) return false;
        if (!SignatureVerifier.IsTrustedMicrosoftFile(PortableOscdimg, out string who))
        {
            Logger.Error($"oscdimg.exe portabile senza firma Microsoft valida ({who}): rimosso.");
            AdkInstaller.TryDelete(PortableOscdimg);
            return false;
        }
        Logger.Success($"oscdimg portabile pronto: {PortableOscdimg} (v{OscdimgLocator.FileVersion(PortableOscdimg)}, firmato: {who})");
        return true;
    }
}
