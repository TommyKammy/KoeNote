using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Data;
using System.Windows.Media;

namespace KoeNote.App.Converters;

public sealed partial class SpeakerChipBrushConverter : IValueConverter
{
    private const string All = "\u5168\u54e1";
    private const string AllSpeakers = "\u5168\u8a71\u8005";

    private static readonly SpeakerChipBrush Neutral = new("#F3F4F6", "#E5E7EB", "#374151");

    private static readonly SpeakerChipBrush[] Palette =
    [
        new("#ECFDF5", "#A7F3D0", "#047857"),
        new("#EFF6FF", "#BFDBFE", "#1D4ED8"),
        new("#FFF7ED", "#FED7AA", "#C2410C"),
        new("#FDF2F8", "#FBCFE8", "#BE185D"),
        new("#F5F3FF", "#DDD6FE", "#6D28D9"),
        new("#FEFCE8", "#FEF08A", "#A16207"),
        new("#ECFEFF", "#A5F3FC", "#0E7490"),
        new("#F0FDFA", "#99F6E4", "#0F766E"),
        new("#F7FEE7", "#D9F99D", "#4D7C0F"),
        new("#FEEFEE", "#FECACA", "#B91C1C"),
        new("#F8FAFC", "#CBD5E1", "#334155"),
        new("#FAF5FF", "#E9D5FF", "#7E22CE")
    ];

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var speaker = value?.ToString() ?? string.Empty;
        var chip = GetChipBrush(speaker);
        var role = parameter?.ToString();
        return role switch
        {
            "Border" => chip.Border,
            "Foreground" => chip.Foreground,
            _ => chip.Background
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }

    private static SpeakerChipBrush GetChipBrush(string speaker)
    {
        if (string.IsNullOrWhiteSpace(speaker) ||
            string.Equals(speaker, All, StringComparison.Ordinal) ||
            string.Equals(speaker, AllSpeakers, StringComparison.Ordinal))
        {
            return Neutral;
        }

        var match = SpeakerNumberRegex().Match(speaker);
        var index = match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var speakerIndex)
            ? speakerIndex
            : GetStableHash(speaker);

        return Palette[(index & int.MaxValue) % Palette.Length];
    }

    private static int GetStableHash(string value)
    {
        unchecked
        {
            var hash = 23;
            foreach (var character in value)
            {
                hash = (hash * 31) + character;
            }

            return hash;
        }
    }

    [GeneratedRegex(@"(?:Speaker|\u8a71\u8005)[_\s-]?(\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SpeakerNumberRegex();

    private sealed class SpeakerChipBrush(string background, string border, string foreground)
    {
        public Brush Background { get; } = CreateBrush(background);

        public Brush Border { get; } = CreateBrush(border);

        public Brush Foreground { get; } = CreateBrush(foreground);

        private static SolidColorBrush CreateBrush(string color)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            brush.Freeze();
            return brush;
        }
    }
}
