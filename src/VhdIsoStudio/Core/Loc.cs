using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;

namespace VhdIsoStudio.Core;

/// <summary>
/// Localizzazione IT/EN con cambio lingua a caldo: la UI si lega all'indicizzatore
/// (Binding Path=[Chiave]) e riceve PropertyChanged("Item[]") quando la lingua cambia.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static Loc Instance { get; } = new();
    public event PropertyChangedEventHandler? PropertyChanged;
    public static event Action? LanguageChanged;

    private string _lang = "en";
    public string Language => _lang;

    public static string Detect() =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("it", StringComparison.OrdinalIgnoreCase) ? "it" : "en";

    public void SetLanguage(string? lang)
    {
        _lang = string.IsNullOrWhiteSpace(lang) ? Detect() : (lang == "it" ? "it" : "en");
        var ci = new CultureInfo(_lang == "it" ? "it-IT" : "en-US");
        Thread.CurrentThread.CurrentUICulture = ci;
        CultureInfo.DefaultThreadCurrentUICulture = ci;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        LanguageChanged?.Invoke();
    }

    public string this[string key] => Get(key);

    public string Get(string key)
    {
        if (Strings.Table.TryGetValue(key, out var v)) return _lang == "it" ? v.It : v.En;
        return "⟨" + key + "⟩";
    }

    public static string T(string key) => Instance.Get(key);

    /// <summary>Testo senza indicatori di tasto di accesso ("_"), per titoli e nomi UI Automation.</summary>
    public static string Plain(string key) => StripAccessKey(Instance.Get(key));

    public static string StripAccessKey(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '_')
            {
                if (i + 1 < s.Length && s[i + 1] == '_') { sb.Append('_'); i++; }
                continue;
            }
            sb.Append(s[i]);
        }
        return sb.ToString();
    }
    public static string F(string key, params object[] args) => string.Format(CultureInfo.CurrentCulture, Instance.Get(key), args);
}

/// <summary>Markup extension XAML: <c>Content="{l:Tr Chiave}"</c>.</summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class TrExtension : MarkupExtension
{
    public string Key { get; set; } = "";
    /// <summary>true = rimuove gli underscore dei tasti di accesso (per AutomationProperties.Name, titoli).</summary>
    public bool Plain { get; set; }
    public TrExtension() { }
    public TrExtension(string key) { Key = key; }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var b = new Binding("[" + Key + "]") { Source = Loc.Instance, Mode = BindingMode.OneWay };
        if (Plain) b.Converter = StripConverter.Instance;
        return b.ProvideValue(serviceProvider);
    }
}

internal sealed class StripConverter : IValueConverter
{
    public static readonly StripConverter Instance = new();
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string s ? Loc.StripAccessKey(s) : value;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
