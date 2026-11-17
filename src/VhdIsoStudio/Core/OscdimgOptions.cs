using System.Globalization;
using System.Text;

namespace VhdIsoStudio.Core;

public enum IsoFileSystem { UdfOnly, UdfIso, JolietIso, JolietOnly, Iso9660, Iso9660Long, Iso9660Nt, DvdVideo }
public enum UdfRevision { Default, V102, V150, V200 }
public enum BootMode { None, BiosAndUefi, UefiOnly, BiosOnly, SingleCustom }
public enum OptimizeMode { None, Md5, Binary }
public enum SizeLimitMode { Default, NoLimit, MaxSize }

/// <summary>
/// Tutti i parametri di oscdimg.exe (riferimento: "oscdimg -help" v2.56, categorie
/// ISO, Joliet, UDF, Boot, Optimize, Order, DVD, Mesg, Other) e costruzione della riga di comando.
/// </summary>
public sealed class OscdimgOptions
{
    // --- File system ---
    public IsoFileSystem FileSystem { get; set; } = IsoFileSystem.UdfOnly;
    public UdfRevision UdfVersion { get; set; } = UdfRevision.V102;   // -udfver102/150/200
    public bool UdfSparse { get; set; }                                // -us (solo -u2)
    public bool UdfEmbedFiles { get; set; }                            // -ue (solo -u2)
    public bool UdfEmbedFileIds { get; set; }                          // -uf (solo -u2)
    public bool UdfLongAllocDescriptors { get; set; }                  // -yl
    public string UdfReadmeFile { get; set; } = "";                    // -ur<file> (solo -u2)
    public string JolietReadmeFile { get; set; } = "";                 // -js<file> (solo -j2)
    public bool IsoLowercase { get; set; }                             // -d (ISO 9660, non con -nt)
    public bool DvdTruncateIso { get; set; }                           // -ut (solo -uv)

    // --- Boot ---
    public BootMode Boot { get; set; } = BootMode.None;
    public string BiosBootFile { get; set; } = "";                     // etfsboot.com
    public string UefiBootFile { get; set; } = "";                     // efisys.bin / efisys_noprompt.bin
    public bool BootNoEmulation { get; set; } = true;                  // -e / ",e"
    public string PlatformId { get; set; } = "0";                      // -p (singola voce) es. 0, EF
    public string LoadSegment { get; set; } = "";                      // ",t" (multi) es. 7C0

    // --- Ottimizzazione ---
    public OptimizeMode Optimize { get; set; } = OptimizeMode.Md5;     // -o / -oc
    public bool IgnoreDiamondTimestamps { get; set; }                  // -oi
    public bool ShowDuplicates { get; set; }                           // -os

    // --- Ordine ---
    public bool I386First { get; set; }                                // -y5
    public string OrderFile { get; set; } = "";                        // -yo<file>

    // --- Messaggi ---
    public bool WarnNonCompliantNames { get; set; }                    // -w1
    public bool WarnNonDosNames { get; set; }                          // -w2
    public bool WarnZeroLength { get; set; }                           // -w3
    public bool ReportEachFile { get; set; }                           // -w4
    public bool SuppressSame64kWarning { get; set; }                   // -yd
    public bool AllocationSummary { get; set; }                        // -a

    // --- Generali ---
    public string Label { get; set; } = "";                            // -l<label> (<=32)
    public bool UseTimestamp { get; set; }                             // -t
    public string Timestamp { get; set; } = "";                        // mm/dd/yyyy,hh:mm:ss
    public bool Gmt { get; set; }                                      // -g
    public bool IncludeHidden { get; set; }                            // -h
    public bool AnsiNames { get; set; }                                // -c
    public bool AlignDirRecords { get; set; }                          // -y6
    public bool WriteSharing { get; set; }                             // -yw
    public bool IgnoreOpenFailures { get; set; }                       // -k
    public bool ScanOnly { get; set; }                                 // -q
    public SizeLimitMode SizeLimit { get; set; } = SizeLimitMode.NoLimit; // -m / -maxsize:N
    public int MaxSizeMb { get; set; } = 4096;
    public bool ResolveSymlinks { get; set; }                          // -r
    public string ExtraArgs { get; set; } = "";

    public OscdimgOptions Clone() => (OscdimgOptions)MemberwiseClone();

    // ---------------------------------------------------------------- preset
    /// <summary>Preset Microsoft per supporti Windows avviabili BIOS+UEFI.</summary>
    public static OscdimgOptions WindowsBootPreset() => new()
    {
        FileSystem = IsoFileSystem.UdfOnly,
        UdfVersion = UdfRevision.V102,
        Optimize = OptimizeMode.Md5,
        SizeLimit = SizeLimitMode.NoLimit,
        Boot = BootMode.BiosAndUefi,
        BootNoEmulation = true
    };

    /// <summary>ISO dati moderna (UDF, nomi Unicode lunghi, file &gt; 4 GB).</summary>
    public static OscdimgOptions DataPreset() => new()
    {
        FileSystem = IsoFileSystem.UdfOnly,
        UdfVersion = UdfRevision.V102,
        Optimize = OptimizeMode.Md5,
        SizeLimit = SizeLimitMode.NoLimit,
        Boot = BootMode.None
    };

    /// <summary>Massima compatibilita' (UDF + ISO 9660).</summary>
    public static OscdimgOptions CompatPreset() => new()
    {
        FileSystem = IsoFileSystem.UdfIso,
        UdfVersion = UdfRevision.V102,
        Optimize = OptimizeMode.Md5,
        SizeLimit = SizeLimitMode.NoLimit,
        Boot = BootMode.None
    };

    // ---------------------------------------------------------------- helper
    /// <summary>Quota un percorso per la riga di comando evitando la trappola "C:\" + virgolette.</summary>
    public static string Q(string path)
    {
        if (string.IsNullOrEmpty(path)) return "\"\"";
        string p = path;
        bool isRoot = p.Length <= 3 && p.Length >= 2 && p[1] == ':';
        if (isRoot) return p.Length == 2 ? p + "\\" : p;         // E:\ senza virgolette
        p = p.TrimEnd('\\');                                       // "C:\dir\" romperebbe il parsing
        return "\"" + p + "\"";
    }

    public static string SanitizeLabel(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var sb = new StringBuilder();
        foreach (char c in raw.Trim())
        {
            if (char.IsLetterOrDigit(c) && c < 128) sb.Append(char.ToUpperInvariant(c));
            else if (c is '_' or '-' or ' ' or '.') sb.Append('_');
        }
        string s = sb.ToString().Trim('_');
        while (s.Contains("__")) s = s.Replace("__", "_");
        return s.Length > 32 ? s[..32] : s;
    }

    private bool IsUdf => FileSystem is IsoFileSystem.UdfOnly or IsoFileSystem.UdfIso;
    private bool IsIso => FileSystem is IsoFileSystem.Iso9660 or IsoFileSystem.Iso9660Long or IsoFileSystem.Iso9660Nt;

    /// <summary>Restituisce le chiavi di localizzazione degli errori di validazione (vuota = ok).</summary>
    public List<string> Validate(string source, string target, bool checkFiles = true)
    {
        var e = new List<string>();
        if (string.IsNullOrWhiteSpace(source)) e.Add("ValSourceMissing");
        else if (checkFiles && !Directory.Exists(source)) e.Add("ValSourceNotFound");
        if (string.IsNullOrWhiteSpace(target) && !ScanOnly) e.Add("ValTargetMissing");
        else if (!ScanOnly && !target.EndsWith(".iso", StringComparison.OrdinalIgnoreCase)) e.Add("ValTargetExt");

        if (Label.Length > 32) e.Add("ValLabelLong");
        if (Label.IndexOfAny(new[] { '"', '\\', '/', ':', '*', '?', '<', '>', '|' }) >= 0) e.Add("ValLabelChars");
        if (FileSystem == IsoFileSystem.Iso9660Nt && IsoLowercase) e.Add("ValNtLower");
        if ((UdfSparse || UdfEmbedFiles || UdfEmbedFileIds || UdfReadmeFile.Length > 0) && FileSystem != IsoFileSystem.UdfOnly)
            e.Add("ValU2Only");
        if (JolietReadmeFile.Length > 0 && FileSystem != IsoFileSystem.JolietOnly) e.Add("ValJ2Only");
        if (DvdTruncateIso && FileSystem != IsoFileSystem.DvdVideo) e.Add("ValUtOnly");
        if (UseTimestamp && !TryParseTimestamp(Timestamp, out _)) e.Add("ValTimestamp");
        if (SizeLimit == SizeLimitMode.MaxSize && MaxSizeMb <= 0) e.Add("ValMaxSize");

        if (checkFiles)
        {
            foreach (var f in new[] { UdfReadmeFile, JolietReadmeFile, OrderFile })
                if (f.Length > 0 && !File.Exists(f)) { e.Add("ValFileNotFound"); break; }
        }

        switch (Boot)
        {
            case BootMode.BiosAndUefi:
                if (BiosBootFile.Length == 0 || UefiBootFile.Length == 0) e.Add("ValBootFiles");
                break;
            case BootMode.UefiOnly:
                if (UefiBootFile.Length == 0) e.Add("ValBootFiles");
                break;
            case BootMode.BiosOnly:
            case BootMode.SingleCustom:
                if (BiosBootFile.Length == 0) e.Add("ValBootFiles");
                break;
        }
        if (checkFiles && Boot != BootMode.None)
        {
            if (BiosBootFile.Length > 0 && Boot != BootMode.UefiOnly && !File.Exists(BiosBootFile)) e.Add("ValBootNotFound");
            else if (UefiBootFile.Length > 0 && Boot is BootMode.BiosAndUefi or BootMode.UefiOnly && !File.Exists(UefiBootFile)) e.Add("ValBootNotFound");
        }
        if (Boot == BootMode.SingleCustom && !IsHex(PlatformId)) e.Add("ValPlatformId");
        if (LoadSegment.Length > 0 && !IsHex(LoadSegment)) e.Add("ValLoadSegment");
        return e.Distinct().ToList();
    }

    private static bool IsHex(string s)
    {
        string v = s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? s[2..] : s;
        return v.Length is > 0 and <= 4 && v.All(Uri.IsHexDigit);
    }

    private static string Hex(string s) =>
        (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? s[2..] : s).ToUpperInvariant();

    public static bool TryParseTimestamp(string s, out DateTime dt) =>
        DateTime.TryParseExact(s, "MM/dd/yyyy,HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out dt);

    /// <summary>Costruisce la riga di argomenti completa per oscdimg.exe.</summary>
    public string BuildArguments(string source, string target)
    {
        var a = new List<string>();

        // File system
        switch (FileSystem)
        {
            case IsoFileSystem.UdfOnly: a.Add("-u2"); break;
            case IsoFileSystem.UdfIso: a.Add("-u1"); break;
            case IsoFileSystem.JolietIso: a.Add("-j1"); break;
            case IsoFileSystem.JolietOnly: a.Add("-j2"); break;
            case IsoFileSystem.Iso9660Long: a.Add("-n"); break;
            case IsoFileSystem.Iso9660Nt: a.Add("-nt"); break;
            case IsoFileSystem.DvdVideo: a.Add("-uv"); break;
        }
        if (IsUdf)
        {
            switch (UdfVersion)
            {
                case UdfRevision.V102: a.Add("-udfver102"); break;
                case UdfRevision.V150: a.Add("-udfver150"); break;
                case UdfRevision.V200: a.Add("-udfver200"); break;
            }
            if (UdfLongAllocDescriptors) a.Add("-yl");
        }
        if (FileSystem == IsoFileSystem.UdfOnly)
        {
            if (UdfSparse) a.Add("-us");
            if (UdfEmbedFiles) a.Add("-ue");
            if (UdfEmbedFileIds) a.Add("-uf");
            if (UdfReadmeFile.Length > 0) a.Add("-ur" + Q(UdfReadmeFile));
        }
        if (FileSystem == IsoFileSystem.JolietOnly && JolietReadmeFile.Length > 0) a.Add("-js" + Q(JolietReadmeFile));
        if (IsIso && IsoLowercase && FileSystem != IsoFileSystem.Iso9660Nt) a.Add("-d");
        if (FileSystem == IsoFileSystem.DvdVideo && DvdTruncateIso) a.Add("-ut");

        // Boot
        string e = BootNoEmulation ? ",e" : "";
        string t = LoadSegment.Length > 0 ? ",t" + Hex(LoadSegment) : "";
        switch (Boot)
        {
            case BootMode.BiosAndUefi:
                a.Add($"-bootdata:2#p0{e}{t},b{Q(BiosBootFile)}#pEF{e},b{Q(UefiBootFile)}");
                break;
            case BootMode.UefiOnly:
                a.Add($"-bootdata:1#pEF{e},b{Q(UefiBootFile)}");
                break;
            case BootMode.BiosOnly:
                a.Add($"-bootdata:1#p0{e}{t},b{Q(BiosBootFile)}");
                break;
            case BootMode.SingleCustom:
                a.Add("-b" + Q(BiosBootFile));
                if (PlatformId.Length > 0 && Hex(PlatformId) != "0" && Hex(PlatformId) != "00") a.Add("-p" + Hex(PlatformId));
                if (BootNoEmulation) a.Add("-e");
                break;
        }

        // Ottimizzazione
        if (Optimize == OptimizeMode.Md5) a.Add("-o");
        else if (Optimize == OptimizeMode.Binary) a.Add("-oc");
        if (IgnoreDiamondTimestamps) a.Add("-oi");
        if (ShowDuplicates) a.Add("-os");

        // Ordine
        if (I386First) a.Add("-y5");
        if (OrderFile.Length > 0) a.Add("-yo" + Q(OrderFile));

        // Messaggi
        if (WarnNonCompliantNames) a.Add("-w1");
        if (WarnNonDosNames) a.Add("-w2");
        if (WarnZeroLength) a.Add("-w3");
        if (ReportEachFile) a.Add("-w4");
        if (SuppressSame64kWarning) a.Add("-yd");
        if (AllocationSummary) a.Add("-a");

        // Generali
        if (Label.Length > 0) a.Add("-l" + (Label.Contains(' ') ? "\"" + Label + "\"" : Label));
        if (UseTimestamp && TryParseTimestamp(Timestamp, out _)) a.Add("-t" + Timestamp);
        if (Gmt) a.Add("-g");
        if (IncludeHidden) a.Add("-h");
        if (AnsiNames) a.Add("-c");
        if (AlignDirRecords) a.Add("-y6");
        if (WriteSharing) a.Add("-yw");
        if (IgnoreOpenFailures) a.Add("-k");
        if (ScanOnly) a.Add("-q");
        if (SizeLimit == SizeLimitMode.NoLimit) a.Add("-m");
        else if (SizeLimit == SizeLimitMode.MaxSize) a.Add("-maxsize:" + MaxSizeMb.ToString(CultureInfo.InvariantCulture));
        if (ResolveSymlinks) a.Add("-r");
        if (!string.IsNullOrWhiteSpace(ExtraArgs)) a.Add(ExtraArgs.Trim());

        a.Add(Q(source));
        // con -q (solo scansione) oscdimg richiede comunque il file di destinazione nella sintassi
        a.Add(Q(string.IsNullOrWhiteSpace(target) ? Path.Combine(Path.GetTempPath(), "scan.iso") : target));
        return string.Join(" ", a);
    }
}
