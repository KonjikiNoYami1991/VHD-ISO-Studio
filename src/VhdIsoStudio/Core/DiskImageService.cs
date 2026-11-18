using System.IO;
using System.Text;
using System.Text.Json;

namespace VhdIsoStudio.Core;

public sealed class VolumeInfo
{
    public int PartitionNumber { get; set; }
    public string DriveLetter { get; set; } = "";
    public string VolumePath { get; set; } = "";
    public string Label { get; set; } = "";
    public string FileSystem { get; set; } = "";
    public long Size { get; set; }
    public long Free { get; set; }
    public string PartitionType { get; set; } = "";

    // rilevamento contenuto (riempito da ContentDetector)
    public bool IsWindowsOs { get; set; }
    public bool IsSetupMedia { get; set; }
    public bool IsWinPe { get; set; }
    public string BiosBootFile { get; set; } = "";
    public string UefiBootFile { get; set; } = "";

    public string Root => DriveLetter.Length > 0 ? DriveLetter + ":\\" : VolumePath;
    public long Used => Math.Max(0, Size - Free);
    public bool HasData => FileSystem.Length > 0 && Root.Length > 0;
    public string Display => $"{(DriveLetter.Length > 0 ? DriveLetter + ":" : "—")}  {Label}";
    public string SizeText => Format.Bytes(Size);
    public string UsedText => Format.Bytes(Used);
    public string Kind => IsSetupMedia ? Loc.T("KindSetup") : IsWinPe ? Loc.T("KindWinPe")
        : IsWindowsOs ? Loc.T("KindWindows") : FileSystem.Length == 0 ? Loc.T("KindNoFs") : Loc.T("KindData");
}

public sealed class MountResult
{
    public string ImagePath { get; set; } = "";
    public int DiskNumber { get; set; } = -1;
    public bool OwnedByUs { get; set; }
    public List<VolumeInfo> Volumes { get; set; } = new();
}

/// <summary>
/// Monta/smonta VHD, VHDX e ISO tramite i cmdlet del modulo Storage (Mount-DiskImage, Get-Partition,
/// Get-Volume) eseguiti in Windows PowerShell 5.1 nascosto, con output JSON.
/// Tiene traccia delle immagini montate dall'app per smontarle all'uscita.
/// </summary>
public static class DiskImageService
{
    private static readonly HashSet<string> Owned = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Sync = new();

    public static IReadOnlyCollection<string> OwnedImages { get { lock (Sync) return Owned.ToList(); } }

    private static string PsLiteral(string s) => "'" + s.Replace("'", "''") + "'";

    private static async Task<string> RunPs(string script, CancellationToken ct, TimeSpan? timeout = null)
    {
        string full = "$ErrorActionPreference='Stop';$ProgressPreference='SilentlyContinue';" +
                      "[Console]::OutputEncoding=[Text.Encoding]::UTF8;" +
                      "try{" + script + "}catch{[Console]::Out.Write((@{error=$_.Exception.Message}|ConvertTo-Json -Compress));exit 1}";
        string enc = Convert.ToBase64String(Encoding.Unicode.GetBytes(full));
        string ps = ProcessRunner.SystemExe(@"WindowsPowerShell\v1.0\powershell.exe");
        var r = await ProcessRunner.RunAsync(ps, "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + enc,
            null, ct, Encoding.UTF8, null, timeout ?? TimeSpan.FromMinutes(3), "-NoProfile -NonInteractive -EncodedCommand <script Storage>").ConfigureAwait(false);
        if (r.Cancelled) throw new OperationCanceledException(ct);
        string outText = r.Output.Trim();
        // l'ultima riga JSON e' il risultato
        string json = outText.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.StartsWith('{') || l.StartsWith('[')) ?? "";
        if (json.StartsWith('{'))
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out var err))
                throw new InvalidOperationException(err.GetString());
        }
        if (r.ExitCode != 0) throw new InvalidOperationException(outText.Length > 0 ? outText : "PowerShell exit " + r.ExitCode);
        return json;
    }

    /// <summary>Monta un'immagine (VHD/VHDX/ISO) e restituisce i volumi con lettera di unita'.</summary>
    public static async Task<MountResult> MountAsync(string imagePath, bool readOnly, bool assignLetters, CancellationToken ct)
    {
        string p = PsLiteral(Path.GetFullPath(imagePath));
        string access = readOnly ? "ReadOnly" : "ReadWrite";
        bool isIso = imagePath.EndsWith(".iso", StringComparison.OrdinalIgnoreCase);
        string mountCmd = isIso
            ? $"Mount-DiskImage -ImagePath $p -PassThru | Out-Null"
            : $"Mount-DiskImage -ImagePath $p -Access {access} -PassThru | Out-Null";
        string script = $@"
$p={p}
$img=Get-DiskImage -ImagePath $p
$owned=$false
if(-not $img.Attached){{ {mountCmd}; $owned=$true }}
$disk=$null
for($i=0;$i -lt 40;$i++){{ $img=Get-DiskImage -ImagePath $p; if($null -ne $img.Number){{$disk=[int]$img.Number;break}}; Start-Sleep -Milliseconds 250 }}
if($null -eq $disk){{ throw 'Numero di disco non disponibile dopo il montaggio / disk number not available after mount' }}
$vols=@()
if({(isIso ? "$true" : "$false")}){{
  for($i=0;$i -lt 40;$i++){{ $v=Get-Volume -DiskImage $img -ErrorAction SilentlyContinue; if($v -and $v.DriveLetter){{break}}; Start-Sleep -Milliseconds 250 }}
  if($v){{ $vols+=[pscustomobject]@{{PartitionNumber=1;DriveLetter=[string]$v.DriveLetter;VolumePath=[string]$v.Path;Label=[string]$v.FileSystemLabel;FileSystem=[string]$v.FileSystem;Size=[int64]$v.Size;Free=[int64]$v.SizeRemaining;PartitionType='ISO'}} }}
}} else {{
  # sicurezza: lavoro SOLO sul disco virtuale appena montato, mai su dischi fisici
  $dk=Get-Disk -Number $disk
  if([string]$dk.BusType -ne 'File Backed Virtual'){{ throw ('Disco ' + $disk + ' inatteso (BusType ' + $dk.BusType + ')') }}
  if($owned -and $dk.IsOffline){{ try{{ Set-Disk -Number $disk -IsOffline $false -ErrorAction Stop; Start-Sleep -Milliseconds 500 }}catch{{}} }}
  Start-Sleep -Milliseconds 700
  $parts=@(Get-Partition -DiskNumber $disk -ErrorAction SilentlyContinue)
  foreach($pt in $parts){{
    $v=$null; try{{ $v=$pt | Get-Volume -ErrorAction Stop }}catch{{}}
    $hasLetter=([int][char]$pt.DriveLetter) -ne 0
    if({(assignLetters ? "$true" : "$false")} -and $v -and $v.FileSystem -and -not $hasLetter -and $pt.Type -notin @('System','Reserved','Recovery')){{
      try{{ $pt | Add-PartitionAccessPath -AssignDriveLetter -ErrorAction Stop; Start-Sleep -Milliseconds 400; $pt=Get-Partition -DiskNumber $disk -PartitionNumber $pt.PartitionNumber; $v=$pt|Get-Volume }}catch{{}}
      $hasLetter=([int][char]$pt.DriveLetter) -ne 0
    }}
    $letter=''; if($hasLetter){{$letter=[string]$pt.DriveLetter}}
    $vp=''; $ap=@($pt.AccessPaths | Where-Object {{ $_ -like '\\?\Volume*' }}); if($ap.Count -gt 0){{$vp=$ap[0]}}
    $o=[ordered]@{{PartitionNumber=[int]$pt.PartitionNumber;DriveLetter=$letter;VolumePath=$vp;Label='';FileSystem='';Size=[int64]$pt.Size;Free=[int64]0;PartitionType=[string]$pt.Type}}
    if($v){{ $o.Label=[string]$v.FileSystemLabel; $o.FileSystem=[string]$v.FileSystem; $o.Size=[int64]$v.Size; $o.Free=[int64]$v.SizeRemaining }}
    $vols+=[pscustomobject]$o
  }}
}}
[Console]::Out.Write((@{{DiskNumber=[int]$disk;Owned=$owned;Volumes=@($vols)}} | ConvertTo-Json -Depth 4 -Compress))
";
        Logger.Info($"Montaggio {(readOnly ? "sola lettura" : "lettura/scrittura")}: {imagePath}");
        string json = await RunPs(script, ct).ConfigureAwait(false);
        var res = new MountResult { ImagePath = imagePath };
        using (var doc = JsonDocument.Parse(json))
        {
            var root = doc.RootElement;
            res.DiskNumber = root.GetProperty("DiskNumber").GetInt32();
            res.OwnedByUs = root.GetProperty("Owned").GetBoolean();
            if (root.TryGetProperty("Volumes", out var vols))
            {
                IEnumerable<JsonElement> items = vols.ValueKind == JsonValueKind.Array ? vols.EnumerateArray() : new[] { vols };
                foreach (var v in items)
                {
                    res.Volumes.Add(new VolumeInfo
                    {
                        PartitionNumber = v.GetProperty("PartitionNumber").GetInt32(),
                        DriveLetter = v.GetProperty("DriveLetter").GetString() ?? "",
                        VolumePath = v.GetProperty("VolumePath").GetString() ?? "",
                        Label = v.GetProperty("Label").GetString() ?? "",
                        FileSystem = v.GetProperty("FileSystem").GetString() ?? "",
                        Size = v.GetProperty("Size").GetInt64(),
                        Free = v.GetProperty("Free").GetInt64(),
                        PartitionType = v.GetProperty("PartitionType").GetString() ?? ""
                    });
                }
            }
        }
        if (res.OwnedByUs) lock (Sync) Owned.Add(imagePath);
        foreach (var v in res.Volumes) ContentDetector.Detect(v);
        Logger.Info($"Disco {res.DiskNumber}: {res.Volumes.Count} partizioni; " +
                    string.Join(", ", res.Volumes.Select(v => $"#{v.PartitionNumber} {v.DriveLetter}: {v.FileSystem} {Format.Bytes(v.Size)} [{v.Kind}]")));
        return res;
    }

    public static async Task DismountAsync(string imagePath, CancellationToken ct)
    {
        string script = $"Dismount-DiskImage -ImagePath {PsLiteral(Path.GetFullPath(imagePath))} | Out-Null; [Console]::Out.Write('{{\"ok\":true}}')";
        Logger.Info("Smontaggio: " + imagePath);
        await RunPs(script, ct).ConfigureAwait(false);
        lock (Sync) Owned.Remove(imagePath);
    }

    /// <summary>Smonta tutte le immagini montate dall'app (uscita, crash). Best effort, sincrono.</summary>
    public static void DismountAllOwned()
    {
        foreach (var img in OwnedImages)
        {
            try { DismountAsync(img, CancellationToken.None).Wait(TimeSpan.FromSeconds(60)); }
            catch (Exception ex) { Logger.Warn($"Smontaggio non riuscito di {img}: {ex.GetBaseException().Message}"); }
        }
    }
}

/// <summary>Riconosce il contenuto di un volume: supporto di installazione, WinPE, Windows installato.</summary>
public static class ContentDetector
{
    public static void Detect(VolumeInfo v)
    {
        string r = v.Root;
        if (r.Length == 0 || v.FileSystem.Length == 0) return;
        try
        {
            bool F(string rel) => File.Exists(Path.Combine(r, rel));
            v.IsWindowsOs = Directory.Exists(Path.Combine(r, @"Windows\System32\config"));
            bool hasInstall = F(@"sources\install.wim") || F(@"sources\install.esd") || F(@"sources\install.swm");
            bool hasBootWim = F(@"sources\boot.wim");
            v.IsSetupMedia = hasInstall;
            v.IsWinPe = !hasInstall && hasBootWim;
            string etfs = Path.Combine(r, @"boot\etfsboot.com");
            string efi = Path.Combine(r, @"efi\microsoft\boot\efisys.bin");
            string efiNp = Path.Combine(r, @"efi\microsoft\boot\efisys_noprompt.bin");
            v.BiosBootFile = File.Exists(etfs) ? etfs : "";
            v.UefiBootFile = File.Exists(efi) ? efi : File.Exists(efiNp) ? efiNp : "";
        }
        catch (Exception ex)
        {
            Logger.Warn($"Analisi volume {r} non riuscita: {ex.Message}");
        }
    }
}

public static class Format
{
    public static string Bytes(long b)
    {
        string[] u = { "B", "KB", "MB", "GB", "TB" };
        double v = b; int i = 0;
        while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
        return $"{v:0.##} {u[i]}";
    }

    public static string Duration(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes:00}m" : t.TotalMinutes >= 1 ? $"{t.Minutes}m {t.Seconds:00}s" : $"{t.Seconds}s";

    /// <summary>Nome file valido per Windows (caratteri vietati, nomi riservati, punti/spazi finali).</summary>
    public static string SafeFileName(string name, string fallback = "image")
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(new[] { '<', '>', ':', '"', '/', '\\', '|', '?', '*' }).ToHashSet();
        var sb = new StringBuilder();
        foreach (char c in name) sb.Append(invalid.Contains(c) || char.IsControl(c) ? '_' : c);
        string s = sb.ToString().Trim().TrimEnd('.', ' ');
        string upper = s.ToUpperInvariant();
        string[] reserved = { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
                              "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };
        if (s.Length == 0) s = fallback;
        if (reserved.Contains(upper)) s = "_" + s;
        return s.Length > 120 ? s[..120] : s;
    }
}
