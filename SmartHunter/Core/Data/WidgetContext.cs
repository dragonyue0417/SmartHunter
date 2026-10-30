namespace SmartHunter.Core.Data
{
    public abstract class WidgetContext : Bindable
    {
        protected virtual SmartHunter.Core.Config.WidgetConfig FontConfig => null;
        public SmartHunter.Core.Config.WidgetAppearanceConfig Palette => FontConfig?.Appearance?.Copy() ?? new SmartHunter.Core.Config.WidgetAppearanceConfig();
        public int FontSize => System.Math.Max(12, System.Math.Min(24, FontConfig?.FontSize ?? 16));
        public virtual void UpdateFromConfig() { NotifyPropertyChanged(nameof(FontSize)); NotifyPropertyChanged(nameof(Palette)); }
    }
}
