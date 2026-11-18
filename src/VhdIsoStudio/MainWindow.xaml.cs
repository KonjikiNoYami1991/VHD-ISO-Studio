using System.IO;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Media;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shell;
using System.Windows.Threading;
using Microsoft.Win32;
using VhdIsoStudio.Core;
using CaptureMode = VhdIsoStudio.Core.CaptureMode;

namespace VhdIsoStudio;

public partial class MainWindow : Window
{
    private AppSettings S => App.Settings;
    private OscdimgOptions _options;
    private MountResult? _mounted;
    private CancellationTokenSource? _cts;
    private bool _busy;
    private bool _closeAfterCancel;
    private bool _lastJobCancelled;
    private bool _loading = true;
    private JobResult? _lastResult;
    private string? _oscdimgPath;

    private readonly ConcurrentQueue<(DateTime, LogLevel, string)> _logQueue = new();
    private readonly DispatcherTimer _logTimer;
    private readonly StringBuilder _logBuffer = new();
    private int _logLines;

    // avanzamento / ETA
    private string _stage = "";
    private DateTime _stageStart;
    private double _stageFirstPct = -1;
    private DateTime _lastUiUpdate = DateTime.MinValue;

    public MainWindow()
    {
        InitializeComponent();
        _options = S.Oscdimg ?? OscdimgOptions.WindowsBootPreset();
        OptionsRoot.DataContext = _options;

        Width = Math.Max(MinWidth, S.WindowWidth);
        Height = Math.Max(MinHeight, S.WindowHeight);
        if (S.WindowMaximized) WindowState = WindowState.Maximized;
        Title = $"{AppInfo.Name} {AppInfo.Version}";
        TaskbarItemInfo = new TaskbarItemInfo();

        // log nella UI (flush periodico, mai dal thread di lavoro)
        Logger.LineWritten += (t, lv, m) => _logQueue.Enqueue((t, lv, m));
        _logTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) => FlushLog(), Dispatcher);
        _logTimer.Start();

        // aggiornamento anteprima per qualsiasi modifica nelle opzioni
        OptionsRoot.AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler((_, _) => ScheduleRefresh()), true);
        OptionsRoot.AddHandler(ToggleButton.UncheckedEvent, new RoutedEventHandler((_, _) => ScheduleRefresh()), true);
        OptionsRoot.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => ScheduleRefresh()), true);
        OptionsRoot.AddHandler(Selector.SelectionChangedEvent, new SelectionChangedEventHandler((_, _) => ScheduleRefresh()), true);

        SourceInitialized += (_, _) => { ThemeManager.ApplyTitleBar(this); EnableElevatedFileDrop(); };
        Loaded += OnLoaded;
        Closing += OnClosing;
        PreviewKeyDown += OnPreviewKeyDown;
        Loc.LanguageChanged += () => { UpdateOscdimgStatus(); UpdateAdminBadge(); RefreshPreview(); RefreshVolumesView(); };
    }

    // =================================================================== avvio
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SelectComboByTag(CmbLanguage, S.Language);
        SelectComboByTag(CmbTheme, S.Theme);
        TxtAutoOut.Text = S.LastOutputFolder;
        TxtAutoBase.Text = S.BaseMediaPath;
        TxtManualBase.Text = S.BaseMediaPath;
        ChkAutoHash.IsChecked = S.AutoComputeHash;
        ChkAutoOpen.IsChecked = S.AutoOpenFolder;
        ChkManualHash.IsChecked = S.AutoComputeHash;
        ChkManualOpen.IsChecked = S.AutoOpenFolder;
        ChkReadOnly.IsChecked = S.MountReadOnly;
        TxtOscdimgPath.Text = S.OscdimgPath;
        _loading = false;

        UpdateAdminBadge();
        DetectOscdimg(log: true);
        RefreshPreview();
        RefreshVolumesView();

        // argomenti da riga di comando / "Apri con"
        var args = App.StartupArgs;
        int ai = Array.FindIndex(args, a => a.Equals("--auto", StringComparison.OrdinalIgnoreCase));
        string? file = args.FirstOrDefault(a => IsVhd(a) && File.Exists(a));
        if (file != null) SetVhd(file);
        if (ai >= 0 && file != null)
        {
            if (args.Length > ai + 2 && Directory.Exists(args[ai + 2])) TxtAutoOut.Text = args[ai + 2];
            Tabs.SelectedItem = TabAuto;
            _ = StartAutoAsync();
        }
        DropZone.Focus();

        if (App.ScreenshotDir != null) _ = CaptureScreenshotsAsync(App.ScreenshotDir);
    }

    public void BringToFront()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Show();
        Activate();
        Topmost = true; Topmost = false;
        NativeMethods.SetForegroundWindow(new WindowInteropHelper(this).Handle);
    }

    private void UpdateAdminBadge()
    {
        var a = AdminGuard.Check();
        AdminText.Text = (a.IsDomainAdmin ? Loc.T("AdminDomain") : Loc.T("AdminLocal")) +
                         (AppInfo.IsPortable ? " · " + Loc.T("PortableBadge") : "");
        AdminBadge.ToolTip = a.User;
        AutomationProperties.SetName(AdminBadge, AdminText.Text + " — " + a.User);
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_busy)
        {
            if (MessageBox.Show(this, Loc.T("ConfirmCloseBusy"), AppInfo.Name, MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                e.Cancel = true;
                return;
            }
            e.Cancel = true;
            _closeAfterCancel = true;
            _cts?.Cancel();
            SetStatus(Loc.T("StatusCancelling"), StatusKind.Warn);
            return;
        }
        SaveSettings();
    }

    private void SaveSettings()
    {
        S.WindowMaximized = WindowState == WindowState.Maximized;
        if (WindowState == WindowState.Normal) { S.WindowWidth = ActualWidth; S.WindowHeight = ActualHeight; }
        S.LastOutputFolder = TxtAutoOut.Text.Trim();
        S.BaseMediaPath = TxtAutoBase.Text.Trim();
        S.AutoComputeHash = ChkAutoHash.IsChecked == true;
        S.AutoOpenFolder = ChkAutoOpen.IsChecked == true;
        S.MountReadOnly = ChkReadOnly.IsChecked == true;
        S.OscdimgPath = TxtOscdimgPath.Text.Trim();
        S.Oscdimg = _options;
        S.Save();
    }

    // =================================================================== scorciatoie
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        Key k = e.Key == Key.System ? e.SystemKey : e.Key;

        if (k == Key.F1) { ShowAbout(); e.Handled = true; return; }
        if (k == Key.F5 || (ctrl && k == Key.Enter)) { StartCurrent(); e.Handled = true; return; }
        if (k == Key.Escape && _busy) { RequestCancel(); e.Handled = true; return; }
        if (!ctrl) return;

        switch (k)
        {
            case Key.D1: case Key.NumPad1: Tabs.SelectedItem = TabAuto; break;
            case Key.D2: case Key.NumPad2: Tabs.SelectedItem = TabManual; break;
            case Key.D3: case Key.NumPad3: Tabs.SelectedItem = TabOptions; break;
            case Key.D4: case Key.NumPad4: Tabs.SelectedItem = TabLog; break;
            case Key.O:
                if (Tabs.SelectedItem == TabManual) BtnVhdBrowse_Click(this, e); else BtnAutoBrowse_Click(this, e);
                break;
            case Key.M when !shift: if (!_busy) BtnMount_Click(this, e); break;
            case Key.U: if (!_busy && BtnDismount.IsEnabled) BtnDismount_Click(this, e); break;
            case Key.L when shift: BtnOpenLogFolder_Click(this, e); break;
            case Key.L: CmbLanguage.SelectedIndex = Loc.Instance.Language == "it" ? 2 : 1; break;
            case Key.T: CmbTheme.SelectedIndex = (CmbTheme.SelectedIndex + 1) % CmbTheme.Items.Count; break;
            case Key.C when shift: BtnCopyCmd_Click(this, e); break;
            case Key.I: if (!_busy) BtnInstallAdk_Click(this, e); break;
            case Key.P when shift: if (!_busy) BtnPortableOscdimg_Click(this, e); break;
            default: return;
        }
        e.Handled = true;
    }

    private void StartCurrent()
    {
        if (_busy) return;
        if (Tabs.SelectedItem == TabAuto) _ = StartAutoAsync();
        else if (Tabs.SelectedItem == TabManual || Tabs.SelectedItem == TabOptions) _ = StartManualAsync();
    }

    // =================================================================== lingua / tema
    private void CmbLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || CmbLanguage.SelectedItem is not ComboBoxItem it) return;
        S.Language = it.Tag as string ?? "";
        Loc.Instance.SetLanguage(S.Language);
        S.Save();
    }

    private void CmbTheme_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || CmbTheme.SelectedItem is not ComboBoxItem it) return;
        S.Theme = it.Tag as string ?? "System";
        ThemeManager.Apply(S.Theme);
        S.Save();
    }

    private static void SelectComboByTag(ComboBox cb, object? tag)
    {
        foreach (var o in cb.Items)
            if (o is ComboBoxItem i && Equals(i.Tag as string ?? "", tag as string ?? "")) { cb.SelectedItem = i; return; }
        cb.SelectedIndex = 0;
    }

    private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.OriginalSource != Tabs) return;
        if (Tabs.SelectedItem == TabOptions || Tabs.SelectedItem == TabManual) RefreshPreview();
        if (!S.ReduceMotion && Tabs.SelectedContent is FrameworkElement fe && SystemParameters.ClientAreaAnimation)
            fe.BeginAnimation(OpacityProperty, new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(180)));
    }

    // =================================================================== oscdimg
    private void DetectOscdimg(bool log)
    {
        _oscdimgPath = OscdimgLocator.Find(TxtOscdimgPath.Text.Trim());
        if (log)
        {
            if (_oscdimgPath != null) Logger.Info($"oscdimg: {_oscdimgPath} (v{OscdimgLocator.FileVersion(_oscdimgPath)})");
            else Logger.Warn("oscdimg.exe non trovato: installare Windows ADK - Deployment Tools.");
        }
        UpdateOscdimgStatus();
    }

    private void UpdateOscdimgStatus()
    {
        if (_oscdimgPath != null)
        {
            OscdimgStatusText.Text = Loc.T("OscdimgOk");
            BtnOscdimgStatus.Tag = "";
            OscdimgInfo.Text = Loc.F("OscdimgInfo", _oscdimgPath, OscdimgLocator.FileVersion(_oscdimgPath)) + "  —  " +
                Loc.T(OscdimgLocator.IsPortableCopy(_oscdimgPath) ? "OscdimgOriginPortable" : "OscdimgOriginSystem");
        }
        else
        {
            OscdimgStatusText.Text = Loc.T("OscdimgMissing");
            BtnOscdimgStatus.Tag = "";
            OscdimgInfo.Text = Loc.T("OscdimgInfoMissing");
        }
        AutomationProperties.SetName(BtnOscdimgStatus, OscdimgStatusText.Text);
    }

    private void BtnOscdimgStatus_Click(object sender, RoutedEventArgs e)
    {
        if (_oscdimgPath == null && !_busy) BtnInstallAdk_Click(sender, e);
        else Tabs.SelectedItem = TabOptions;
    }

    private void BtnDetectOscdimg_Click(object sender, RoutedEventArgs e)
    {
        DetectOscdimg(log: true);
        SetStatus(_oscdimgPath != null ? Loc.T("OscdimgOk") : Loc.T("OscdimgMissing"),
            _oscdimgPath != null ? StatusKind.Ok : StatusKind.Warn);
    }

    private void BtnBrowseOscdimg_Click(object sender, RoutedEventArgs e)
    {
        var d = new OpenFileDialog { Filter = "oscdimg.exe|oscdimg.exe|*.exe|*.exe", Title = Loc.Plain("LblOscdimgPath") };
        if (d.ShowDialog(this) == true) { TxtOscdimgPath.Text = d.FileName; DetectOscdimg(true); SaveSettings(); }
    }

    private async void BtnInstallAdk_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        await EnsureOscdimgAsync(forceInstall: true);
    }

    private async void BtnPortableOscdimg_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        await AcquireOscdimgAsync(portable: true);
    }

    /// <summary>
    /// Restituisce il percorso di oscdimg; se manca chiede come ottenerlo:
    /// Si = copia PORTABILE accanto all'exe (consigliato, nulla installato), No = installa l'ADK nel sistema.
    /// </summary>
    private async Task<string?> EnsureOscdimgAsync(bool forceInstall = false)
    {
        DetectOscdimg(false);
        if (_oscdimgPath != null && !forceInstall) return _oscdimgPath;

        string q = (_oscdimgPath != null ? Loc.T("ConfirmReinstallAdk") : Loc.T("OscdimgNotFound")) + Environment.NewLine + Environment.NewLine + Loc.T("ConfirmGetOscdimg");
        var answer = MessageBox.Show(this, q, AppInfo.Name, MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Cancel || answer == MessageBoxResult.None) return _oscdimgPath;
        return await AcquireOscdimgAsync(portable: answer == MessageBoxResult.Yes);
    }

    private async Task<string?> AcquireOscdimgAsync(bool portable)
    {
        bool ok = false;
        await RunBusyAsync(async (p, ct) =>
        {
            ok = portable
                ? await PortableTools.AcquireAsync(S.AdkDownloadUrl, p, ct)
                : await AdkInstaller.InstallAsync(S.AdkDownloadUrl, p, ct);
            return null;
        }, portable ? "StagePortableCopy" : "StageAdkDownload", silentSuccess: true);

        // dopo una copia portabile la si preferisce a un eventuale percorso personalizzato
        if (portable && ok && PortableTools.HasPortableOscdimg) TxtOscdimgPath.Text = "";
        DetectOscdimg(true);
        if (ok && _oscdimgPath != null)
        {
            SetStatus(Loc.T(portable ? "PortableOk" : "AdkInstalled"), StatusKind.Ok);
            RefreshPreview();
            SaveSettings();
        }
        else if (!ok && !_lastJobCancelled)
        {
            SetStatus(Loc.T(portable ? "PortableFailed" : "AdkFailed"), StatusKind.Error);
            MessageBox.Show(this, Loc.F(portable ? "PortableFailedLong" : "AdkFailedLong", Logger.CurrentFile),
                AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        return _oscdimgPath;
    }

    // =================================================================== AUTO: input
    private static bool IsVhd(string p) =>
        p.EndsWith(".vhd", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".vhdx", StringComparison.OrdinalIgnoreCase);

    private string _autoVhd = "";

    private void SetVhd(string path)
    {
        _autoVhd = path;
        TxtVhd.Text = path;
        AutoVhdTitle.Text = Path.GetFileName(path);
        try
        {
            var fi = new FileInfo(path);
            AutoVhdSub.Text = $"{Format.Bytes(fi.Length)} — {fi.DirectoryName}";
        }
        catch { AutoVhdSub.Text = path; }
        DropIcon.Text = "";
        AutomationProperties.SetName(DropZone, Loc.Plain("DropZoneA11y") + ": " + Path.GetFileName(path));
        if (string.IsNullOrWhiteSpace(TxtAutoOut.Text)) TxtAutoOut.Text = Path.GetDirectoryName(path) ?? "";
        S.LastVhdFolder = Path.GetDirectoryName(path) ?? "";
        SetStatus(Loc.F("StatusVhdSelected", Path.GetFileName(path)), StatusKind.Info);
    }

    private string? PickVhd()
    {
        var d = new OpenFileDialog
        {
            Filter = Loc.T("FilterVhd") + "|*.vhdx;*.vhd|" + Loc.T("FilterAll") + "|*.*",
            Title = Loc.Plain("BtnBrowseVhd"),
            InitialDirectory = Directory.Exists(S.LastVhdFolder) ? S.LastVhdFolder : ""
        };
        return d.ShowDialog(this) == true ? d.FileName : null;
    }

    private string? PickFolder(string title, string initial = "")
    {
        var d = new OpenFolderDialog { Title = title, InitialDirectory = Directory.Exists(initial) ? initial : "" };
        return d.ShowDialog(this) == true ? d.FolderName : null;
    }

    private string? PickIso(string title)
    {
        var d = new OpenFileDialog { Filter = "ISO|*.iso|" + Loc.T("FilterAll") + "|*.*", Title = title };
        return d.ShowDialog(this) == true ? d.FileName : null;
    }

    private void BtnAutoBrowse_Click(object sender, RoutedEventArgs e)
    {
        if (e is RoutedEventArgs r && r.OriginalSource is Button) r.Handled = true; // non far scattare il click della drop zone
        var f = PickVhd();
        if (f != null) SetVhd(f);
    }

    private void DropZone_Click(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d && FindParent<Button>(d) != null) return;
        var f = PickVhd();
        if (f != null) SetVhd(f);
    }

    private void DropZone_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Space)
        {
            var f = PickVhd();
            if (f != null) SetVhd(f);
            e.Handled = true;
        }
    }

    private static T? FindParent<T>(DependencyObject d) where T : DependencyObject
    {
        while (d != null)
        {
            if (d is T t) return t;
            d = VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d);
        }
        return null;
    }

    private static string? DroppedFile(DragEventArgs e, Func<string, bool> accept)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return null;
        return (e.Data.GetData(DataFormats.FileDrop) as string[])?.FirstOrDefault(accept);
    }

    private void DropZone_DragEnter(object sender, DragEventArgs e)
    {
        bool ok = DroppedFile(e, IsVhd) != null;
        e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
        if (ok && e.RoutedEvent == DragEnterEvent) PulseDropZone(true);
    }

    private void DropZone_DragLeave(object sender, DragEventArgs e) => PulseDropZone(false);

    private void DropZone_Drop(object sender, DragEventArgs e)
    {
        PulseDropZone(false);
        var f = DroppedFile(e, IsVhd);
        if (f != null) SetVhd(f);
        else SetStatus(Loc.T("StatusDropInvalid"), StatusKind.Warn);
    }

    private void PulseDropZone(bool on)
    {
        if (S.ReduceMotion || !SystemParameters.ClientAreaAnimation)
        {
            DropBorder.StrokeThickness = on ? 3 : 2;
            return;
        }
        var anim = new DoubleAnimation(on ? 1.18 : 1.0, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        DropIconScale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
        DropIconScale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
        DropBorder.StrokeThickness = on ? 3 : 2;
    }

    private void BtnAutoOut_Click(object sender, RoutedEventArgs e)
    {
        var f = PickFolder(Loc.Plain("LblOutputFolder"), TxtAutoOut.Text);
        if (f != null) TxtAutoOut.Text = f;
    }

    private void BtnAutoBaseFolder_Click(object sender, RoutedEventArgs e)
    {
        var f = PickFolder(Loc.Plain("LblBaseMedia"), TxtAutoBase.Text);
        if (f != null) { TxtAutoBase.Text = f; TxtManualBase.Text = f; }
    }

    private void BtnAutoBaseIso_Click(object sender, RoutedEventArgs e)
    {
        var f = PickIso(Loc.Plain("LblBaseMedia"));
        if (f != null) { TxtAutoBase.Text = f; TxtManualBase.Text = f; }
    }

    private async void BtnAutoStart_Click(object sender, RoutedEventArgs e) => await StartAutoAsync();

    private async Task StartAutoAsync()
    {
        if (_busy) return;
        string vhd = _autoVhd.Length > 0 ? _autoVhd : TxtVhd.Text.Trim();
        if (!IsVhd(vhd) || !File.Exists(vhd))
        {
            SetStatus(Loc.T("ErrSelectVhd"), StatusKind.Warn);
            Shake(DropZone);
            DropZone.Focus();
            return;
        }
        string outDir = TxtAutoOut.Text.Trim();
        if (outDir.Length == 0) outDir = Path.GetDirectoryName(vhd) ?? "";
        TxtAutoOut.Text = outDir;
        string baseMedia = TxtAutoBase.Text.Trim();
        if (baseMedia.Length > 0 && !File.Exists(baseMedia) && !Directory.Exists(baseMedia))
        {
            SetStatus(Loc.T("ErrBaseMediaInvalid"), StatusKind.Warn);
            TxtAutoBase.Focus();
            return;
        }
        if (_mounted != null && string.Equals(_mounted.ImagePath, vhd, StringComparison.OrdinalIgnoreCase))
        {
            // gia' montato dalla scheda manuale: lo smonto per ripartire in sola lettura pulita
            await DismountCurrentAsync(silent: true);
        }

        var osc = await EnsureOscdimgAsync();
        if (osc == null) { SetStatus(Loc.T("OscdimgMissing"), StatusKind.Error); return; }
        SaveSettings();

        var job = new AutoJob
        {
            VhdPath = vhd,
            OutputFolder = outDir,
            BaseMediaPath = baseMedia,
            ComputeHash = ChkAutoHash.IsChecked == true
        };
        await RunBusyAsync(async (p, ct) => await new JobEngine(osc, p, ct).RunAutoAsync(job), "StageMount",
            openFolder: ChkAutoOpen.IsChecked == true);
    }

    // =================================================================== drop da Esplora non elevato
    /// <summary>
    /// L'app gira elevata: Windows (UIPI) blocca il drag &amp; drop OLE da Esplora file non elevato.
    /// Si accettano quindi i file con WM_DROPFILES, consentito tramite ChangeWindowMessageFilterEx.
    /// </summary>
    private void EnableElevatedFileDrop()
    {
        try
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            foreach (uint msg in new uint[] { NativeMethods.WM_DROPFILES, NativeMethods.WM_COPYDATA, NativeMethods.WM_COPYGLOBALDATA })
                NativeMethods.ChangeWindowMessageFilterEx(hwnd, msg, NativeMethods.MSGFLT_ALLOW, IntPtr.Zero);
            NativeMethods.RevokeDragDrop(hwnd);
            NativeMethods.DragAcceptFiles(hwnd, true);
            HwndSource.FromHwnd(hwnd)?.AddHook(DropHook);
            // WPF potrebbe registrare di nuovo il drop target OLE al caricamento: lo revoco anche dopo
            Loaded += (_, _) => { NativeMethods.RevokeDragDrop(hwnd); NativeMethods.DragAcceptFiles(hwnd, true); };
        }
        catch (Exception ex) { Logger.Warn("Drop elevato non disponibile: " + ex.Message); }
    }

    private IntPtr DropHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != NativeMethods.WM_DROPFILES) return IntPtr.Zero;
        handled = true;
        var files = new List<string>();
        NativeMethods.POINT pt = default;
        try
        {
            uint n = NativeMethods.DragQueryFile(wParam, 0xFFFFFFFF, null, 0);
            for (uint i = 0; i < n; i++)
            {
                var sb = new StringBuilder(32768);
                if (NativeMethods.DragQueryFile(wParam, i, sb, (uint)sb.Capacity) > 0) files.Add(sb.ToString());
            }
            NativeMethods.DragQueryPoint(wParam, out pt);
        }
        finally { NativeMethods.DragFinish(wParam); }
        if (files.Count == 0 || _busy) return IntPtr.Zero;

        // elemento sotto il punto di rilascio (pixel fisici -> DIP)
        var src = PresentationSource.FromVisual(this);
        Point p = new(pt.X, pt.Y);
        if (src?.CompositionTarget != null) p = src.CompositionTarget.TransformFromDevice.Transform(p);
        var hit = InputHitTest(p) as DependencyObject;
        bool overFolderBox = hit != null && (FindParent<TextBox>(hit) is TextBox tb) && (tb == TxtSrcFolder || tb == TxtStaging || tb == TxtAutoOut || tb == TxtManualBase || tb == TxtAutoBase);

        string? dir = files.FirstOrDefault(Directory.Exists);
        string? vhd = files.FirstOrDefault(f => IsVhd(f) && File.Exists(f));
        string? iso = files.FirstOrDefault(f => f.EndsWith(".iso", StringComparison.OrdinalIgnoreCase) && File.Exists(f));
        if (overFolderBox && hit != null && FindParent<TextBox>(hit) is TextBox target)
        {
            if ((target == TxtManualBase || target == TxtAutoBase) && (iso ?? dir) is string b) { target.Text = b; return IntPtr.Zero; }
            if (dir != null) { target.Text = dir; return IntPtr.Zero; }
        }
        if (vhd != null)
        {
            SetVhd(vhd);
            if (Tabs.SelectedItem == TabManual) RbSrcVhd.IsChecked = true;
        }
        else if (dir != null && Tabs.SelectedItem == TabManual)
        {
            RbSrcFolder.IsChecked = true;
            TxtSrcFolder.Text = dir;
        }
        else SetStatus(Loc.T("StatusDropInvalid"), StatusKind.Warn);
        return IntPtr.Zero;
    }

    // =================================================================== MANUALE: sorgente
    private void SourceMode_Changed(object sender, RoutedEventArgs e)
    {
        if (PnlSrcVhd == null || PnlSrcFolder == null) return;
        bool vhd = RbSrcVhd.IsChecked == true;
        PnlSrcVhd.Visibility = vhd ? Visibility.Visible : Visibility.Collapsed;
        PnlSrcFolder.Visibility = vhd ? Visibility.Collapsed : Visibility.Visible;
        RefreshPreview();
    }

    private void CaptureMode_Changed(object sender, RoutedEventArgs e)
    {
        if (PnlStaging == null || PnlDism == null) return;
        bool copy = RbCapCopy.IsChecked == true, dism = RbCapDism.IsChecked == true;
        PnlStaging.Visibility = copy || dism ? Visibility.Visible : Visibility.Collapsed;
        PnlDism.Visibility = dism ? Visibility.Visible : Visibility.Collapsed;
        if ((copy || dism) && TxtStaging.Text.Trim().Length == 0)
        {
            string iso = TxtIsoOut.Text.Trim();
            string dir = iso.Length > 0 ? Path.GetDirectoryName(iso) ?? "" : TxtAutoOut.Text.Trim();
            if (dir.Length > 0) TxtStaging.Text = Path.Combine(dir, "staging_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        }
        if (dism && TxtWimName.Text.Trim().Length == 0 && TxtVhd.Text.Trim().Length > 0)
            TxtWimName.Text = Path.GetFileNameWithoutExtension(TxtVhd.Text.Trim());
        RefreshPreview();
    }

    private void BtnVhdBrowse_Click(object sender, RoutedEventArgs e)
    {
        var f = PickVhd();
        if (f != null) { SetVhd(f); RbSrcVhd.IsChecked = true; }
    }

    private void Txt_PreviewDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void TxtVhd_PreviewDrop(object sender, DragEventArgs e)
    {
        var f = DroppedFile(e, IsVhd);
        if (f != null) SetVhd(f);
        e.Handled = true;
    }

    private void TxtFolder_PreviewDrop(object sender, DragEventArgs e)
    {
        var f = DroppedFile(e, Directory.Exists);
        if (f != null && sender is TextBox tb) tb.Text = f;
        e.Handled = true;
    }

    private void BtnSrcFolder_Click(object sender, RoutedEventArgs e)
    {
        var f = PickFolder(Loc.Plain("LblSrcFolder"), TxtSrcFolder.Text);
        if (f != null) TxtSrcFolder.Text = f;
    }

    private async void BtnMount_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        string vhd = TxtVhd.Text.Trim();
        if (!IsVhd(vhd) || !File.Exists(vhd))
        {
            SetStatus(Loc.T("ErrSelectVhd"), StatusKind.Warn);
            Tabs.SelectedItem = TabManual;
            TxtVhd.Focus();
            return;
        }
        if (_mounted != null) await DismountCurrentAsync(silent: true);
        bool ro = ChkReadOnly.IsChecked == true, letters = ChkAssignLetters.IsChecked == true;
        MountResult? res = null;
        await RunBusyAsync(async (p, ct) =>
        {
            p.Report(new JobProgress(Loc.T("StageMount"), null, Path.GetFileName(vhd)));
            res = await DiskImageService.MountAsync(vhd, ro, letters, ct);
            return null;
        }, "StageMount", silentSuccess: true);
        if (res == null) return;

        _mounted = res;
        RefreshVolumesView();
        BtnDismount.IsEnabled = true;
        var best = res.Volumes.Where(v => v.HasData && v.DriveLetter.Length > 0)
                      .OrderByDescending(v => v.IsSetupMedia).ThenByDescending(v => v.IsWinPe)
                      .ThenByDescending(v => v.IsWindowsOs).ThenByDescending(v => v.Used).FirstOrDefault();
        if (best != null) LstVolumes.SelectedItem = best;
        if (TxtIsoOut.Text.Trim().Length == 0)
            TxtIsoOut.Text = Path.Combine(Path.GetDirectoryName(vhd) ?? "", Format.SafeFileName(Path.GetFileNameWithoutExtension(vhd)) + ".iso");
        SetStatus(Loc.F("StatusMounted", Path.GetFileName(vhd), res.Volumes.Count(v => v.DriveLetter.Length > 0)), StatusKind.Ok);
        if (!res.OwnedByUs) SetStatus(Loc.T("StatusAlreadyMounted"), StatusKind.Warn);
        LstVolumes.Focus();
    }

    private async void BtnDismount_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _mounted == null) return;
        if (!_mounted.OwnedByUs &&
            MessageBox.Show(this, Loc.T("ConfirmDismountForeign"), AppInfo.Name, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        await DismountCurrentAsync(silent: false);
    }

    private async Task DismountCurrentAsync(bool silent)
    {
        if (_mounted == null) return;
        string img = _mounted.ImagePath;
        await RunBusyAsync(async (p, ct) =>
        {
            p.Report(new JobProgress(Loc.T("StageDismount"), null, Path.GetFileName(img)));
            await DiskImageService.DismountAsync(img, ct);
            return null;
        }, "StageDismount", silentSuccess: true);
        _mounted = null;
        BtnDismount.IsEnabled = false;
        RefreshVolumesView();
        if (!silent) SetStatus(Loc.F("StatusDismounted", Path.GetFileName(img)), StatusKind.Ok);
    }

    private void RefreshVolumesView()
    {
        if (LstVolumes == null) return;
        var sel = LstVolumes.SelectedItem;
        LstVolumes.ItemsSource = null;
        LstVolumes.ItemsSource = _mounted?.Volumes;
        if (sel != null && _mounted?.Volumes.Contains(sel as VolumeInfo ?? new VolumeInfo()) == true) LstVolumes.SelectedItem = sel;
        VolumesEmpty.Visibility = _mounted == null || _mounted.Volumes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void LstVolumes_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LstVolumes.SelectedItem is VolumeInfo v)
        {
            if (_options.Label.Length == 0)
            {
                _options.Label = OscdimgOptions.SanitizeLabel(v.Label.Length > 0 ? v.Label : Path.GetFileNameWithoutExtension(TxtVhd.Text));
                RebindOptions();
            }
            if (v.IsWindowsOs && !v.IsSetupMedia && RbCapDirect.IsChecked == true)
                SetStatus(Loc.T("HintWindowsVolume"), StatusKind.Info);
        }
        RefreshPreview();
    }

    private void AnyInput_Changed(object sender, RoutedEventArgs e) => ScheduleRefresh();

    private void BtnStaging_Click(object sender, RoutedEventArgs e)
    {
        var f = PickFolder(Loc.Plain("LblStaging"), TxtStaging.Text);
        if (f != null) TxtStaging.Text = Path.Combine(f, "staging_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
    }

    private void BtnWimConfig_Click(object sender, RoutedEventArgs e)
    {
        var d = new OpenFileDialog { Filter = "INI|*.ini|" + Loc.T("FilterAll") + "|*.*", Title = Loc.Plain("LblWimConfig") };
        if (d.ShowDialog(this) == true) TxtWimConfig.Text = d.FileName;
    }

    private void BtnManualBaseFolder_Click(object sender, RoutedEventArgs e)
    {
        var f = PickFolder(Loc.Plain("LblBaseMedia"), TxtManualBase.Text);
        if (f != null) TxtManualBase.Text = f;
    }

    private void BtnManualBaseIso_Click(object sender, RoutedEventArgs e)
    {
        var f = PickIso(Loc.Plain("LblBaseMedia"));
        if (f != null) TxtManualBase.Text = f;
    }

    private void BtnIsoOut_Click(object sender, RoutedEventArgs e)
    {
        string cur = TxtIsoOut.Text.Trim();
        var d = new SaveFileDialog
        {
            Filter = "ISO|*.iso",
            DefaultExt = ".iso",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = cur.Length > 0 ? Path.GetFileName(cur) : "image.iso",
            InitialDirectory = cur.Length > 0 && Directory.Exists(Path.GetDirectoryName(cur)) ? Path.GetDirectoryName(cur) : ""
        };
        if (d.ShowDialog(this) == true) TxtIsoOut.Text = d.FileName;
    }

    private void BtnGoOptions_Click(object sender, RoutedEventArgs e) => Tabs.SelectedItem = TabOptions;

    // =================================================================== MANUALE: avvio
    private string ManualSource(out long used)
    {
        used = 0;
        if (RbSrcFolder.IsChecked == true) return TxtSrcFolder.Text.Trim();
        if (LstVolumes.SelectedItem is VolumeInfo v) { used = v.Used; return v.Root; }
        return "";
    }

    private CaptureMode ManualMode =>
        RbCapCopy.IsChecked == true ? CaptureMode.CopyToStaging : RbCapDism.IsChecked == true ? CaptureMode.DismWim : CaptureMode.Direct;

    private async void BtnManualStart_Click(object sender, RoutedEventArgs e) => await StartManualAsync();

    private async Task StartManualAsync()
    {
        if (_busy) return;
        string src = ManualSource(out long used);
        if (src.Length == 0)
        {
            SetStatus(RbSrcFolder.IsChecked == true ? Loc.T("ValSourceMissing") : Loc.T("ErrMountFirst"), StatusKind.Warn);
            Tabs.SelectedItem = TabManual;
            return;
        }
        var mode = ManualMode;
        string iso = TxtIsoOut.Text.Trim();
        if (iso.Length > 0 && !iso.EndsWith(".iso", StringComparison.OrdinalIgnoreCase)) { iso += ".iso"; TxtIsoOut.Text = iso; }

        var opts = _options.Clone();
        // in modalita' copia/DISM la radice dell'ISO e' la cartella di lavoro: valida i parametri su quella
        var errs = opts.Validate(mode == CaptureMode.Direct ? src : (TxtStaging.Text.Trim().Length > 0 ? TxtStaging.Text.Trim() : src),
                                 iso, checkFiles: mode == CaptureMode.Direct);
        if (mode != CaptureMode.Direct) errs.Remove("ValSourceNotFound");
        if (mode == CaptureMode.DismWim && ChkInject.IsChecked == true) errs.Remove("ValBootFiles");
        if (errs.Count > 0)
        {
            ManualValidation.Text = string.Join(Environment.NewLine, errs.Select(Loc.T));
            SetStatus(Loc.T(errs[0]), StatusKind.Warn);
            return;
        }
        if (mode != CaptureMode.Direct && TxtStaging.Text.Trim().Length == 0)
        {
            SetStatus(Loc.T("ErrStagingMissing"), StatusKind.Warn);
            TxtStaging.Focus();
            return;
        }
        if (mode == CaptureMode.CopyToStaging && Directory.Exists(TxtStaging.Text.Trim()) &&
            Directory.EnumerateFileSystemEntries(TxtStaging.Text.Trim()).Any() &&
            MessageBox.Show(this, Loc.F("ConfirmStagingMirror", TxtStaging.Text.Trim()), AppInfo.Name,
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        if (!opts.ScanOnly && File.Exists(iso) &&
            MessageBox.Show(this, Loc.F("ConfirmOverwrite", iso), AppInfo.Name, MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        if (mode == CaptureMode.DismWim && ChkInject.IsChecked == true &&
            !File.Exists(TxtManualBase.Text.Trim()) && !Directory.Exists(TxtManualBase.Text.Trim()))
        {
            SetStatus(Loc.T("ErrBaseMediaMissing"), StatusKind.Warn);
            TxtManualBase.Focus();
            return;
        }

        var osc = await EnsureOscdimgAsync();
        if (osc == null) { SetStatus(Loc.T("OscdimgMissing"), StatusKind.Error); return; }
        SaveSettings();

        var job = new ManualJob
        {
            SourceRoot = src,
            SourceUsedBytes = used,
            Mode = mode,
            StagingDir = TxtStaging.Text.Trim(),
            KeepStaging = ChkKeepStaging.IsChecked == true,
            InjectIntoBaseMedia = ChkInject.IsChecked == true,
            BaseMediaPath = TxtManualBase.Text.Trim(),
            IsoPath = iso,
            Oscdimg = opts,
            ComputeHash = ChkManualHash.IsChecked == true,
            Dism = new DismOptions
            {
                ImageName = TxtWimName.Text.Trim(),
                Description = TxtWimDesc.Text.Trim(),
                Compression = (CmbWimCompress.SelectedItem as ComboBoxItem)?.Tag is WimCompression c ? c : WimCompression.Max,
                CheckIntegrity = ChkWimIntegrity.IsChecked == true,
                Verify = ChkWimVerify.IsChecked == true,
                Bootable = ChkWimBootable.IsChecked == true,
                NoRpFix = ChkWimNoRpFix.IsChecked == true,
                ExtendedAttributes = ChkWimEa.IsChecked == true,
                ConfigFile = TxtWimConfig.Text.Trim()
            }
        };
        await RunBusyAsync(async (p, ct) => await new JobEngine(osc, p, ct).RunManualAsync(job), "StageOscdimg",
            openFolder: ChkManualOpen.IsChecked == true);
    }

    // =================================================================== OPZIONI
    private bool _refreshPending;

    private void ScheduleRefresh()
    {
        if (_refreshPending || _loading) return;
        _refreshPending = true;
        Dispatcher.InvokeAsync(() => { _refreshPending = false; RefreshPreview(); }, DispatcherPriority.Background);
    }

    private void RebindOptions()
    {
        OptionsRoot.DataContext = null;
        OptionsRoot.DataContext = _options;
        RefreshPreview();
    }

    private void RefreshPreview()
    {
        if (_loading || TxtPreviewOpts == null) return;
        var o = _options;
        bool udf = o.FileSystem is IsoFileSystem.UdfOnly or IsoFileSystem.UdfIso;
        bool u2 = o.FileSystem == IsoFileSystem.UdfOnly;
        CmbUdfVer.IsEnabled = udf; ChkYl.IsEnabled = udf;
        ChkUs.IsEnabled = u2; ChkUe.IsEnabled = u2; ChkUf.IsEnabled = u2; TxtUdfReadme.IsEnabled = u2; BtnUdfReadme.IsEnabled = u2;
        bool j2 = o.FileSystem == IsoFileSystem.JolietOnly;
        TxtJolietReadme.IsEnabled = j2; BtnJolietReadme.IsEnabled = j2;
        ChkD.IsEnabled = o.FileSystem is IsoFileSystem.Iso9660 or IsoFileSystem.Iso9660Long;
        ChkUt.IsEnabled = o.FileSystem == IsoFileSystem.DvdVideo;
        bool bios = o.Boot is BootMode.BiosAndUefi or BootMode.BiosOnly or BootMode.SingleCustom;
        bool uefi = o.Boot is BootMode.BiosAndUefi or BootMode.UefiOnly;
        TxtBiosFile.IsEnabled = bios; BtnBiosFile.IsEnabled = bios;
        TxtUefiFile.IsEnabled = uefi; BtnUefiFile.IsEnabled = uefi;
        TxtPlatform.IsEnabled = o.Boot == BootMode.SingleCustom;
        TxtLoadSeg.IsEnabled = o.Boot is BootMode.BiosAndUefi or BootMode.BiosOnly;
        ChkE.IsEnabled = o.Boot != BootMode.None;
        TxtMaxSize.IsEnabled = o.SizeLimit == SizeLimitMode.MaxSize;

        string src = ManualSource(out _);
        var mode = ManualMode;
        string isoRoot = mode == CaptureMode.Direct ? src : TxtStaging.Text.Trim();
        string shownSrc = isoRoot.Length > 0 ? isoRoot : Loc.T("PlaceholderSource");
        string shownIso = TxtIsoOut.Text.Trim().Length > 0 ? TxtIsoOut.Text.Trim() : Loc.T("PlaceholderIso");
        string exe = _oscdimgPath != null ? OscdimgOptions.Q(_oscdimgPath) : "oscdimg.exe";
        string cmd = exe + " " + o.BuildArguments(shownSrc, shownIso);
        TxtPreviewOpts.Text = cmd;
        TxtPreviewManual.Text = cmd;

        var errs = o.Validate(isoRoot, TxtIsoOut.Text.Trim(), checkFiles: mode == CaptureMode.Direct && isoRoot.Length > 0)
                    .Where(k => k is not ("ValSourceMissing" or "ValTargetMissing")).ToList();
        if (mode == CaptureMode.DismWim && ChkInject.IsChecked == true) errs.Remove("ValBootFiles");
        string val = errs.Count == 0 ? "" : "⚠ " + string.Join(Environment.NewLine + "⚠ ", errs.Select(Loc.T));
        OptsValidation.Text = val;
        ManualValidation.Text = val;
    }

    private void ApplyPreset(OscdimgOptions p)
    {
        p.Label = _options.Label;
        if (p.Boot != BootMode.None)
        {
            p.BiosBootFile = _options.BiosBootFile;
            p.UefiBootFile = _options.UefiBootFile;
        }
        _options = p;
        RebindOptions();
        SetStatus(Loc.T("StatusPresetApplied"), StatusKind.Ok);
    }

    private void PresetBoot_Click(object sender, RoutedEventArgs e)
    {
        ApplyPreset(OscdimgOptions.WindowsBootPreset());
        if (_options.BiosBootFile.Length == 0 && _options.UefiBootFile.Length == 0) BtnUseAdkBoot_Click(sender, e);
    }

    private void PresetData_Click(object sender, RoutedEventArgs e) => ApplyPreset(OscdimgOptions.DataPreset());
    private void PresetCompat_Click(object sender, RoutedEventArgs e) => ApplyPreset(OscdimgOptions.CompatPreset());

    private void PresetFromVolume_Click(object sender, RoutedEventArgs e)
    {
        if (LstVolumes.SelectedItem is not VolumeInfo v)
        {
            SetStatus(Loc.T("ErrMountFirst"), StatusKind.Warn);
            return;
        }
        var adk = OscdimgLocator.AdkBootFiles(_oscdimgPath);
        string bios = v.BiosBootFile.Length > 0 ? v.BiosBootFile : (v.IsSetupMedia || v.IsWinPe ? adk.Bios : "");
        string uefi = v.UefiBootFile.Length > 0 ? v.UefiBootFile : (v.IsSetupMedia || v.IsWinPe ? adk.Uefi : "");
        var p = bios.Length > 0 || uefi.Length > 0 ? OscdimgOptions.WindowsBootPreset() : OscdimgOptions.DataPreset();
        p.BiosBootFile = bios; p.UefiBootFile = uefi;
        p.Boot = bios.Length > 0 && uefi.Length > 0 ? BootMode.BiosAndUefi : uefi.Length > 0 ? BootMode.UefiOnly
               : bios.Length > 0 ? BootMode.BiosOnly : BootMode.None;
        p.Label = OscdimgOptions.SanitizeLabel(v.Label.Length > 0 ? v.Label : Path.GetFileNameWithoutExtension(TxtVhd.Text));
        _options = p;
        RebindOptions();
        SetStatus(Loc.F("StatusPresetFromVolume", v.Root, v.Kind), StatusKind.Ok);
    }

    private void BrowseTextFile_Click(object sender, RoutedEventArgs e)
    {
        var d = new OpenFileDialog { Filter = "TXT|*.txt|" + Loc.T("FilterAll") + "|*.*" };
        if (d.ShowDialog(this) != true) return;
        if (sender == BtnUdfReadme) _options.UdfReadmeFile = d.FileName;
        else if (sender == BtnJolietReadme) _options.JolietReadmeFile = d.FileName;
        else if (sender == BtnOrderFile) _options.OrderFile = d.FileName;
        RebindOptions();
    }

    private void BrowseBootFile_Click(object sender, RoutedEventArgs e)
    {
        var d = new OpenFileDialog { Filter = Loc.T("FilterBoot") + "|*.com;*.bin;*.img|" + Loc.T("FilterAll") + "|*.*" };
        if (d.ShowDialog(this) != true) return;
        if (sender == BtnBiosFile) _options.BiosBootFile = d.FileName;
        else _options.UefiBootFile = d.FileName;
        RebindOptions();
    }

    private void BtnUseAdkBoot_Click(object sender, RoutedEventArgs e)
    {
        var adk = OscdimgLocator.AdkBootFiles(_oscdimgPath);
        if (adk.Bios.Length == 0 && adk.Uefi.Length == 0)
        {
            SetStatus(Loc.T("ErrAdkBootMissing"), StatusKind.Warn);
            return;
        }
        _options.BiosBootFile = adk.Bios;
        _options.UefiBootFile = adk.Uefi;
        if (_options.Boot == BootMode.None) _options.Boot = BootMode.BiosAndUefi;
        RebindOptions();
        SetStatus(Loc.T("StatusAdkBoot"), StatusKind.Ok);
    }

    private void BtnNoPrompt_Click(object sender, RoutedEventArgs e)
    {
        string cur = _options.UefiBootFile;
        string dir = cur.Length > 0 ? Path.GetDirectoryName(cur) ?? "" : Path.GetDirectoryName(_oscdimgPath ?? "") ?? "";
        string np = Path.Combine(dir, "efisys_noprompt.bin");
        if (!File.Exists(np))
        {
            SetStatus(Loc.T("ErrNoPromptMissing"), StatusKind.Warn);
            return;
        }
        _options.UefiBootFile = np;
        RebindOptions();
        SetStatus(Loc.T("StatusNoPrompt"), StatusKind.Ok);
    }

    private void BtnNow_Click(object sender, RoutedEventArgs e)
    {
        _options.Timestamp = DateTime.Now.ToString("MM/dd/yyyy,HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        _options.UseTimestamp = true;
        RebindOptions();
    }

    private void BtnCopyCmd_Click(object sender, RoutedEventArgs e)
    {
        RefreshPreview();
        if (TrySetClipboard(TxtPreviewOpts.Text)) SetStatus(Loc.T("StatusCopied"), StatusKind.Ok);
    }

    // =================================================================== REGISTRO
    private void FlushLog()
    {
        if (_logQueue.IsEmpty) return;
        bool verbose = ChkVerbose.IsChecked == true;
        var sb = new StringBuilder();
        while (_logQueue.TryDequeue(out var item))
        {
            if (item.Item2 == LogLevel.Debug && !verbose) continue;
            string tag = item.Item2 switch
            {
                LogLevel.Error => "✖", LogLevel.Warn => "⚠", LogLevel.Success => "✔", LogLevel.Debug => "·", _ => "•"
            };
            sb.Append(item.Item1.ToString("HH:mm:ss")).Append(' ').Append(tag).Append(' ').AppendLine(item.Item3);
            _logLines++;
        }
        if (sb.Length == 0) return;
        _logBuffer.Append(sb);
        if (_logLines > 6000)
        {
            // mantiene le ultime ~5000 righe nella vista (il file di log resta completo)
            string all = _logBuffer.ToString();
            int cut = 0;
            for (int i = 0; i < 1000 && cut >= 0; i++) cut = all.IndexOf('\n', cut + 1);
            if (cut > 0) { _logBuffer.Clear(); _logBuffer.Append(all[(cut + 1)..]); _logLines -= 1000; }
            TxtLog.Text = _logBuffer.ToString();
        }
        else TxtLog.AppendText(sb.ToString());
        TxtLog.ScrollToEnd();
    }

    private void BtnCopyLog_Click(object sender, RoutedEventArgs e)
    {
        if (TrySetClipboard(TxtLog.Text)) SetStatus(Loc.T("StatusCopied"), StatusKind.Ok);
    }

    private void BtnSaveLog_Click(object sender, RoutedEventArgs e)
    {
        var d = new SaveFileDialog { Filter = "Log|*.log;*.txt", FileName = $"VhdIsoStudio_{DateTime.Now:yyyyMMdd_HHmmss}.log" };
        if (d.ShowDialog(this) == true)
        {
            try { File.WriteAllText(d.FileName, TxtLog.Text, Encoding.UTF8); SetStatus(Loc.T("StatusSaved"), StatusKind.Ok); }
            catch (Exception ex) { SetStatus(ex.Message, StatusKind.Error); }
        }
    }

    private void BtnOpenLogFolder_Click(object sender, RoutedEventArgs e) =>
        OpenInExplorer(Path.GetDirectoryName(Logger.CurrentFile) ?? AppInfo.LogDir, select: null);

    private void BtnClearLog_Click(object sender, RoutedEventArgs e)
    {
        _logBuffer.Clear(); _logLines = 0; TxtLog.Clear();
    }

    // =================================================================== RISULTATO
    private void ShowResult(JobResult r)
    {
        _lastResult = r;
        ResultCard.Visibility = Visibility.Visible;
        ResultIcon.Text = "";
        ResultIcon.SetResourceReference(TextBlock.ForegroundProperty, "Ok");
        ResultTitle.Text = r.IsoPath.Length > 0 ? Loc.T("ResultOk") : Loc.T("ScanOnlyDone");
        ResultSummary.Text = r.Summary;
        var sb = new StringBuilder();
        if (r.IsoPath.Length > 0)
        {
            sb.AppendLine(r.IsoPath);
            sb.AppendLine($"{Loc.T("ResultSize")}: {Format.Bytes(r.IsoSize)}");
        }
        if (r.Sha256.Length > 0) sb.AppendLine("SHA-256: " + r.Sha256);
        if (r.WimPath != null) sb.AppendLine("WIM: " + r.WimPath);
        sb.Append($"{Loc.T("ResultTime")}: {Format.Duration(r.Elapsed)}");
        ResultDetails.Text = sb.ToString();
        BtnResultCopy.IsEnabled = r.Sha256.Length > 0;
        BtnResultOpen.IsEnabled = r.IsoPath.Length > 0;
        if (!S.ReduceMotion && SystemParameters.ClientAreaAnimation)
            ResultCard.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250)));
    }

    private void BtnResultOpen_Click(object sender, RoutedEventArgs e)
    {
        if (_lastResult?.IsoPath is { Length: > 0 } p) OpenInExplorer(Path.GetDirectoryName(p)!, p);
    }

    private void BtnResultCopy_Click(object sender, RoutedEventArgs e)
    {
        if (_lastResult?.Sha256 is { Length: > 0 } h && TrySetClipboard(h)) SetStatus(Loc.T("StatusCopied"), StatusKind.Ok);
    }

    // =================================================================== esecuzione lavori
    private async Task RunBusyAsync(Func<IProgress<JobProgress>, CancellationToken, Task<JobResult?>> work, string firstStageKey,
                                    bool openFolder = false, bool silentSuccess = false)
    {
        if (_busy) return;
        SetBusy(true);
        _lastJobCancelled = false;
        _cts = new CancellationTokenSource();
        _stage = ""; _stageFirstPct = -1;
        var progress = new Progress<JobProgress>(OnProgress);
        OnProgress(new JobProgress(Loc.T(firstStageKey), null, null));
        try
        {
            var result = await Task.Run(() => work(progress, _cts.Token));
            if (result != null)
            {
                ShowResult(result);
                SetStatus(Loc.F("StatusDone", Format.Duration(result.Elapsed)), StatusKind.Ok);
                SystemSounds.Asterisk.Play();
                NativeMethods.Flash(new WindowInteropHelper(this).Handle);
                if (openFolder && result.IsoPath.Length > 0) OpenInExplorer(Path.GetDirectoryName(result.IsoPath)!, result.IsoPath);
                if (Tabs.SelectedItem != TabAuto && Tabs.SelectedItem != TabManual) Tabs.SelectedItem = TabAuto;
                if (Tabs.SelectedItem == TabManual)
                    MessageBox.Show(this, ResultDetails.Text, Loc.Plain("ResultOk"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else if (!silentSuccess) SetStatus(Loc.T("StatusReady"), StatusKind.Ok);
        }
        catch (OperationCanceledException)
        {
            _lastJobCancelled = true;
            Logger.Warn("Operazione annullata dall'utente.");
            SetStatus(Loc.T("StatusCancelled"), StatusKind.Warn);
        }
        catch (UserFacingException ex)
        {
            Logger.Error("Errore: " + ex.Message);
            SetStatus(Loc.T(ex.Key), StatusKind.Error);
            MessageBox.Show(this, ex.Localized + Environment.NewLine + Environment.NewLine + Loc.F("SeeLog", Logger.CurrentFile),
                AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (Exception ex)
        {
            Logger.Error("Errore imprevisto", ex);
            string msg = ex.GetBaseException().Message;
            SetStatus(msg, StatusKind.Error);
            MessageBox.Show(this, Loc.F("ErrUnexpected", msg, Logger.CurrentFile), AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            SetBusy(false);
            if (_closeAfterCancel) { _closeAfterCancel = false; _ = Dispatcher.InvokeAsync(Close); }
        }
    }

    private void OnProgress(JobProgress p)
    {
        var now = DateTime.Now;
        if (p.Stage != _stage)
        {
            _stage = p.Stage;
            _stageStart = now;
            _stageFirstPct = -1;
            StatusText.Text = p.Stage;
            Logger.Info("» " + p.Stage);
        }
        if (p.Percent is double pct)
        {
            if (_stageFirstPct < 0) { _stageFirstPct = pct; _stageStart = now; }
            Progress.IsIndeterminate = false;
            Progress.Value = pct;
            StatusPercent.Text = $"{pct:0}%";
            TaskbarItemInfo.ProgressState = TaskbarItemProgressState.Normal;
            TaskbarItemInfo.ProgressValue = pct / 100.0;
            double done = pct - _stageFirstPct;
            var elapsed = now - _stageStart;
            if (done > 0.5 && elapsed.TotalSeconds > 3)
            {
                var eta = TimeSpan.FromSeconds(elapsed.TotalSeconds * (100 - pct) / done);
                StatusEta.Text = Loc.F("Eta", Format.Duration(eta));
            }
            AutomationProperties.SetName(Progress, $"{Loc.T("ProgressA11y")} {pct:0}%");
        }
        else
        {
            Progress.IsIndeterminate = true;
            StatusPercent.Text = "";
            StatusEta.Text = "";
            TaskbarItemInfo.ProgressState = TaskbarItemProgressState.Indeterminate;
        }
        if (p.Detail != null && (now - _lastUiUpdate).TotalMilliseconds > 120)
        {
            StatusDetail.Text = p.Detail;
            _lastUiUpdate = now;
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        BtnCancel.IsEnabled = busy;
        foreach (var b in new UIElement[] { BtnAutoStart, BtnManualStart, BtnMount, BtnAutoBrowse, DropZone })
            b.IsEnabled = !busy;
        BtnDismount.IsEnabled = !busy && _mounted != null;
        Cursor = busy ? Cursors.AppStarting : null;
        if (!busy)
        {
            Progress.IsIndeterminate = false;
            Progress.Value = 0;
            StatusPercent.Text = "";
            StatusEta.Text = "";
            StatusDetail.Text = "";
            TaskbarItemInfo.ProgressState = TaskbarItemProgressState.None;
            AutomationProperties.SetName(Progress, Loc.T("ProgressA11y"));
        }
        else SetStatusIcon(StatusKind.Busy);
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e) => RequestCancel();

    private void RequestCancel()
    {
        if (!_busy || _cts == null) return;
        if (MessageBox.Show(this, Loc.T("ConfirmCancel"), AppInfo.Name, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            _cts.Cancel();
            SetStatus(Loc.T("StatusCancelling"), StatusKind.Warn);
        }
    }

    // =================================================================== status
    private enum StatusKind { Info, Ok, Warn, Error, Busy }

    private void SetStatus(string text, StatusKind kind)
    {
        StatusText.Text = text;
        SetStatusIcon(kind);
        // annuncio per screen reader (regione live)
        var peer = UIElementAutomationPeer.FromElement(StatusText) ?? UIElementAutomationPeer.CreatePeerForElement(StatusText);
        peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        if (kind == StatusKind.Error) SystemSounds.Hand.Play();
    }

    private void SetStatusIcon(StatusKind kind)
    {
        (string glyph, string brush) = kind switch
        {
            StatusKind.Ok => ("", "Ok"),
            StatusKind.Warn => ("", "Warn"),
            StatusKind.Error => ("", "Err"),
            StatusKind.Busy => ("", "Accent"),
            _ => ("", "Accent")
        };
        StatusIcon.Text = glyph;
        StatusIcon.SetResourceReference(TextBlock.ForegroundProperty, brush);
    }

    private void Shake(FrameworkElement el)
    {
        if (S.ReduceMotion || !SystemParameters.ClientAreaAnimation) return;
        var tt = new TranslateTransform();
        el.RenderTransform = tt;
        var a = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(300) };
        foreach (var (t, x) in new[] { (0, 0.0), (60, -8.0), (120, 8.0), (180, -5.0), (240, 5.0), (300, 0.0) })
            a.KeyFrames.Add(new LinearDoubleKeyFrame(x, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(t))));
        tt.BeginAnimation(TranslateTransform.XProperty, a);
    }

    // =================================================================== utilita'
    private static bool TrySetClipboard(string text)
    {
        for (int i = 0; i < 5; i++)
        {
            try { Clipboard.SetText(text); return true; }
            catch { Thread.Sleep(60); }
        }
        return false;
    }

    private static void OpenInExplorer(string folder, string? select)
    {
        try
        {
            string args = select != null && File.Exists(select) ? $"/select,\"{select}\"" : $"\"{folder}\"";
            Process.Start(new ProcessStartInfo("explorer.exe", args) { UseShellExecute = true });
        }
        catch (Exception ex) { Logger.Warn("Impossibile aprire Esplora file: " + ex.Message); }
    }

    // =================================================================== Informazioni
    private void BtnAbout_Click(object sender, RoutedEventArgs e) => ShowAbout();

    private void ShowAbout()
    {
        var w = new AboutWindow { Owner = this };
        w.ShowDialog();
    }
}
