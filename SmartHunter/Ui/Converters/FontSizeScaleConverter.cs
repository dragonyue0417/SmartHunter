using System;
using System.Globalization;
using System.Windows.Data;

namespace SmartHunter.Ui.Converters
{
    public sealed class FontSizeScaleConverter : IValueConverter
    {
        public object Convert(object value, Type type, object parameter, CultureInfo culture)
        {
            double size = System.Convert.ToDouble(value, CultureInfo.InvariantCulture);
            double factor = parameter == null ? 1 : Double.Parse(parameter.ToString(), CultureInfo.InvariantCulture);
            return size * factor;
        }
        public object ConvertBack(object value, Type type, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
