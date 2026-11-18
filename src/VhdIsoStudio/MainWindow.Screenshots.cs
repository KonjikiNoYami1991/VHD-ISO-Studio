using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using VhdIsoStudio.Core;

namespace VhdIsoStudio;

/// <summary>
/// Modalita' "--screenshots &lt;cartella&gt;": riempie l'interfaccia con DATI DI ESEMPIO e salva in PNG
/// le schede principali nei vari temi/lingue, poi chiude l'app. Usata dalla CI per la documentazione:
/// le immagini sono l'interfaccia reale renderizzata da WPF, non dei mockup.
/// Non monta dischi, non esegue comandi e non salva le impostazioni.
/// </summary>
public partial class MainWindow
{
    private const double ShotWidth = 1180, ShotHeight = 780, ShotScale = 1.5;

    private async Task CaptureScreenshotsAsync(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            S.ReduceMotion = true;            // niente dissolvenze a meta' nelle immagini
            _logTimer.Stop();                  // il log mostrato e' quello di esempio
            Logger.Info("Generazione screenshot in " + dir);

            var shots = new (string Theme, string Lang, TabItem Tab, string Name, bool Busy)[]
            {
                ("Dark",   "it", TabAuto,    "auto-dark-it",     false),
                ("Dark",   "it", TabManual,  "manual-dark-it",   true),
                ("Dark",   "it", TabOptions, "options-dark-it",  false),
                ("Light",  "en", TabAuto,    "auto-light-en",    false),
                ("Light",  "en", TabManual,  "manual-light-en",  false),
                ("Light",  "en", TabOptions, "options-light-en", false),
                ("Medium", "it", TabAuto,    "auto-medium-it",   false),
                ("Medium", "en", TabManual,  "manual-medium-en", false),
                ("Dark",   "en", TabLog,     "log-dark-en",      false),
            };

            foreach (var s in shots)
            {
                ThemeManager.Apply(s.Theme);
                Loc.Instance.SetLanguage(s.Lang);
                FillDemoData();
                Tabs.SelectedItem = s.Tab;
                if (s.Busy) ShowDemoProgress(); else ClearDemoProgress();
                await SettleAsync();
                SaveShot(Path.Combine(dir, s.Name + ".png"));
            }
            Logger.Success("Screenshot completati.");
        }
        catch (Exception ex)
        {
            Logger.Error("Screenshot non riusciti", ex);
            Application.Current.Shutdown(1);
            return;
        }
        Application.Current.Shutdown(0);
    }

    private async Task SettleAsync()
    {
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        await Task.Delay(350);
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    }

    /// <summary>Renderizza il contenuto della finestra a dimensione fissa (indipendente dallo schermo del runner).</summary>
    private void SaveShot(string path)
    {
        if (Content is not FrameworkElement root) return;
        var size = new Size(ShotWidth, ShotHeight);
        root.Measure(size);
        root.Arrange(new Rect(size));

        int w = (int)(ShotWidth * ShotScale), h = (int)(ShotHeight * ShotScale);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            // la scala la applica gia' RenderTargetBitmap tramite i DPI (96 * ShotScale)
            dc.DrawRectangle(Background, null, new Rect(size));
            dc.DrawRectangle(new VisualBrush(root) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top },
                             null, new Rect(size));
        }
        var rtb = new RenderTargetBitmap(w, h, 96 * ShotScale, 96 * ShotScale, PixelFormats.Pbgra32);
        rtb.Render(dv);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using (var fs = File.Create(path)) enc.Save(fs);
        Logger.Info("Screenshot: " + path);
        root.InvalidateMeasure();
    }

    // ------------------------------------------------------------------ dati di esempio
    private void FillDemoData()
    {
        const string vhd = @"D:\VM\Win11-Golden.vhdx";
        _autoVhd = vhd;
        TxtVhd.Text = vhd;
        AutoVhdTitle.Text = "Win11-Golden.vhdx";
        AutoVhdSub.Text = "38.2 GB — D:\\VM";
        DropIcon.Text = "\uE73E";
        TxtAutoOut.Text = @"D:\ISO";
        TxtAutoBase.Text = @"D:\ISO\Win11_24H2_x64.iso";
        TxtManualBase.Text = TxtAutoBase.Text;
        ChkAutoHash.IsChecked = true;
        ChkAutoOpen.IsChecked = true;

        long gb = 1024L * 1024 * 1024, mb = 1024L * 1024;
        _mounted = new MountResult
        {
            ImagePath = vhd, DiskNumber = 3, OwnedByUs = true,
            Volumes =
            {
                new VolumeInfo { PartitionNumber = 1, Label = "SYSTEM", FileSystem = "FAT32", Size = 100 * mb, Free = 68 * mb, PartitionType = "System" },
                new VolumeInfo { PartitionNumber = 2, Size = 16 * mb, PartitionType = "Reserved" },
                new VolumeInfo { PartitionNumber = 3, DriveLetter = "E", Label = "Windows", FileSystem = "NTFS", Size = 79 * gb, Free = 41 * gb, PartitionType = "Basic", IsWindowsOs = true },
                new VolumeInfo { PartitionNumber = 4, Label = "Recovery", FileSystem = "NTFS", Size = 750 * mb, Free = 120 * mb, PartitionType = "Recovery" },
            }
        };
        RefreshVolumesView();
        LstVolumes.SelectedItem = _mounted.Volumes[2];
        BtnDismount.IsEnabled = true;

        RbSrcVhd.IsChecked = true;
        RbCapDism.IsChecked = true;
        TxtStaging.Text = @"D:\ISO\staging_Win11-Golden";
        TxtWimName.Text = "Windows 11 Pro - Golden";
        TxtWimDesc.Text = "Sysprep 2026-11";
        ChkInject.IsChecked = true;
        TxtIsoOut.Text = @"D:\ISO\Win11-Golden.iso";

        var o = OscdimgOptions.WindowsBootPreset();
        o.BiosBootFile = @"D:\Tools\VhdIsoStudio\tools\oscdimg\etfsboot.com";
        o.UefiBootFile = @"D:\Tools\VhdIsoStudio\tools\oscdimg\efisys.bin";
        o.Label = "WIN11_GOLDEN";
        _options = o;
        _oscdimgPath = @"D:\Tools\VhdIsoStudio\tools\oscdimg\oscdimg.exe";
        RebindOptions();
        OscdimgStatusText.Text = Loc.T("OscdimgOk");
        BtnOscdimgStatus.Tag = "\uE73E";
        OscdimgInfo.Text = Loc.F("OscdimgInfo", _oscdimgPath, "2.56") + "  —  " + Loc.T("OscdimgOriginPortable");
        OptsValidation.Text = "";
        ManualValidation.Text = "";

        ShowResult(new JobResult
        {
            IsoPath = @"D:\ISO\Win11-Golden.iso",
            IsoSize = 6_412_345_344,
            Sha256 = "9f2c4e7a1b03d58e6a0c2f71d4b8e93a5c6d7e8f9012ab34cd56ef7890a1b2c3",
            Summary = Loc.T("AutoSummary_OsMedia"),
            Elapsed = TimeSpan.FromMinutes(17.4)
        });
        // finto: nessun file reale -> pulsanti attivi solo per l'aspetto
        BtnResultOpen.IsEnabled = true;

        string it = Loc.Instance.Language;
        TxtLog.Text = string.Join(Environment.NewLine, new[]
        {
            "09:41:02 • ==== VHD ISO Studio " + AppInfo.Version + " — Microsoft Windows NT 10.0.26100.0 — X64",
            "09:41:02 • " + (it == "it" ? "Modalita' portabile: si' — dati: D:\\Tools\\VhdIsoStudio\\Data" : "Portable mode: yes — data: D:\\Tools\\VhdIsoStudio\\Data"),
            "09:41:03 • oscdimg: D:\\Tools\\VhdIsoStudio\\tools\\oscdimg\\oscdimg.exe (v2.56)",
            "09:41:20 • » " + Loc.T("StageMount"),
            "09:41:23 • " + (it == "it" ? "Disco 3: 4 partizioni; #1" : "Disk 3: 4 partitions; #1") + " : FAT32 100 MB [System], #3 E: NTFS 79 GB [" + Loc.T("KindWindows") + "]",
            "09:41:24 • » " + Loc.T("StageDism"),
            "09:41:24 • > C:\\Windows\\system32\\dism.exe /English /Capture-Image /ImageFile:\"D:\\ISO\\staging_Win11-Golden\\sources\\install.wim\" /CaptureDir:E:\\ /Name:\"Windows 11 Pro - Golden\" /Compress:max /CheckIntegrity /Verify",
            "09:55:51 • < dism.exe exit 0",
            "09:55:51 ✔ WIM: D:\\ISO\\staging_Win11-Golden\\sources\\install.wim (5.62 GB)",
            "09:55:52 • » " + Loc.T("StageOscdimg"),
            "09:55:52 • > oscdimg.exe -u2 -udfver102 -bootdata:2#p0,e,b\"...\\etfsboot.com\"#pEF,e,b\"...\\efisys.bin\" -o -lWIN11_GOLDEN -m \"D:\\ISO\\staging_Win11-Golden\" \"D:\\ISO\\Win11-Golden.iso\"",
            "09:58:11 • < oscdimg.exe exit 0",
            "09:58:11 ✔ ISO: D:\\ISO\\Win11-Golden.iso (5.97 GB)",
            "09:58:40 • SHA-256: 9f2c4e7a1b03d58e6a0c2f71d4b8e93a5c6d7e8f9012ab34cd56ef7890a1b2c3",
            "09:58:41 • » " + Loc.T("StageDismount"),
        });
        RefreshPreview();
        OptsValidation.Text = "";
        ManualValidation.Text = "";
    }

    private void ShowDemoProgress()
    {
        StatusText.Text = Loc.T("StageOscdimg");
        SetStatusIcon(StatusKind.Busy);
        Progress.IsIndeterminate = false;
        Progress.Value = 63;
        StatusPercent.Text = "63%";
        StatusEta.Text = Loc.F("Eta", "1m 24s");
        StatusDetail.Text = "63% complete";
        BtnCancel.IsEnabled = true;
    }

    private void ClearDemoProgress()
    {
        StatusText.Text = Loc.T("StatusReady");
        SetStatusIcon(StatusKind.Ok);
        Progress.Value = 0;
        StatusPercent.Text = "";
        StatusEta.Text = "";
        StatusDetail.Text = "";
        BtnCancel.IsEnabled = false;
    }
}
