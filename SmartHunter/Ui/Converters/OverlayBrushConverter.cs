using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using SmartHunter.Core.Config;
namespace SmartHunter.Ui.Converters
{
    public static class OverlayColors
    {
        public static bool TryParse(string text,out Color color)
        {
            color=Colors.Transparent;
            if(String.IsNullOrWhiteSpace(text)) return false;
            text=text.Trim();
            if((text.Length!=7 && text.Length!=9)||text[0]!='#') return false;
            uint value;
            if(!UInt32.TryParse(text.Substring(1),NumberStyles.HexNumber,CultureInfo.InvariantCulture,out value)) return false;
            color=Color.FromArgb(text.Length==7?(byte)255:(byte)(value>>24),(byte)(value>>16),(byte)(value>>8),(byte)value);return true;
        }
        public static string Canonical(Color color)=>color.ToString(CultureInfo.InvariantCulture);
        public static SolidColorBrush Brush(Color color){var brush=new SolidColorBrush(color);brush.Freeze();return brush;}
    }
    public sealed class OverlayBrushConverter : IMultiValueConverter
    {
        public object Convert(object[] values,Type targetType,object parameter,CultureInfo culture)
        {
            var palette=values.Length>0?values[0] as WidgetAppearanceConfig:null;
            int state=values.Length>1 && values[1] is int id?id:-1;
            Color fallback=Colors.White;
            if(values.Length>2) {if(values[2] is Color c) fallback=c;else if(values[2] is SolidColorBrush b) fallback=b.Color;}
            string kind=parameter as string,text=null;
            StatusAppearanceConfig status=null;
            if(state>=0 && palette?.StatusColors!=null) palette.StatusColors.TryGetValue(state.ToString(CultureInfo.InvariantCulture),out status);
            if(kind=="Background") text=palette?.BackgroundColor;
            else if(kind=="StateBar") text=status?.BarColor;
            else text=!String.IsNullOrWhiteSpace(status?.TextColor)?status.TextColor:palette?.TextColor;
            return OverlayColors.Brush(OverlayColors.TryParse(text,out var color)?color:fallback);
        }
        public object[] ConvertBack(object value,Type[] targetTypes,object parameter,CultureInfo culture)=>throw new NotSupportedException();
    }
}
