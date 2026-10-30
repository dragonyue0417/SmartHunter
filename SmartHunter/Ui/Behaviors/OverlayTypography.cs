using System.Windows;

namespace SmartHunter.Ui.Behaviors
{
    // Inherits through item templates, where the DataContext is a monster, part
    // or status rather than the owning widget. No layout scale/width is changed.
    public static class OverlayTypography
    {
        public static readonly DependencyProperty FontSizeProperty = DependencyProperty.RegisterAttached(
            "FontSize", typeof(double), typeof(OverlayTypography),
            new FrameworkPropertyMetadata(16d, FrameworkPropertyMetadataOptions.Inherits));
        public static double GetFontSize(DependencyObject target) => (double)target.GetValue(FontSizeProperty);
        public static void SetFontSize(DependencyObject target, double value) => target.SetValue(FontSizeProperty, value);
    }
}
