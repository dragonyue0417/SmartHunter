using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using SmartHunter.Game.Helpers;

namespace SmartHunter.Wilds
{
    internal static class WildsMonsterDataMigration
    {
        static int PreviousIndex(string key)
        {
            if (key == null || !key.StartsWith("em", StringComparison.Ordinal) || !key.EndsWith("_00", StringComparison.Ordinal)) return -1;
            return Int32.TryParse(key.Substring(2, key.Length - 5), out int index) && index >= 0 && index < WildsMonsterTypes.Count ? index : -1;
        }
        public static bool Upgrade(JObject document)
        {
            bool changed = false;
            if (document["Monsters"] is JObject monsters)
            {
                var definitions = WildsCatalog.Instance.Monsters;
                var entries = monsters.Properties().ToArray();
                bool legacy = entries.Any(p => ((string)p.Value["NameStringId"] ?? "").StartsWith("LOC_WILDS_MONSTER_", StringComparison.Ordinal) ||
                    PreviousIndex(p.Name) == 0 || definitions.Any(d => d.Value.NameStringId == TextKeyNames.Canonical((string)p.Value["NameStringId"]) && d.Key != p.Name));
                if (legacy)
                {
                    // Build a new dictionary simultaneously: sequential renaming
                    // would overwrite Rathalos while moving Rathian to em001_00.
                    var current = new JObject();
                    foreach (var entry in entries.OrderBy(p => definitions.ContainsKey(p.Name) && definitions[p.Name].NameStringId == (string)p.Value["NameStringId"] ? 0 : 1))
                    {
                        string name = TextKeyNames.Canonical((string)entry.Value["NameStringId"]);
                        string id = definitions.FirstOrDefault(d => d.Value.NameStringId == name).Key;
                        int index = PreviousIndex(entry.Name);
                        if (id == null) id = index >= 0 ? WildsMonsterTypes.Id(index) : entry.Name;
                        if (current[id] is JObject existing && entry.Value is JObject incoming)
                        {
                            // Prefer an already converted entry. Never merge part
                            // arrays: their physical order identifies native slots.
                            foreach (var property in incoming.Properties())
                                if (existing[property.Name] == null) existing[property.Name] = property.Value.DeepClone();
                        }
                        else current[id] = entry.Value.DeepClone();
                        changed |= id != entry.Name;
                    }
                    if (changed) document["Monsters"] = current;
                }
            }
            if (document["StatusEffects"] is JArray statuses)
                for (int i = 0; i < statuses.Count; i++)
                {
                    if (!(statuses[i] is JObject status)) continue;
                    if ((string)status["GroupId"] == "Status")
                    {
                        status["GroupId"] = i == 0 ? "Rage" : i == 1 ? "Stamina" : "StatusEffect";
                        changed = true;
                    }
                    if (i == 1 && (string)status["NameStringId"] == "LOC_STATUS_EFFECT_EXHAUST")
                    { status["NameStringId"] = "LOC_STATUS_EFFECT_STAMINA"; changed = true; }
                    if (((string)status["NameStringId"] ?? "").StartsWith("LOC_STATUS_EFFECT_UNKNOWN_", StringComparison.Ordinal))
                    { status["NameStringId"] = "LOC_STATUS_EFFECT_UNKNOWN"; changed = true; }
                }
            return changed;
        }
    }
}
