using System.Text.RegularExpressions;
using SmartHunter.Core.Config;

namespace SmartHunter.Game.Config
{
    public enum MonsterLayout { Vertical, Horizontal, Grid }

    public class MonsterWidgetConfig : WidgetConfig
    {
        public bool CenterMonsters = true;
        [Newtonsoft.Json.JsonIgnore] public bool UseNetworkServer;
        // em[0-9]|ems[0-9]|gm[0-9]
        public string IncludeMonsterIdRegex = "em[0-9]";
        public string IncludePartGroupIdRegex = ".*";
        public string IncludePartSoftenGroupIdRegex = ".*";
        public string IncludeStatusEffectGroupIdRegex = ".*";
        public bool ShowUnchangedMonsters = true;
        public float HideMonstersAfterSeconds = 999f;
        public bool ShowParts = true;
        public bool ShowUnchangedParts = false;
        public float HidePartsAfterSeconds = 12f;
        public bool ShowSoftenParts = false;
        public float HideSoftenPartsAfterSeconds = 6f;
        public bool ShowStatusEffects = true;
        public bool ShowUnchangedStatusEffects = false;
        public float HideStatusEffectsAfterSeconds = 12f;

        public bool ShowSize = true;
        public bool ShowCrown = true;
        public bool ShowBars = true;
        public bool ShowNumbers = true;
        public bool ShowPercents = false;
        public bool UseAnimations = false;
        public bool ShowOnlySelectedMonster = false;
        public bool PrioritizeSelectedMonster = true;
        public bool ShowInCamp = true;
        public bool AlwaysShowParts = false;
        public MonsterLayout Layout = MonsterLayout.Horizontal;
        public bool WrapHorizontal = true;
        public bool SelectedDetailsOnly = false;
        public int Columns = 3;
        public int Rows = 2;
        public int WindowWidth = 0;
        public int CardWidth = 220;


        public MonsterWidgetConfig(float x, float y) : base(x, y)
        {
        }

        public bool MatchIncludeMonsterIdRegex(string monsterId)
        {
            return !string.IsNullOrEmpty(monsterId) && Regex.IsMatch(monsterId, IncludeMonsterIdRegex);
        }

        public bool MatchIncludePartGroupIdRegex(string groupId)
        {
            return groupId != null && Regex.IsMatch(groupId, IncludePartGroupIdRegex);
        }

        public bool MatchIncludePartSoftenGroupIdRegex(string groupId)
        {
            return groupId != null && Regex.IsMatch(groupId, IncludePartSoftenGroupIdRegex);
        }

        public bool MatchIncludeStatusEffectGroupIdRegex(string groupId)
        {
            return groupId != null && Regex.IsMatch(groupId, IncludeStatusEffectGroupIdRegex);
        }
    }
}
