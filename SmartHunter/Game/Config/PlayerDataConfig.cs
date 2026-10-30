using System.Collections.Generic;
using SmartHunter.Wilds;
namespace SmartHunter.Game.Config
{
    public sealed class PlayerDataConfig
    {
        public StatusEffectConfig[] StatusEffects;
        public PlayerDataConfig() : this(SmartHunter.World.GameProfile.Active) { }
        public PlayerDataConfig(SmartHunter.World.GameChoice game)
        {
            if (game == SmartHunter.World.GameChoice.World) { StatusEffects = new WorldPlayerDataConfig().StatusEffects; return; }
            var effects = new List<StatusEffectConfig>();
            foreach (var b in WildsCatalog.Instance.PlayerStatusEffects) effects.Add(new StatusEffectConfig(b.GroupId, b.NameStringId, b.Source, b.TimerOffset, b.Conditions));
            foreach (var id in new[] { "GHILLIE", "ROCKSTEADY", "MENDING", "EVASION", "CORRUPTED" })
            {
                effects.Add(new StatusEffectConfig("Equipment", "LOC_EQUIPMENT_MANTLE_" + id, 0, null));
                effects.Add(new StatusEffectConfig("Equipment", "LOC_EQUIPMENT_RECHARGE_MANTLE_" + id, 0, null));
            }
            StatusEffects = effects.ToArray();
        }
    }
}
