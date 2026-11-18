/*
 * VHD ISO Studio
 * ------------------------------------------------------------------------------------------
 * Sinossi: monta un VHD/VHDX come disco, ne cattura il contenuto (diretto, copia robocopy o
 * immagine WIM con DISM) e crea un'ISO con oscdimg.exe esponendo tutti i suoi parametri.
 * Include una modalita' completamente automatica. Interfaccia WPF IT/EN, 3 temi + alto contrasto.
 * Requisiti: Windows 10 1809+/11 x64 o ARM64, account amministratore (locale o di dominio).
 * Dipendenze: oscdimg.exe (Windows ADK - Deployment Tools), installato dall'app se manca.
 * Build: build.ps1 (locale) oppure GitHub Actions (.github/workflows/build.yml).
 * Argomenti: --selftest (verifica risorse ed esce 0), --auto <file.vhdx> [cartella output],
 *            <file.vhd|vhdx> (precompila la sorgente).
 *
 * Changelog
 * 1.1.0 (2026-11-18) - Completamente portabile: Data\ accanto all'exe (impostazioni, log, download,
 *                      lavoro), oscdimg + file di boot in tools\oscdimg senza installare l'ADK
 *                      (layout offline + msiexec /a), build ZIP portabile senza estrazioni in %TEMP%.
 * 1.0.0 (2026-11-17) - Prima versione: montaggio VHD/VHDX, cattura diretta/robocopy/DISM,
 *                      tutti i parametri oscdimg, modalita' automatica, installazione ADK
 *                      Deployment Tools con verifica firma, IT/EN, temi, accessibilita'.
 */
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using VhdIsoStudio.Core;

namespace VhdIsoStudio;

public partial class App : Application
{
    private static Mutex? _mutex;
    private static EventWaitHandle? _activateEvent;
    private const string MutexName = @"Global\VhdIsoStudio.SingleInstance";
    private const string EventName = @"Global\VhdIsoStudio.Activate";

    public static AppSettings Settings { get; private set; } = new();
    public static string[] StartupArgs { get; private set; } = Array.Empty<string>();

    protected override void OnStartup(StartupEventArgs e)
    {
        StartupArgs = e.Args;
        Logger.Init();
        Logger.Info($"==== {AppInfo.Name} {AppInfo.Version} avvio — {Environment.OSVersion} — {System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}");
        Logger.Info($"Modalita' portabile: {(AppInfo.IsPortable ? "si'" : "no")} — dati: {AppInfo.MachineDataDir}");

        // gestori globali: nessun crash silenzioso
        DispatcherUnhandledException += OnDispatcherException;
        AppDomain.CurrentDomain.UnhandledException += (_, a) => Fatal(a.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, a) => { Logger.Error("Task non osservato", a.Exception); a.SetObserved(); };

        if (e.Args.Contains("--selftest", StringComparer.OrdinalIgnoreCase))
        {
            Shutdown(SelfTest());
            return;
        }

        Settings = AppSettings.Load();
        Loc.Instance.SetLanguage(Settings.Language);

        try { NativeMethods.SetCurrentProcessExplicitAppUserModelID(AppInfo.AppUserModelId); } catch { }

        // solo amministratori (locali o di dominio) con token elevato
        var admin = AdminGuard.Check();
        Logger.Info($"Utente: {admin.User} — admin elevato: {admin.IsElevatedAdmin} — admin dominio: {admin.IsDomainAdmin}");
        if (!admin.IsElevatedAdmin)
        {
            MessageBox.Show(Loc.T("ErrNotAdmin"), AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Stop);
            Shutdown(5);
            return;
        }

        // istanza singola: la seconda istanza riattiva la prima
        _mutex = new Mutex(true, MutexName, out bool created);
        if (!created)
        {
            try
            {
                if (EventWaitHandle.TryOpenExisting(EventName, out var ev)) { ev.Set(); ev.Dispose(); }
            }
            catch { }
            Logger.Info("Istanza gia' in esecuzione: attivata quella esistente.");
            Shutdown(0);
            return;
        }
        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
        var t = new Thread(() =>
        {
            while (_activateEvent.WaitOne())
                Dispatcher.InvokeAsync(() => (MainWindow as MainWindow)?.BringToFront());
        }) { IsBackground = true, Name = "ActivateListener" };
        t.Start();

        ThemeManager.Apply(Settings.Theme);
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += (_, _) =>
            Dispatcher.InvokeAsync(() => ThemeManager.Apply(ThemeManager.Requested));

        base.OnStartup(e);
        var w = new MainWindow();
        MainWindow = w;
        w.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { DiskImageService.DismountAllOwned(); } catch { }
        Settings.Save();
        Logger.Info($"==== chiusura (codice {e.ApplicationExitCode})");
        try { _mutex?.ReleaseMutex(); } catch { }
        base.OnExit(e);
    }

    private static int SelfTest()
    {
        try
        {
            var ico = GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"));
            if (ico == null) return 2;
            foreach (var t in new[] { "Dark", "Light", "Medium", "Styles" })
                _ = new ResourceDictionary { Source = new Uri($"pack://application:,,,/Themes/{t}.xaml") };
            var o = OscdimgOptions.WindowsBootPreset();
            o.BiosBootFile = @"C:\b\etfsboot.com"; o.UefiBootFile = @"C:\b\efisys.bin";
            string args = o.BuildArguments(@"C:\src", @"C:\out\x.iso");
            if (!args.Contains("-bootdata:2#p0,e,b\"C:\\b\\etfsboot.com\"#pEF,e,b\"C:\\b\\efisys.bin\"")) return 3;
            foreach (var k in Strings.Table) if (string.IsNullOrEmpty(k.Value.It) || string.IsNullOrEmpty(k.Value.En)) return 4;
            Logger.Info("Selftest OK");
            return 0;
        }
        catch (Exception ex)
        {
            Logger.Error("Selftest fallito", ex);
            return 1;
        }
    }

    private void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        Logger.Error("Eccezione non gestita (UI)", e.Exception);
        string msg = e.Exception is UserFacingException u ? u.Localized : Loc.F("ErrUnexpected", e.Exception.Message, Logger.CurrentFile);
        MessageBox.Show(msg, AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private static void Fatal(Exception? ex)
    {
        Logger.Error("Errore fatale", ex ?? new Exception("sconosciuto"));
        try { DiskImageService.DismountAllOwned(); } catch { }
        try
        {
            MessageBox.Show(Loc.F("ErrUnexpected", ex?.Message ?? "?", Logger.CurrentFile), AppInfo.Name,
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch { }
    }
}
