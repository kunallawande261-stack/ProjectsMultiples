using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FolderComparerUI.Converters
{
    /// <summary>Converts a string "Visible"/"Collapsed"/"Hidden" to Visibility.</summary>
    [ValueConversion(typeof(string), typeof(Visibility))]
    public class StringToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            (value as string) switch
            {
                "Visible"  => Visibility.Visible,
                "Hidden"   => Visibility.Hidden,
                _          => Visibility.Collapsed
            };

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is Visibility v ? v.ToString() : "Collapsed";
    }
}
