using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace FileDedupe.Converters;

/// <summary>Inverts a boolean. Used to disable buttons while work is running.</summary>
public sealed partial class BoolNegationConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is not bool b || !b;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        value is not bool b || !b;
}

/// <summary>
/// Maps true to Visible. WinUI's x:Bind does not convert bool to Visibility implicitly,
/// so this is required rather than optional.
/// </summary>
public sealed partial class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is bool b && b ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        value is Visibility v && v == Visibility.Visible;
}
