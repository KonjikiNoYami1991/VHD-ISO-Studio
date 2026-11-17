using VhdIsoStudio.Core;
using Xunit;

public class OscdimgOptionsTests
{
    [Fact]
    public void WindowsBootPreset_MatchesMicrosoftSyntax()
    {
        var o = OscdimgOptions.WindowsBootPreset();
        o.BiosBootFile = @"C:\media\boot\etfsboot.com";
        o.UefiBootFile = @"C:\media\efi\microsoft\boot\efisys.bin";
        o.Label = "WIN11";
        string a = o.BuildArguments(@"C:\media", @"D:\out\win.iso");
        Assert.Equal(
            "-u2 -udfver102 -bootdata:2#p0,e,b\"C:\\media\\boot\\etfsboot.com\"#pEF,e,b\"C:\\media\\efi\\microsoft\\boot\\efisys.bin\" -o -lWIN11 -m \"C:\\media\" \"D:\\out\\win.iso\"",
            a);
    }

    [Theory]
    [InlineData(@"E:\", @"E:\")]
    [InlineData(@"E:", @"E:\")]
    [InlineData(@"C:\dir with space\", "\"C:\\dir with space\"")]
    [InlineData(@"C:\a\b.iso", "\"C:\\a\\b.iso\"")]
    public void Q_AvoidsTrailingBackslashQuoteTrap(string input, string expected) =>
        Assert.Equal(expected, OscdimgOptions.Q(input));

    [Fact]
    public void UefiOnly_And_BiosOnly()
    {
        var o = new OscdimgOptions { Boot = BootMode.UefiOnly, UefiBootFile = @"C:\e.bin", Optimize = OptimizeMode.None, SizeLimit = SizeLimitMode.Default };
        Assert.Contains("-bootdata:1#pEF,e,b\"C:\\e.bin\"", o.BuildArguments(@"E:\", @"C:\x.iso"));
        o.Boot = BootMode.BiosOnly; o.BiosBootFile = @"C:\b.com"; o.LoadSegment = "0x7c0";
        Assert.Contains("-bootdata:1#p0,e,t7C0,b\"C:\\b.com\"", o.BuildArguments(@"E:\", @"C:\x.iso"));
    }

    [Fact]
    public void SingleCustom_UsesBPE()
    {
        var o = new OscdimgOptions { Boot = BootMode.SingleCustom, BiosBootFile = @"C:\cdboot.bin", PlatformId = "EF", BootNoEmulation = true };
        string a = o.BuildArguments(@"E:\", @"C:\x.iso");
        Assert.Contains("-b\"C:\\cdboot.bin\" -pEF -e", a);
    }

    [Fact]
    public void AllGeneralSwitches_Emitted()
    {
        var o = new OscdimgOptions
        {
            FileSystem = IsoFileSystem.UdfOnly, UdfVersion = UdfRevision.V200, UdfSparse = true, UdfEmbedFiles = true,
            UdfEmbedFileIds = true, UdfLongAllocDescriptors = true, Optimize = OptimizeMode.Binary, IgnoreDiamondTimestamps = true,
            ShowDuplicates = true, I386First = true, WarnNonCompliantNames = true, WarnNonDosNames = true, WarnZeroLength = true,
            ReportEachFile = true, SuppressSame64kWarning = true, AllocationSummary = true, UseTimestamp = true,
            Timestamp = "12/31/2026,15:01:00", Gmt = true, IncludeHidden = true, AnsiNames = true, AlignDirRecords = true,
            WriteSharing = true, IgnoreOpenFailures = true, ScanOnly = true, SizeLimit = SizeLimitMode.MaxSize, MaxSizeMb = 4096,
            ResolveSymlinks = true, Label = "MY LABEL"
        };
        string a = o.BuildArguments(@"E:\", @"C:\x.iso");
        foreach (var sw in new[] { "-u2", "-udfver200", "-yl", "-us", "-ue", "-uf", "-oc", "-oi", "-os", "-y5", "-w1", "-w2", "-w3", "-w4",
                                   "-yd", "-a", "-t12/31/2026,15:01:00", "-g", "-h", "-c", "-y6", "-yw", "-k", "-q", "-maxsize:4096", "-r", "-l\"MY LABEL\"" })
            Assert.True(a.Contains(" " + sw + " ") || a.StartsWith(sw + " "), $"manca {sw} in: {a}");
        Assert.DoesNotContain(" -m ", a);
    }

    [Fact]
    public void Validate_DetectsConflicts()
    {
        var o = new OscdimgOptions { FileSystem = IsoFileSystem.Iso9660Nt, IsoLowercase = true, UdfSparse = true, DvdTruncateIso = true,
                                     JolietReadmeFile = "x", UseTimestamp = true, Timestamp = "bad", Label = new string('A', 33) };
        var e = o.Validate("", "out.img", checkFiles: false);
        foreach (var k in new[] { "ValSourceMissing", "ValTargetExt", "ValLabelLong", "ValNtLower", "ValU2Only", "ValJ2Only", "ValUtOnly", "ValTimestamp" })
            Assert.Contains(k, e);
    }

    [Theory]
    [InlineData("MyDisk v2.1 (test)", "MYDISK_V2_1_TEST")]
    [InlineData("àèì", "")]
    [InlineData("a_very_long_label_that_exceeds_thirty_two_chars", "A_VERY_LONG_LABEL_THAT_EXCEEDS_T")]
    public void SanitizeLabel(string input, string expected) => Assert.Equal(expected, OscdimgOptions.SanitizeLabel(input));

    [Theory]
    [InlineData("45% complete", 45.0)]
    [InlineData("[==========                 18.0%                          ]", 18.0)]
    [InlineData("100% complete", 100.0)]
    [InlineData("Scanning source tree", null)]
    public void ParsePercent(string line, double? expected) => Assert.Equal(expected, ProcessRunner.ParsePercent(line));
}
