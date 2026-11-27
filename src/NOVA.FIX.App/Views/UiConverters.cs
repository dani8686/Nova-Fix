
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace NOVA.FIX.App.Views;

/// <summary>Static converter instances referenced from XAML via x:Static.</summary>
public static class UiConverters
{
    public static readonly IValueConverter BoolToVisibility = new BoolToVis();
    public static readonly IValueConverter InverseBool = new InvBool();

    private sealed class BoolToVis : IValueConverter
    {
        public object Convert(object? v, Type t, object? p, CultureInfo c)
            => v is true ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object? v, Type t, object? p, CultureInfo c)
            => v is Visibility.Visible;
    }

    private sealed class InvBool : IValueConverter
    {
        public object Convert(object? v, Type t, object? p, CultureInfo c)
            => v is not true;

        public object ConvertBack(object? v, Type t, object? p, CultureInfo c)
            => v is not true;
    }
}
