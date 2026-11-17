using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace VhdIsoStudio.Core;

/// <summary>Temi Scuro / Chiaro / Intermedio / Sistema, piu' Alto contrasto di Windows rispettato automaticamente.</summary>
public static class ThemeManager
{
    public static string Current { get; private set; } = "Dark";
    public static string Requested { get; private set; } = "System";
    public static event Action? ThemeChanged;

    public static bool SystemUsesLight()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("AppsUseLightTheme") is int v && v == 1;
        }
        catch { return false; }
    }

    public static void Apply(string requested)
    {
        Requested = requested;
        string effective = requested switch
        {
            "Dark" or "Light" or "Medium" => requested,
            _ => SystemUsesLight() ? "Light" : "Dark"
        };
        if (SystemParameters.HighContrast) effective = "HighContrast";
        Current = effective;

        ResourceDictionary dict = effective == "HighContrast"
            ? BuildHighContrast()
            : new ResourceDictionary { Source = new Uri($"pack://application:,,,/Themes/{effective}.xaml", UriKind.Absolute) };

        var merged = Application.Current.Resources.MergedDictionaries;
        // indice 0 = palette, indice 1 = stili
        if (merged.Count > 0) merged[0] = dict; else merged.Insert(0, dict);

        foreach (Window w in Application.Current.Windows) ApplyTitleBar(w);
        ThemeChanged?.Invoke();
        Logger.Debug("Tema applicato: " + effective);
    }

    public static bool IsDark => Current is "Dark" or "Medium" ||
                                 (Current == "HighContrast" && Luma(SystemColors.WindowColor) < 0.5);

    private static double Luma(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;

    private static ResourceDictionary BuildHighContrast()
    {
        SolidColorBrush B(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
        var d = new ResourceDictionary
        {
            ["C.Bg"] = SystemColors.WindowColor,
            ["C.Accent"] = SystemColors.HighlightColor,
            ["Bg"] = B(SystemColors.WindowColor),
            ["Surface"] = B(SystemColors.WindowColor),
            ["SurfaceAlt"] = B(SystemColors.ControlColor),
            ["Hover"] = B(SystemColors.HighlightColor),
            ["Line"] = B(SystemColors.WindowTextColor),
            ["Fg"] = B(SystemColors.WindowTextColor),
            ["FgMuted"] = B(SystemColors.WindowTextColor),
            ["Accent"] = B(SystemColors.HighlightColor),
            ["AccentHover"] = B(SystemColors.HotTrackColor),
            ["OnAccent"] = B(SystemColors.HighlightTextColor),
            ["Ok"] = B(SystemColors.WindowTextColor),
            ["Warn"] = B(SystemColors.WindowTextColor),
            ["Err"] = B(SystemColors.WindowTextColor),
            ["Focus"] = B(SystemColors.HighlightColor),
            ["InputBg"] = B(SystemColors.WindowColor),
            ["Selection"] = B(SystemColors.HighlightColor),
            ["AccentSoft"] = B(SystemColors.ControlColor),
            ["HeaderBrush"] = B(SystemColors.ActiveCaptionColor)
        };
        return d;
    }

    /// <summary>Colora la barra del titolo nativa (dark mode immersiva + colore didascalia su Windows 11).</summary>
    public static void ApplyTitleBar(Window w)
    {
        try
        {
            IntPtr hwnd = new WindowInteropHelper(w).Handle;
            if (hwnd == IntPtr.Zero) return;
            int dark = IsDark ? 1 : 0;
            // 20 = DWMWA_USE_IMMERSIVE_DARK_MODE (Win10 20H1+/Win11), 19 su build precedenti
            if (NativeMethods.DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int)) != 0)
                NativeMethods.DwmSetWindowAttribute(hwnd, 19, ref dark, sizeof(int));

            if (Application.Current.Resources["C.Bg"] is Color bg && Current != "HighContrast")
            {
                int colorRef = bg.R | (bg.G << 8) | (bg.B << 16);           // COLORREF 0x00BBGGRR
                NativeMethods.DwmSetWindowAttribute(hwnd, 35, ref colorRef, sizeof(int)); // DWMWA_CAPTION_COLOR (Win11)
                int text = IsDark ? 0x00F0EAE8 : 0x00271811;
                NativeMethods.DwmSetWindowAttribute(hwnd, 36, ref text, sizeof(int));     // DWMWA_TEXT_COLOR (Win11)
            }
        }
        catch { /* versioni di Windows senza supporto: si ignora */ }
    }
}
