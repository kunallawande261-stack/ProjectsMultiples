using System;
using System.Globalization;
using System.Windows.Data;

namespace FolderComparerUI.Converters
{
    [ValueConversion(typeof(string), typeof(string))]
    public class CategoryToSymbolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            (value as string) switch
            {
                "Same"         => "=",
                "Different"    => "!=",
                "Left-Orphan"  => "←",
                "Right-Orphan" => "→",
                _              => "?"
            };

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
