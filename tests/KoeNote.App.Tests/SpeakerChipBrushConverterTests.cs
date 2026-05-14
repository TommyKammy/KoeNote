using System.Globalization;
using System.Windows.Media;
using KoeNote.App.Converters;

namespace KoeNote.App.Tests;

public sealed class SpeakerChipBrushConverterTests
{
    [Fact]
    public void Convert_AssignsDeterministicPastelColorsBySpeaker()
    {
        var converter = new SpeakerChipBrushConverter();

        var first = GetColor(converter.Convert("Speaker_0", typeof(Brush), null, CultureInfo.InvariantCulture));
        var firstAgain = GetColor(converter.Convert("Speaker_0", typeof(Brush), null, CultureInfo.InvariantCulture));
        var second = GetColor(converter.Convert("Speaker_1", typeof(Brush), null, CultureInfo.InvariantCulture));
        var all = GetColor(converter.Convert("全話者", typeof(Brush), null, CultureInfo.InvariantCulture));

        Assert.Equal(first, firstAgain);
        Assert.NotEqual(first, second);
        Assert.Equal(Color.FromRgb(0xF3, 0xF4, 0xF6), all);
    }

    [Fact]
    public void Convert_ReturnsMatchingForegroundAndBorderRoles()
    {
        var converter = new SpeakerChipBrushConverter();

        var border = GetColor(converter.Convert("Speaker_0", typeof(Brush), "Border", CultureInfo.InvariantCulture));
        var foreground = GetColor(converter.Convert("Speaker_0", typeof(Brush), "Foreground", CultureInfo.InvariantCulture));

        Assert.Equal(Color.FromRgb(0xA7, 0xF3, 0xD0), border);
        Assert.Equal(Color.FromRgb(0x04, 0x78, 0x57), foreground);
    }

    private static Color GetColor(object value)
    {
        return Assert.IsType<SolidColorBrush>(value).Color;
    }
}
