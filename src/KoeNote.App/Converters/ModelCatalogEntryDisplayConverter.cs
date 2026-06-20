using System.Globalization;
using System.Windows.Data;
using KoeNote.App.Services.Models;

namespace KoeNote.App.Converters;

public sealed class ModelCatalogEntryDisplayConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            ModelCatalogEntry entry => entry.SetupDisplayName,
            null => string.Empty,
            _ => value.ToString() ?? string.Empty
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}
