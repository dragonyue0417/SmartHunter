using System.Windows;
using SmartHunter.Core.Config;
namespace SmartHunter.Ui.Behaviors
{
    public static class OverlayAppearance
    {
        public static readonly DependencyProperty PaletteProperty=DependencyProperty.RegisterAttached("Palette",typeof(WidgetAppearanceConfig),typeof(OverlayAppearance),new FrameworkPropertyMetadata(null,FrameworkPropertyMetadataOptions.Inherits));
        public static WidgetAppearanceConfig GetPalette(DependencyObject value)=>(WidgetAppearanceConfig)value.GetValue(PaletteProperty);
        public static void SetPalette(DependencyObject value,WidgetAppearanceConfig palette)=>value.SetValue(PaletteProperty,palette);
        public static readonly DependencyProperty StateIdProperty=DependencyProperty.RegisterAttached("StateId",typeof(int),typeof(OverlayAppearance),new FrameworkPropertyMetadata(-1,FrameworkPropertyMetadataOptions.Inherits));
        public static int GetStateId(DependencyObject value)=>(int)value.GetValue(StateIdProperty);
        public static void SetStateId(DependencyObject value,int state)=>value.SetValue(StateIdProperty,state);
    }
}
