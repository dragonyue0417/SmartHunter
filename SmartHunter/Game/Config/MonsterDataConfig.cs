using System.Collections.Generic;
using Newtonsoft.Json;
using SmartHunter.Wilds;
namespace SmartHunter.Game.Config
{
    public sealed class MonsterDataConfig
    {
        public MonsterDataConfig() : this(SmartHunter.World.GameProfile.Active) { }
        public MonsterDataConfig(SmartHunter.World.GameChoice game)
        {
            if (game == SmartHunter.World.GameChoice.World)
            {
                var world = new WorldMonsterDataConfig(); Monsters = world.Monsters; StatusEffects = world.StatusEffects;
            }
            else
            {
                Monsters = JsonConvert.DeserializeObject<Dictionary<string, MonsterConfig>>(JsonConvert.SerializeObject(WildsCatalog.Instance.Monsters));
                StatusEffects = JsonConvert.DeserializeObject<MonsterStatusEffectConfig[]>(JsonConvert.SerializeObject(WildsCatalog.Instance.StatusEffects));
            }
        }
        // The embedded and user-editable schemas both use SmartHunter's models.
        // Clone defaults so user edits never mutate the embedded catalog.
        public Dictionary<string, MonsterConfig> Monsters;
        public MonsterStatusEffectConfig[] StatusEffects;
    }
}
