namespace SmartHunter.Core.Config
{
    public class WidgetConfig
    {
        public bool IsVisible = true;
        public float X;
        public float Y;
        public float Scale = 1f;
        public int FontSize = 16;
        public WidgetAppearanceConfig Appearance = new WidgetAppearanceConfig();

        public WidgetConfig(float x, float y)
        {
            X = x;
            Y = y;
        }
    }
}
