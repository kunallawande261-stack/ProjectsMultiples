using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace FolderComparerUI.Converters
{
    /// <summary>Converts a colour name string to a SolidColorBrush for BC status label.</summary>
    [ValueConversion(typeof(string), typeof(Brush))]
    public class StringToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(value as string ?? "Gray");
                return new SolidColorBrush(color);
            }
            catch { return Brushes.Gray; }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
