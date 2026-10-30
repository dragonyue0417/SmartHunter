using System.Text.RegularExpressions;
using SmartHunter.Core.Config;

namespace SmartHunter.Game.Config
{
    public class PlayerWidgetConfig : WidgetConfig
    {
        public string IncludeStatusEffectGroupIdRegex = ".*";

        public PlayerWidgetConfig(float x, float y) : base(x, y)
        {
        }

        public bool MatchIncludeStatusEffectGroupIdRegex(string groupId)
        {
            return !string.IsNullOrEmpty(groupId) && Regex.IsMatch(groupId, IncludeStatusEffectGroupIdRegex);
        }
    }
}
