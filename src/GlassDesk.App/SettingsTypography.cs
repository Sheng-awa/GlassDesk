using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using GlassDesk.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Windows.UI.Text;
using Windows.UI.ViewManagement;

namespace GlassDesk.App;

/// <summary>
/// Applies the shared SystemA catalog to existing settings text. This adapter owns
/// text properties only: it never changes a control's bounds, spacing or template.
/// </summary>
internal static class SettingsTypography
{
    static readonly AccessibilitySettings Accessibility = new();
    static readonly UISettings SystemColors = new();
    static readonly ConditionalWeakTable<TextBlock, TextState> TextStates = new();

    public static TextBlock Apply(TextBlock text, TextRole role, InkRole ink, TextFlags? numericFlags = null)
    {
        if (numericFlags is not (null or TextFlags.None or TextFlags.TabularNumbers or TextFlags.TabularDigitsOnly))
            throw new ArgumentOutOfRangeException(nameof(numericFlags));
        var state = TextStates.GetValue(text, static element => new TextState(element));
        state.Role = role;
        state.Ink = ink;
        state.Selected = false;
        state.NumericFlags = numericFlags;
        state.Present(text);
        return text;
    }

    public static T Apply<T>(T control, TextRole role, InkRole ink) where T : Control
    {
        var token = TypographyCatalog.Get(role);
        control.FontFamily = Family(token.Optical, CultureInfo.CurrentUICulture.Name);
        control.FontSize = token.FontSizeDip;
        // Editable/template text can acquire any script after construction. Use a
        // real regular face rather than allow non-Latin fallback to synthesize 500.
        control.FontWeight = Weight(400);
        control.CharacterSpacing = 0;
        control.Foreground = Ink(ink);
        return control;
    }

    public static void ApplyNavigation(Button button, bool selected)
    {
        Apply(button, TextRole.NavigationSettings, InkRole.Primary);
        // Retain the Button itself, its automation name, focus and hit region.
        // Only its string presenter becomes a typed text presenter.
        if (button.Content is string title)
            button.Content = new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center };
        if (button.Content is not TextBlock label) return;
        var state = TextStates.GetValue(label, static element => new TextState(element));
        state.Role = TextRole.NavigationSettings;
        state.Ink = InkRole.Primary;
        state.Selected = selected;
        state.NumericFlags = null;
        state.Present(label);
    }

    public static void ApplyInfoBar(InfoBar infoBar)
    {
        Apply(infoBar, TextRole.Body, InkRole.Secondary);
        var title = TypographyCatalog.Get(TextRole.Label);
        var body = TypographyCatalog.Get(TextRole.Body);
        // The framework template keeps its severity icons, close affordance and
        // bindings. Its text resource keys override the default semibold request,
        // which could synthesize a CJK face. No template is replaced or traversed.
        infoBar.Resources["InfoBarTitleFontSize"] = (double)title.FontSizeDip;
        infoBar.Resources["InfoBarMessageFontSize"] = (double)body.FontSizeDip;
        infoBar.Resources["InfoBarTitleFontWeight"] = Weight(400);
        infoBar.Resources["InfoBarMessageFontWeight"] = Weight(400);
        infoBar.Resources["InfoBarTitleForeground"] = Ink(InkRole.Primary);
        infoBar.Resources["InfoBarMessageForeground"] = Ink(InkRole.Secondary);
        var textStyle = new Style(typeof(TextBlock));
        textStyle.Setters.Add(new Setter(TextBlock.LineHeightProperty, (double)body.LineHeightDip));
        textStyle.Setters.Add(new Setter(TextBlock.LineStackingStrategyProperty, LineStackingStrategy.BlockLineHeight));
        infoBar.Resources[typeof(TextBlock)] = textStyle;
    }

    public static SolidColorBrush Ink(InkRole role)
    {
        if (Accessibility.HighContrast)
            return new(SystemColors.UIElementColor(role == InkRole.Disabled ? UIElementType.GrayText : UIElementType.WindowText));
        uint argb = TypographyCatalog.Ink(role);
        return new(Color.FromArgb(255, (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
    }

    static FontWeight Weight(int value) => new() { Weight = checked((ushort)value) };

    static FontFamily Family(OpticalRole optical, string locale)
    {
        string cjk = locale.StartsWith("ja", StringComparison.OrdinalIgnoreCase) ? "Yu Gothic UI, Meiryo UI" :
            locale.StartsWith("ko", StringComparison.OrdinalIgnoreCase) ? "Malgun Gothic" :
            locale.StartsWith("zh-TW", StringComparison.OrdinalIgnoreCase) || locale.StartsWith("zh-HK", StringComparison.OrdinalIgnoreCase)
                || locale.StartsWith("zh-Hant", StringComparison.OrdinalIgnoreCase) ? "Microsoft JhengHei UI" : "Microsoft YaHei UI";
        // XAML accepts an ordered family list. System fallback remains the final
        // authority for scripts these installed Windows families do not contain.
        return new(string.Join(", ", FontResolutionPolicy.Families(optical)) + ", " + cjk + ", Segoe UI Emoji");
    }

    static string Locale(string value)
    {
        bool kana = false, hangul = false;
        foreach (var rune in value.EnumerateRunes())
        {
            kana |= rune.Value is >= 0x3040 and <= 0x30ff;
            hangul |= rune.Value is >= 0x1100 and <= 0x11ff or >= 0x3130 and <= 0x318f or >= 0xac00 and <= 0xd7af;
        }
        if (kana && !hangul) return "ja-JP";
        if (hangul && !kana) return "ko-KR";
        return CultureInfo.CurrentUICulture.Name;
    }

    static bool LatinCluster(string cluster)
    {
        // Work on text elements, never UTF-16 code units. Combining accents stay
        // with their base and emoji ZWJ/keycap/variation sequences stay regular.
        foreach (var rune in cluster.EnumerateRunes())
        {
            if (rune.Value is 0x200d or 0x20e3 or 0xfe0e or 0xfe0f) return false;
            if (Rune.GetUnicodeCategory(rune) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark)
                continue;
            if (rune.Value > 0x024f && rune.Value is not (>= 0x2000 and <= 0x206f)) return false;
        }
        return true;
    }

    static bool DecimalCluster(string cluster) => cluster.EnumerateRunes().Any(rune => Rune.GetUnicodeCategory(rune) == UnicodeCategory.DecimalDigitNumber);

    sealed class TextState
    {
        public TextRole Role;
        public InkRole Ink;
        public bool Selected;
        public TextFlags? NumericFlags;
        string _source;
        bool _presenting;

        public TextState(TextBlock element)
        {
            _source = element.Text;
            element.RegisterPropertyChangedCallback(TextBlock.TextProperty, (sender, _) =>
            {
                if (_presenting) return;
                var block = (TextBlock)sender;
                _source = block.Text;
                Present(block);
            });
        }

        public void Present(TextBlock block)
        {
            if (_presenting) return;
            _presenting = true;
            try
            {
                var token = TypographyCatalog.Get(Role);
                TextFlags flags = NumericFlags ?? token.Flags;
                string locale = Locale(_source);
                int latinWeight = Role == TextRole.NavigationSettings && Selected ? 600 : token.LatinWeight;
                var family = Family(token.Optical, locale);
                block.FontFamily = family;
                block.FontSize = token.FontSizeDip;
                block.FontWeight = Weight(400);
                block.LineHeight = token.LineHeightDip;
                block.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
                block.Language = locale;
                block.Foreground = SettingsTypography.Ink(Ink);
                block.CharacterSpacing = flags == TextFlags.None
                    ? checked((int)Math.Round(FontResolutionPolicy.Tracking(token, _source) * 1000)) : 0;
                Typography.SetNumeralAlignment(block, flags.HasFlag(TextFlags.TabularNumbers) ? FontNumeralAlignment.Tabular : FontNumeralAlignment.Normal);

                var pieces = new List<(string Text, int Weight, bool Tabular)>();
                var text = new StringBuilder();
                int currentWeight = 400;
                bool currentTabular = false;
                var elements = StringInfo.GetTextElementEnumerator(_source);
                while (elements.MoveNext())
                {
                    string cluster = elements.GetTextElement();
                    int weight = LatinCluster(cluster) ? latinWeight : 400;
                    bool tabular = flags.HasFlag(TextFlags.TabularNumbers)
                        || flags.HasFlag(TextFlags.TabularDigitsOnly) && DecimalCluster(cluster);
                    if (text.Length > 0 && (currentWeight != weight || currentTabular != tabular))
                    {
                        pieces.Add((text.ToString(), currentWeight, currentTabular));
                        text.Clear();
                    }
                    currentWeight = weight;
                    currentTabular = tabular;
                    text.Append(cluster);
                }
                if (text.Length > 0) pieces.Add((text.ToString(), currentWeight, currentTabular));

                // Most settings text is regular and retains XAML's simple text
                // path. Runs are needed only when weights/numeral features vary.
                if (pieces.Count <= 1)
                {
                    block.Inlines.Clear();
                    block.Text = _source;
                    block.FontWeight = Weight(pieces.Count == 0 ? 400 : pieces[0].Weight);
                    if (pieces.Count == 1 && pieces[0].Tabular) Typography.SetNumeralAlignment(block, FontNumeralAlignment.Tabular);
                    return;
                }

                block.Inlines.Clear();
                foreach (var piece in pieces)
                {
                    var run = new Run { Text = piece.Text, FontFamily = family, FontWeight = Weight(piece.Weight), Language = locale, CharacterSpacing = 0 };
                    Typography.SetNumeralAlignment(run, piece.Tabular ? FontNumeralAlignment.Tabular : FontNumeralAlignment.Normal);
                    block.Inlines.Add(run);
                }
            }
            finally { _presenting = false; }
        }
    }
}
