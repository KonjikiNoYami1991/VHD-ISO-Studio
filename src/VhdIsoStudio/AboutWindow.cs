using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VhdIsoStudio.Core;

namespace VhdIsoStudio;

/// <summary>Finestra "Informazioni": versione, changelog, log, scorciatoie, opzioni di accessibilita'.</summary>
public sealed class AboutWindow : Window
{
    public AboutWindow()
    {
        // lo stile implicito di Window non si applica alle sottoclassi: lo imposto esplicitamente
        SetResourceReference(StyleProperty, typeof(Window));
        Title = Loc.Plain("AboutTitle");
        Width = 640; Height = 680; MinWidth = 480; MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        try { Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/app.ico")); } catch { }
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);
        KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) Close(); };

        var root = new DockPanel { Margin = new Thickness(24) };

        var close = new Button { Content = Loc.T("BtnClose"), IsDefault = true, IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        close.Click += (_, _) => Close();
        DockPanel.SetDock(close, Dock.Bottom);
        root.Children.Add(close);

        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 16) };
        head.Children.Add(new Image
        {
            Source = new BitmapImage(new Uri("pack://application:,,,/Assets/app.png")),
            Width = 64, Height = 64, Margin = new Thickness(0, 0, 16, 0)
        });
        var ht = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        ht.Children.Add(Text(AppInfo.Name, 22, FontWeights.SemiBold));
        ht.Children.Add(Muted($"{Loc.T("Version")} {AppInfo.Version} — {AppInfo.BuildDate} — .NET {Environment.Version}"));
        ht.Children.Add(Muted(Loc.T("Subtitle")));
        head.Children.Add(ht);
        DockPanel.SetDock(head, Dock.Top);
        root.Children.Add(head);

        var body = new StackPanel();

        body.Children.Add(Text(Loc.T("AboutShortcuts"), 15, FontWeights.SemiBold, top: 4));
        body.Children.Add(Mono(Loc.T("ShortcutsList")));

        body.Children.Add(Text(Loc.T("AboutAccessibility"), 15, FontWeights.SemiBold, top: 14));
        var rm = new CheckBox { Content = Loc.T("ChkReduceMotion"), IsChecked = App.Settings.ReduceMotion, ToolTip = Loc.T("TipReduceMotion") };
        rm.Checked += (_, _) => { App.Settings.ReduceMotion = true; App.Settings.Save(); };
        rm.Unchecked += (_, _) => { App.Settings.ReduceMotion = false; App.Settings.Save(); };
        body.Children.Add(rm);
        body.Children.Add(Muted(Loc.T("AboutA11yText")));

        body.Children.Add(Text(Loc.T("AboutPaths"), 15, FontWeights.SemiBold, top: 14));
        body.Children.Add(Muted(Loc.T(AppInfo.IsPortable ? "AboutPortableOn" : "AboutPortableOff")));
        body.Children.Add(Mono($"Log:      {Logger.CurrentFile}\nSettings: {AppSettings.FilePath}\nTools:    {AppInfo.ToolsDir}\nExe:      {AppInfo.ExePath}"));
        var openLog = new Button { Content = Loc.T("BtnOpenLogFolder"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
        openLog.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Path.GetDirectoryName(Logger.CurrentFile)}\"") { UseShellExecute = true }); } catch { }
        };
        body.Children.Add(openLog);

        body.Children.Add(Text(Loc.T("AboutChangelog"), 15, FontWeights.SemiBold, top: 14));
        body.Children.Add(Mono(Loc.T("ChangelogText")));

        body.Children.Add(Text(Loc.T("AboutLicense"), 15, FontWeights.SemiBold, top: 14));
        body.Children.Add(Muted(Loc.T("LicenseText")));

        root.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false });
        Content = root;
    }

    private static TextBlock Text(string s, double size, FontWeight w, double top = 0)
    {
        var t = new TextBlock { Text = s, FontSize = size, FontWeight = w, Margin = new Thickness(0, top, 0, 6), TextWrapping = TextWrapping.Wrap };
        t.SetResourceReference(TextBlock.ForegroundProperty, "Fg");
        return t;
    }

    private static TextBlock Muted(string s)
    {
        var t = new TextBlock { Text = s, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2) };
        t.SetResourceReference(TextBlock.ForegroundProperty, "FgMuted");
        return t;
    }

    private static TextBox Mono(string s)
    {
        var t = new TextBox { Text = s, AcceptsReturn = true };
        t.SetResourceReference(StyleProperty, "Mono");
        return t;
    }
}
