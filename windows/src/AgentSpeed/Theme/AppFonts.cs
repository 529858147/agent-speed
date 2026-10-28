using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using AgentSpeed.Presentation.Localization;

namespace AgentSpeed.Theme;

/// <summary>Typefaces per language (DESIGN section 6).</summary>
internal static class AppFonts
{
    public const string UiFontKey = "AppFontFamily";
    public const string DisplayFontKey = "AppDisplayFontFamily";
    public const string SymbolFontKey = "AppSymbolFontFamily";

    private static readonly string[] MonospaceCandidates = ["Cascadia Mono", "Consolas", "Courier New"];
    private static FontFamily? _monospace;

    public static FontFamily Symbols { get; } = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    /// <summary>
    /// Monospaced face (device fingerprint, saved API key, request preview): Cascadia Mono, Consolas, Courier New, without any
    /// family WPF cannot open. Windows Terminal from the Store shares its Cascadia fonts with the system (files under
    /// <c>WindowsApps</c>), so DirectWrite lists them; on some computers opening them fails with an access error, for instance
    /// while the font cache still lists the files of a replaced terminal version. WPF then throws
    /// <see cref="UnauthorizedAccessException"/> as soon as text is formatted with the family, instead of moving on to the next
    /// family of the list. Resolved on first use.
    /// </summary>
    public static FontFamily Monospace => _monospace ??= ResolveMonospace();

    public static FontFamily Ui(string languageCode) => new($"Segoe UI Variable Text, Segoe UI, {CjkFallback(languageCode)}");

    public static FontFamily Display(string languageCode) =>
        new($"Segoe UI Variable Display, Segoe UI, {CjkFallback(languageCode)}");

    /// <summary>Numeric display face: Segoe UI digits are tabular by default.</summary>
    public static FontFamily Numeric { get; } = new("Segoe UI Variable Display, Segoe UI");

    public static XmlLanguage XmlLanguageFor(string languageCode) =>
        XmlLanguage.GetLanguage(LanguageResolver.CultureFor(languageCode).IetfLanguageTag);

    public static void Apply(ResourceDictionary resources, string languageCode)
    {
        resources[UiFontKey] = Ui(languageCode);
        resources[DisplayFontKey] = Display(languageCode);
        resources[SymbolFontKey] = Symbols;
    }

    private static string CjkFallback(string languageCode) => languageCode switch
    {
        LanguageCodes.ZhHans => "Microsoft YaHei UI, Microsoft JhengHei UI, Yu Gothic UI",
        LanguageCodes.Ja => "Yu Gothic UI, Meiryo UI, Microsoft JhengHei UI",
        LanguageCodes.En => "Microsoft JhengHei UI, Microsoft YaHei UI, Yu Gothic UI",
        _ => "Microsoft JhengHei UI, Microsoft YaHei UI, Yu Gothic UI",
    };

    private static FontFamily ResolveMonospace()
    {
        var usable = MonospaceCandidates.Where(CanFormat).ToArray();
        return usable.Length > 0 ? new FontFamily(string.Join(", ", usable)) : SystemFonts.MessageFontFamily;
    }

    /// <summary>
    /// Whether text can be laid out in <paramref name="family"/>: a family that is not installed passes (WPF skips it in a list);
    /// one that is listed but cannot be read does not, whatever WPF throws for it. Laying out a character takes the path that a
    /// text box, a text block or a paragraph takes.
    /// </summary>
    private static bool CanFormat(string family)
    {
        try
        {
            var face = new Typeface(new FontFamily(family), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            _ = new FormattedText("0", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, 12, Brushes.Black, 1.0).Width;
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return false;
        }
    }
}
