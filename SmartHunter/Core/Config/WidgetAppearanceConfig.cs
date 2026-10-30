using System.Collections.Generic;
using System.Linq;

namespace SmartHunter.Core.Config
{
    public sealed class StatusAppearanceConfig
    {
        public string BarColor="", TextColor="";
        public StatusAppearanceConfig Copy() => new StatusAppearanceConfig {BarColor=BarColor,TextColor=TextColor};
    }
    public sealed class WidgetAppearanceConfig
    {
        // Empty means the skin's original color, including its alpha value.
        public string BackgroundColor="", TextColor="";
        public Dictionary<string,StatusAppearanceConfig> StatusColors=new Dictionary<string,StatusAppearanceConfig>();
        public WidgetAppearanceConfig Copy() => new WidgetAppearanceConfig {
            BackgroundColor=BackgroundColor,TextColor=TextColor,
            StatusColors=(StatusColors??new Dictionary<string,StatusAppearanceConfig>()).Where(p=>p.Value!=null).ToDictionary(p=>p.Key,p=>p.Value.Copy())
        };
    }
}
