using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using SmartHunter.Game.Config;

namespace SmartHunter.Wilds
{
    public enum WildsEffectSource : uint { Consumable = 1, HuntingHorn = 2, Palico = 3, Skill = 4, Debuff = 5 }
    public sealed class WildsStatusEffectConfig : StatusEffectConfig
    {
        public bool HasMaxDuration = true, IsBuildup;
        public int Index, MaxBuildup;
        public WildsStatusEffectConfig() : base(null, null, 0, null) { }
        public static int Hex(string text) => String.IsNullOrEmpty(text) ? 0 : Convert.ToInt32(text, 16);
    }
    public sealed class WildsCatalog
    {
        public static WildsCatalog Instance { get; } = new WildsCatalog();
        public Dictionary<string, MonsterConfig> Monsters;
        public MonsterStatusEffectConfig[] StatusEffects;
        public WildsStatusEffectConfig[] PlayerStatusEffects;
        static JObject Document(string name)
        {
            string resource = "SmartHunter.Wilds.Data." + name + ".json";
            using (var input = typeof(WildsCatalog).Assembly.GetManifestResourceStream(resource))
            {
                if (input == null)
                    throw new InvalidDataException("Missing built-in game definitions: " + name);
                using (var text = new StreamReader(input))
                    return JObject.Parse(text.ReadToEnd());
            }
        }
        WildsCatalog()
        {
            var actors = Document("MonsterData");
            var player = Document("PlayerData");
            Monsters = actors["Monsters"].ToObject<Dictionary<string, MonsterConfig>>();
            StatusEffects = actors["StatusEffects"].ToObject<MonsterStatusEffectConfig[]>();
            PlayerStatusEffects = player["StatusEffects"].ToObject<WildsStatusEffectConfig[]>();
            if (Monsters.Count > 512 || StatusEffects.Length > 256 || PlayerStatusEffects.Length > 512)
                throw new InvalidDataException("Game definitions exceed their bounds.");
        }
        public MonsterConfig Monster(int runtimeType)
        {
            string id = WildsMonsterTypes.Id(runtimeType);
            return Monsters.TryGetValue(id, out var definition) ? definition : null;
        }
    }
}
