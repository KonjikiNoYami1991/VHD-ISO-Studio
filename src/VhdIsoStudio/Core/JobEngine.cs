using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace VhdIsoStudio.Core;

public sealed record JobProgress(string Stage, double? Percent, string? Detail);

public enum CaptureMode { Direct, CopyToStaging, DismWim }
public enum WimCompression { Max, Fast, None }

public sealed class DismOptions
{
    public string ImageName { get; set; } = "";
    public string Description { get; set; } = "";
    public WimCompression Compression { get; set; } = WimCompression.Max;
    public bool CheckIntegrity { get; set; } = true;
    public bool Verify { get; set; } = true;
    public bool Bootable { get; set; }
    public bool NoRpFix { get; set; }
    public bool ExtendedAttributes { get; set; }
    public string ConfigFile { get; set; } = "";
}

public sealed class JobResult
{
    public string IsoPath { get; set; } = "";
    public long IsoSize { get; set; }
    public string Sha256 { get; set; } = "";
    public string Summary { get; set; } = "";
    public TimeSpan Elapsed { get; set; }
    public string? WimPath { get; set; }
}

/// <summary>Richiesta di lavoro manuale (sorgente = volume montato o cartella).</summary>
public sealed class ManualJob
{
    public string SourceRoot { get; set; } = "";
    public long SourceUsedBytes { get; set; }
    public CaptureMode Mode { get; set; } = CaptureMode.Direct;
    public string StagingDir { get; set; } = "";
    public bool KeepStaging { get; set; }
    public DismOptions Dism { get; set; } = new();
    public bool InjectIntoBaseMedia { get; set; }
    public string BaseMediaPath { get; set; } = "";
    public string IsoPath { get; set; } = "";
    public OscdimgOptions Oscdimg { get; set; } = new();
    public bool ComputeHash { get; set; } = true;
}

/// <summary>Richiesta modalita' automatica.</summary>
public sealed class AutoJob
{
    public string VhdPath { get; set; } = "";
    public string OutputFolder { get; set; } = "";
    public string BaseMediaPath { get; set; } = "";
    public bool ComputeHash { get; set; } = true;
}

/// <summary>Esegue le pipeline: cattura (diretta, robocopy, DISM) e creazione ISO con oscdimg.</summary>
public sealed class JobEngine
{
    private readonly string _oscdimg;
    private readonly IProgress<JobProgress> _p;
    private readonly CancellationToken _ct;

    public JobEngine(string oscdimgPath, IProgress<JobProgress> progress, CancellationToken ct)
    {
        _oscdimg = oscdimgPath; _p = progress; _ct = ct;
    }

    // ======================================================================= AUTO
    public async Task<JobResult> RunAutoAsync(AutoJob job)
    {
        var sw = Stopwatch.StartNew();
        if (!File.Exists(job.VhdPath)) throw new UserFacingException("ErrVhdNotFound", job.VhdPath);
        Directory.CreateDirectory(job.OutputFolder);
        EnsureWritable(job.OutputFolder);

        _p.Report(new JobProgress(Loc.T("StageMount"), null, Path.GetFileName(job.VhdPath)));
        var mount = await DiskImageService.MountAsync(job.VhdPath, readOnly: true, assignLetters: true, _ct);
        try
        {
            var candidates = mount.Volumes.Where(v => v.HasData && v.DriveLetter.Length > 0).ToList();
            if (candidates.Count == 0) throw new UserFacingException("ErrNoVolumes");

            VolumeInfo vol = candidates.FirstOrDefault(v => v.IsSetupMedia)
                             ?? candidates.FirstOrDefault(v => v.IsWinPe)
                             ?? candidates.FirstOrDefault(v => v.IsWindowsOs)
                             ?? candidates.OrderByDescending(v => v.Used).First();
            Logger.Info($"Auto: volume scelto {vol.Root} ({vol.Kind}, {vol.UsedText} usati)");

            string baseName = Format.SafeFileName(Path.GetFileNameWithoutExtension(job.VhdPath));
            string iso = UniquePath(Path.Combine(job.OutputFolder, baseName + ".iso"));
            string label = OscdimgOptions.SanitizeLabel(vol.Label.Length > 0 ? vol.Label : baseName);
            if (label.Length == 0) label = "VHD_IMAGE";

            var manual = new ManualJob
            {
                SourceRoot = vol.Root,
                SourceUsedBytes = vol.Used,
                IsoPath = iso,
                ComputeHash = job.ComputeHash
            };

            if (vol.IsSetupMedia || vol.IsWinPe)
            {
                // supporto avviabile: ISO diretta con boot BIOS+UEFI dai file del supporto (o dell'ADK)
                manual.Mode = CaptureMode.Direct;
                manual.Oscdimg = BootableFor(vol.Root, vol.BiosBootFile, vol.UefiBootFile, label);
                manual.Oscdimg.Label = label;
            }
            else if (vol.IsWindowsOs && job.BaseMediaPath.Length > 0)
            {
                // Windows installato + supporto base: cattura WIM e ricostruisce un'ISO di installazione
                manual.Mode = CaptureMode.DismWim;
                manual.InjectIntoBaseMedia = true;
                manual.BaseMediaPath = job.BaseMediaPath;
                manual.StagingDir = UniqueDir(Path.Combine(job.OutputFolder, baseName + "_staging"));
                manual.Dism = new DismOptions { ImageName = baseName, Description = $"Captured from {Path.GetFileName(job.VhdPath)}" };
                manual.Oscdimg = OscdimgOptions.WindowsBootPreset();
                manual.Oscdimg.Label = label;
            }
            else if (vol.IsWindowsOs)
            {
                // Windows installato senza supporto base: WIM catturato + ISO dati che lo contiene
                manual.Mode = CaptureMode.DismWim;
                manual.StagingDir = UniqueDir(Path.Combine(job.OutputFolder, baseName + "_staging"));
                manual.Dism = new DismOptions { ImageName = baseName, Description = $"Captured from {Path.GetFileName(job.VhdPath)}" };
                manual.Oscdimg = OscdimgOptions.DataPreset();
                manual.Oscdimg.Label = label;
            }
            else
            {
                // disco dati: ISO UDF direttamente dal volume
                manual.Mode = CaptureMode.Direct;
                manual.Oscdimg = OscdimgOptions.DataPreset();
                manual.Oscdimg.Label = label;
            }

            var res = await RunManualAsync(manual);
            res.Summary = Loc.T("AutoSummary_" + (vol.IsSetupMedia || vol.IsWinPe ? "Boot" : vol.IsWindowsOs ? (job.BaseMediaPath.Length > 0 ? "OsMedia" : "OsWim") : "Data"));
            res.Elapsed = sw.Elapsed;
            return res;
        }
        finally
        {
            if (mount.OwnedByUs)
            {
                _p.Report(new JobProgress(Loc.T("StageDismount"), null, null));
                try { await DiskImageService.DismountAsync(job.VhdPath, CancellationToken.None); }
                catch (Exception ex) { Logger.Warn("Smontaggio non riuscito: " + ex.Message); }
            }
        }
    }

    private OscdimgOptions BootableFor(string root, string bios, string uefi, string label)
    {
        var o = OscdimgOptions.WindowsBootPreset();
        var adk = OscdimgLocator.AdkBootFiles(_oscdimg);
        if (bios.Length == 0) bios = adk.Bios;
        if (uefi.Length == 0) uefi = adk.Uefi;
        o.BiosBootFile = bios; o.UefiBootFile = uefi;
        o.Boot = bios.Length > 0 && uefi.Length > 0 ? BootMode.BiosAndUefi
               : uefi.Length > 0 ? BootMode.UefiOnly
               : bios.Length > 0 ? BootMode.BiosOnly : BootMode.None;
        if (o.Boot == BootMode.None) Logger.Warn("Nessun file di boot trovato: ISO non avviabile.");
        o.Label = label;
        return o;
    }

    // ===================================================================== MANUAL
    public async Task<JobResult> RunManualAsync(ManualJob job)
    {
        var sw = Stopwatch.StartNew();
        var result = new JobResult();
        string isoSource = job.SourceRoot;
        bool cleanupStaging = false;

        if (string.IsNullOrWhiteSpace(job.IsoPath) && !job.Oscdimg.ScanOnly) throw new UserFacingException("ValTargetMissing");
        string outDir = Path.GetDirectoryName(Path.GetFullPath(job.IsoPath)) ?? "";
        if (!job.Oscdimg.ScanOnly)
        {
            Directory.CreateDirectory(outDir);
            EnsureWritable(outDir);
        }

        bool stagingCreatedByUs = false;
        if (job.Mode != CaptureMode.Direct && job.StagingDir.Length > 0)
        {
            EnsureSafeStaging(job.StagingDir, job.SourceRoot);
            stagingCreatedByUs = !Directory.Exists(job.StagingDir);
        }
        try
        {
            switch (job.Mode)
            {
                case CaptureMode.Direct:
                    CheckFreeSpace(outDir, job.SourceUsedBytes);
                    break;

                case CaptureMode.CopyToStaging:
                    if (job.StagingDir.Length == 0) throw new UserFacingException("ErrStagingMissing");
                    CheckFreeSpace(job.StagingDir, job.SourceUsedBytes * 2);
                    await RobocopyAsync(job.SourceRoot, job.StagingDir, job.SourceUsedBytes, Array.Empty<string>(), mirror: true);
                    isoSource = job.StagingDir;
                    cleanupStaging = !job.KeepStaging;
                    break;

                case CaptureMode.DismWim:
                {
                    if (job.StagingDir.Length == 0) throw new UserFacingException("ErrStagingMissing");
                    Directory.CreateDirectory(job.StagingDir);
                    CheckFreeSpace(job.StagingDir, (long)(job.SourceUsedBytes * 1.2));
                    string wim;
                    if (job.InjectIntoBaseMedia)
                    {
                        await PrepareBaseMediaAsync(job.BaseMediaPath, job.StagingDir);
                        wim = Path.Combine(job.StagingDir, "sources", "install.wim");
                        var root = job.StagingDir;
                        var bios = Path.Combine(root, @"boot\etfsboot.com");
                        var uefi = Path.Combine(root, @"efi\microsoft\boot\efisys.bin");
                        if (job.Oscdimg.Boot != BootMode.None)
                        {
                            if (job.Oscdimg.BiosBootFile.Length == 0 && File.Exists(bios)) job.Oscdimg.BiosBootFile = bios;
                            if (job.Oscdimg.UefiBootFile.Length == 0 && File.Exists(uefi)) job.Oscdimg.UefiBootFile = uefi;
                            var adk = OscdimgLocator.AdkBootFiles(_oscdimg);
                            if (job.Oscdimg.BiosBootFile.Length == 0) job.Oscdimg.BiosBootFile = adk.Bios;
                            if (job.Oscdimg.UefiBootFile.Length == 0) job.Oscdimg.UefiBootFile = adk.Uefi;
                        }
                    }
                    else
                    {
                        // il WIM va nella cartella di lavoro, che diventa la radice dell'ISO
                        string wimName = Format.SafeFileName(job.Dism.ImageName.Length > 0 ? job.Dism.ImageName : "install");
                        wim = Path.Combine(job.StagingDir, wimName + ".wim");
                    }
                    await DismCaptureAsync(job.SourceRoot, wim, job.Dism);
                    result.WimPath = wim;
                    isoSource = job.StagingDir;
                    cleanupStaging = !job.KeepStaging;
                    break;
                }
            }

            // ------------------------------------------------ oscdimg
            var errs = job.Oscdimg.Validate(isoSource, job.IsoPath);
            if (errs.Count > 0) throw new UserFacingException(errs[0]);

            if (!job.Oscdimg.ScanOnly && File.Exists(job.IsoPath))
            {
                Logger.Warn("Sovrascrittura ISO esistente: " + job.IsoPath);
                File.Delete(job.IsoPath);
            }
            await OscdimgAsync(job.Oscdimg.BuildArguments(isoSource, job.IsoPath));

            if (job.Oscdimg.ScanOnly)
            {
                result.Summary = Loc.T("ScanOnlyDone");
                result.Elapsed = sw.Elapsed;
                return result;
            }

            var fi = new FileInfo(job.IsoPath);
            if (!fi.Exists || fi.Length == 0) throw new UserFacingException("ErrIsoMissing");
            result.IsoPath = fi.FullName;
            result.IsoSize = fi.Length;
            Logger.Success($"ISO creata: {fi.FullName} ({Format.Bytes(fi.Length)})");

            if (job.ComputeHash)
            {
                result.Sha256 = await Sha256Async(fi.FullName);
                await File.WriteAllTextAsync(fi.FullName + ".sha256", $"{result.Sha256} *{fi.Name}{Environment.NewLine}", _ct);
                Logger.Info("SHA-256: " + result.Sha256);
            }
        }
        finally
        {
            if (cleanupStaging && stagingCreatedByUs && job.StagingDir.Length > 0 && Directory.Exists(job.StagingDir))
            {
                await DeleteTreeAsync(job.StagingDir);
                result.WimPath = null; // il WIM era nella cartella temporanea (ed e' incluso nell'ISO)
            }
        }
        result.Elapsed = sw.Elapsed;
        return result;
    }

    // ===================================================================== steps
    private async Task OscdimgAsync(string args)
    {
        string stage = Loc.T("StageOscdimg");
        _p.Report(new JobProgress(stage, null, Loc.T("StageScanning")));
        var r = await ProcessRunner.RunAsync(_oscdimg, args, line =>
        {
            var pct = ProcessRunner.ParsePercent(line);
            _p.Report(new JobProgress(stage, pct, line));
        }, _ct);
        if (r.Cancelled) throw new OperationCanceledException(_ct);
        if (r.ExitCode != 0)
        {
            string last = r.Output.Split('\n').Select(s => s.Trim()).LastOrDefault(s => s.Contains("ERROR", StringComparison.OrdinalIgnoreCase))
                          ?? r.Output.Split('\n').Select(s => s.Trim()).LastOrDefault(s => s.Length > 0) ?? "";
            throw new UserFacingException("ErrOscdimg", $"{r.ExitCode}: {last}");
        }
    }

    private async Task DismCaptureAsync(string source, string wim, DismOptions o)
    {
        string name = (o.ImageName.Length > 0 ? o.ImageName : "Captured image").Replace("\"", "'");
        string desc = o.Description.Replace("\"", "'");
        var a = new List<string>
        {
            "/English", "/Capture-Image",
            $"/ImageFile:\"{wim}\"",
            "/CaptureDir:" + OscdimgOptions.Q(source),
            $"/Name:\"{name}\""
        };
        if (desc.Length > 0) a.Add($"/Description:\"{desc}\"");
        a.Add("/Compress:" + o.Compression.ToString().ToLowerInvariant());
        if (o.CheckIntegrity) a.Add("/CheckIntegrity");
        if (o.Verify) a.Add("/Verify");
        if (o.Bootable) a.Add("/Bootable");
        if (o.NoRpFix) a.Add("/NoRpFix");
        if (o.ExtendedAttributes) a.Add("/EA");
        if (o.ConfigFile.Length > 0) a.Add($"/ConfigFile:\"{o.ConfigFile}\"");
        string logFile = Path.Combine(AppInfo.LogDir, $"dism_{DateTime.Now:yyyyMMdd_HHmmss}.log");
        a.Add($"/LogPath:\"{logFile}\"");

        if (File.Exists(wim)) { Logger.Warn("WIM esistente sovrascritto: " + wim); File.Delete(wim); }
        string stage = Loc.T("StageDism");
        _p.Report(new JobProgress(stage, null, source));
        var r = await ProcessRunner.RunAsync(ProcessRunner.SystemExe("dism.exe"), string.Join(" ", a),
            l => _p.Report(new JobProgress(stage, ProcessRunner.ParsePercent(l), l.Trim('[', ']', ' ', '='))), _ct);
        if (r.Cancelled) throw new OperationCanceledException(_ct);
        if (r.ExitCode != 0) throw new UserFacingException("ErrDism", $"{r.ExitCode} — log: {logFile}");
        Logger.Success($"WIM catturato: {wim} ({Format.Bytes(new FileInfo(wim).Length)})");
    }

    private async Task PrepareBaseMediaAsync(string basePath, string staging)
    {
        if (string.IsNullOrWhiteSpace(basePath)) throw new UserFacingException("ErrBaseMediaMissing");
        bool isIso = basePath.EndsWith(".iso", StringComparison.OrdinalIgnoreCase) && File.Exists(basePath);
        string root = basePath;
        MountResult? m = null;
        try
        {
            if (isIso)
            {
                _p.Report(new JobProgress(Loc.T("StageMountBase"), null, Path.GetFileName(basePath)));
                m = await DiskImageService.MountAsync(basePath, true, true, _ct);
                root = m.Volumes.FirstOrDefault(v => v.DriveLetter.Length > 0)?.Root
                       ?? throw new UserFacingException("ErrBaseMediaInvalid");
            }
            if (!Directory.Exists(Path.Combine(root, "sources")) || !File.Exists(Path.Combine(root, @"sources\boot.wim")))
                throw new UserFacingException("ErrBaseMediaInvalid");

            long size = 0;
            try { size = new DriveInfo(root).TotalSize - new DriveInfo(root).AvailableFreeSpace; } catch { }
            await RobocopyAsync(root, staging, size, new[] { "install.wim", "install.esd", "install*.swm" }, mirror: true);
            // i file copiati da ISO sono in sola lettura: li rendo scrivibili per il WIM e la pulizia
            ClearReadOnly(staging);
        }
        finally
        {
            if (m != null && m.OwnedByUs)
            {
                try { await DiskImageService.DismountAsync(basePath, CancellationToken.None); } catch { }
            }
        }
    }

    private static readonly Regex RoboBytes = new(@"^\s*(?:New File|Newer|Older|Changed|Tweaked|\*EXTRA File)?\s*(\d+)\s+\S", RegexOptions.Compiled);

    private async Task RobocopyAsync(string src, string dst, long totalBytes, string[] excludeFiles, bool mirror)
    {
        Directory.CreateDirectory(dst);
        var a = new List<string>
        {
            OscdimgOptions.Q(src), OscdimgOptions.Q(dst),
            mirror ? "/MIR" : "/E",
            "/COPY:DAT", "/DCOPY:DAT", "/R:1", "/W:1", "/XJ", "/NP", "/NDL", "/BYTES", "/MT:8",
            "/XD", "\"System Volume Information\"", "\"$RECYCLE.BIN\"", "\"$Recycle.Bin\"",
            "/XF", "pagefile.sys", "hiberfil.sys", "swapfile.sys", "DumpStack.log.tmp"
        };
        foreach (var x in excludeFiles) a.Add(x.Contains(' ') ? $"\"{x}\"" : x);
        string stage = Loc.T("StageCopy");
        long copied = 0;
        _p.Report(new JobProgress(stage, totalBytes > 0 ? 0 : null, src));
        var r = await ProcessRunner.RunAsync(ProcessRunner.SystemExe("robocopy.exe"), string.Join(" ", a), line =>
        {
            var m = RoboBytes.Match(line);
            if (m.Success && long.TryParse(m.Groups[1].Value, out long b)) copied += b;
            double? pct = totalBytes > 0 ? Math.Min(99.9, copied * 100.0 / totalBytes) : null;
            _p.Report(new JobProgress(stage, pct, line.Trim()));
        }, _ct);
        if (r.Cancelled) throw new OperationCanceledException(_ct);
        // robocopy: 0-7 = successo (con dettagli), >= 8 = errori
        if (r.ExitCode >= 8) throw new UserFacingException("ErrRobocopy", r.ExitCode.ToString());
    }

    private async Task<string> Sha256Async(string file)
    {
        string stage = Loc.T("StageHash");
        await using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, true);
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buf = new byte[4 << 20];
        long total = fs.Length, done = 0; int n;
        var last = DateTime.MinValue;
        while ((n = await fs.ReadAsync(buf, _ct)) > 0)
        {
            sha.AppendData(buf, 0, n);
            done += n;
            if ((DateTime.Now - last).TotalMilliseconds > 200)
            {
                _p.Report(new JobProgress(stage, total > 0 ? done * 100.0 / total : null, Format.Bytes(done)));
                last = DateTime.Now;
            }
        }
        return Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
    }

    // ===================================================================== utils
    /// <summary>
    /// Blocca cartelle di lavoro pericolose: robocopy /MIR e la pulizia finale cancellano file.
    /// Vietati: radici di unita', cartelle di sistema e profili, cartelle che contengono la sorgente o l'ISO.
    /// </summary>
    private static void EnsureSafeStaging(string staging, string source)
    {
        string full = Path.GetFullPath(staging).TrimEnd('\\') + "\\";
        string? root = Path.GetPathRoot(full);
        if (root == null || string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
            throw new UserFacingException("ErrStagingUnsafe", staging);
        var forbidden = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Path.GetDirectoryName(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)) ?? ""
        }.Where(f => f.Length > 0).Select(f => f.TrimEnd('\\') + "\\");
        foreach (var f in forbidden)
        {
            // la cartella di lavoro non puo' essere (o contenere) una cartella di sistema/profilo
            if (f.StartsWith(full, StringComparison.OrdinalIgnoreCase))
                throw new UserFacingException("ErrStagingUnsafe", staging);
            // ne' stare dentro Windows / Program Files
            if (full.StartsWith(f, StringComparison.OrdinalIgnoreCase) &&
                (f.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows), StringComparison.OrdinalIgnoreCase) ||
                 f.Contains("Program Files", StringComparison.OrdinalIgnoreCase)))
                throw new UserFacingException("ErrStagingUnsafe", staging);
        }
        string src = Path.GetFullPath(source).TrimEnd('\\') + "\\";
        if (src.StartsWith(full, StringComparison.OrdinalIgnoreCase) || full.StartsWith(src, StringComparison.OrdinalIgnoreCase))
            throw new UserFacingException("ErrStagingUnsafe", staging);
    }

    private static void EnsureWritable(string dir)
    {
        try
        {
            string probe = Path.Combine(dir, $".vis_probe_{Guid.NewGuid():N}");
            File.WriteAllText(probe, "x");
            File.Delete(probe);
        }
        catch (Exception ex)
        {
            throw new UserFacingException("ErrNotWritable", $"{dir} ({ex.Message})");
        }
    }

    private static void CheckFreeSpace(string dir, long needed)
    {
        if (needed <= 0) return;
        try
        {
            string? root = Path.GetPathRoot(Path.GetFullPath(dir));
            if (string.IsNullOrEmpty(root) || root.StartsWith(@"\\")) return; // UNC: non verificabile in modo affidabile
            var di = new DriveInfo(root);
            long want = (long)(needed * 1.05) + 200L * 1024 * 1024;
            if (di.AvailableFreeSpace < want)
                throw new UserFacingException("ErrNoSpace", $"{root} — {Format.Bytes(di.AvailableFreeSpace)} / {Format.Bytes(want)}");
        }
        catch (UserFacingException) { throw; }
        catch (Exception ex) { Logger.Warn("Verifica spazio libero non riuscita: " + ex.Message); }
    }

    public static string UniquePath(string path)
    {
        if (!File.Exists(path)) return path;
        string dir = Path.GetDirectoryName(path)!, name = Path.GetFileNameWithoutExtension(path), ext = Path.GetExtension(path);
        for (int i = 2; ; i++)
        {
            string p = Path.Combine(dir, $"{name} ({i}){ext}");
            if (!File.Exists(p)) return p;
        }
    }

    public static string UniqueDir(string path)
    {
        if (!Directory.Exists(path) && !File.Exists(path)) return path;
        for (int i = 2; ; i++)
        {
            string p = $"{path}_{i}";
            if (!Directory.Exists(p) && !File.Exists(p)) return p;
        }
    }

    private static void ClearReadOnly(string dir)
    {
        foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
        {
            try
            {
                var at = File.GetAttributes(f);
                if ((at & FileAttributes.ReadOnly) != 0) File.SetAttributes(f, at & ~FileAttributes.ReadOnly);
            }
            catch { }
        }
    }

    private async Task DeleteTreeAsync(string dir)
    {
        _p.Report(new JobProgress(Loc.T("StageCleanup"), null, dir));
        await Task.Run(() =>
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    ClearReadOnly(dir);
                    Directory.Delete(dir, true);
                    Logger.Info("Cartella temporanea eliminata: " + dir);
                    return;
                }
                catch (Exception ex)
                {
                    if (attempt == 2) Logger.Warn($"Impossibile eliminare {dir}: {ex.Message}");
                    Thread.Sleep(1000);
                }
            }
        });
    }
}

/// <summary>Errore con messaggio localizzato da mostrare all'utente (il dettaglio tecnico va nel log).</summary>
public sealed class UserFacingException : Exception
{
    public string Key { get; }
    public string? Detail { get; }
    public UserFacingException(string key, string? detail = null) : base(key + (detail != null ? ": " + detail : ""))
    {
        Key = key; Detail = detail;
    }
    public string Localized => Loc.T(Key) + (Detail != null ? Environment.NewLine + Detail : "");
}
