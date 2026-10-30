using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SmartHunter.Game.Helpers
{
    internal static class TextKeyNames
    {
        static readonly Regex unknownPart = new Regex(@"^unk \(id: (\d+)\)$", RegexOptions.Compiled);
        static readonly Regex unknownStatus = new Regex(@"^unk(\d+)$", RegexOptions.Compiled);
        static readonly Regex separators = new Regex(@"[^A-Z0-9]+", RegexOptions.Compiled);
        public static string Canonical(string key)
        {
            if (key == null) return null;
            if (key.StartsWith("LOC_", StringComparison.Ordinal) && key.Length > 4 && key.All(c => c == '_' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9') && !key.EndsWith("_",StringComparison.Ordinal) && !key.Contains("__")) return LegacyTextKeys.Resolve(key);
            var part = unknownPart.Match(key);
            if (part.Success) return "LOC_PART_UNKNOWN_" + part.Groups[1].Value;
            var status = unknownStatus.Match(key);
            if (status.Success) return "LOC_STATUS_EFFECT_UNKNOWN_" + status.Groups[1].Value;
            string original = key;
            key = key.ToUpperInvariant();
            if (key == "AILMENT_ENRAGE") key = "LOC_STATUS_EFFECT_RAGE";
            else if (key == "ABNORMALITY_BLAST") key = "LOC_STATUS_EFFECT_BLASTBLIGHT";
            else if (key.StartsWith("ABNORMALITY_", StringComparison.Ordinal)) key = "LOC_PLAYER_STATUS_EFFECT_" + key.Substring(12);
            else if (key.StartsWith("AILMENT_", StringComparison.Ordinal)) key = "LOC_STATUS_EFFECT_" + key.Substring(8);
            else if (key.StartsWith("PART_", StringComparison.Ordinal)) key = "LOC_" + key;
            else if (key.StartsWith("WILDS_VARIANT_", StringComparison.Ordinal)) key = "LOC_MONSTER_VARIANT_" + key.Substring(14);
            else if (!key.StartsWith("LOC_", StringComparison.Ordinal)) key = "LOC_" + key;
            key = separators.Replace(key, "_").Trim('_');
            if (key == "LOC") key = "LOC_UNKNOWN";
            if (original.Any(c => c > 127))
                using (var hash = SHA256.Create()) key += "_" + BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(original)),0,4).Replace("-", "");
            return LegacyTextKeys.Resolve(key);
        }
        static readonly Lazy<Dictionary<string,string>> playerAliases = new Lazy<Dictionary<string,string>>(() =>
            LegacyTextKeys.PlayerAliases.ToDictionary(p => "LOC_STATUS_EFFECT_" + p.Key.Substring("LOC_PLAYER_STATUS_EFFECT_".Length), p => p.Value));
        public static string PlayerKey(string key)
        {
            key = Canonical(key);
            return key != null && playerAliases.Value.TryGetValue(key, out var current) ? current : key;
        }
        static readonly Lazy<JObject> previous = new Lazy<JObject>(() =>
        {
            using (var stream = typeof(TextKeyNames).Assembly.GetManifestResourceStream("SmartHunter.Wilds.Data.LegacyTextFingerprints.json"))
            using (var reader = new StreamReader(stream)) return JObject.Parse(reader.ReadToEnd());
        });
        public static bool Normalize(JToken token, string language = null, IDictionary<string,string> defaults = null)
        {
            bool changed = false;
            if (token is JObject obj)
            {
                if (obj["Strings"] is JObject strings)
                    foreach (var property in strings.Properties().ToArray())
                    {
                        string key = Canonical(property.Name);
                        if (key == property.Name) continue;
                        var current = strings[key];
                        bool customOld = !WasPreviousDefault(language, property.Name, (string)property.Value);
                        bool stockCurrent = defaults != null && defaults.TryGetValue(key, out var standard) && (string)current == standard;
                        if (current == null || customOld && stockCurrent) strings[key] = property.Value.DeepClone();
                        property.Remove(); changed = true;
                    }
                if (obj["Strings"] is JObject named && !LanguageCatalog.IsWorld(language))
                    foreach (var pair in playerAliases.Value)
                    {
                        if (named[pair.Key] == null || named[pair.Value] != null) continue;
                        named[pair.Value] = WasPreviousDefault(language, pair.Key, (string)named[pair.Key]) && defaults != null && defaults.TryGetValue(pair.Value,out var standard)
                            ? standard : named[pair.Key].DeepClone();
                        changed = true;
                    }
                foreach (var property in obj.Properties().ToArray())
                {
                    if (property.Name == "Strings") continue;
                    if ((property.Name == "NameStringId" || property.Name == "StringId" || property.Name == "NameId") && property.Value.Type == JTokenType.String)
                    {
                        string key = Canonical((string)property.Value);
                        if (key != (string)property.Value) { property.Value = key; changed = true; }
                    }
                    else changed |= Normalize(property.Value, language, defaults);
                }
            }
            else if (token is JArray array) foreach (var child in array) changed |= Normalize(child, language, defaults);
            return changed;
        }
        internal static bool WasPreviousDefault(string language,string key,string value)
        {
            JToken expected=previous.Value[LanguageCatalog.IsWorld(language)?"World":"Wilds"]?[LanguageCatalog.Canonical(language??"")]?[key];
            if(expected==null||value==null)return false;
            string digest;
            using(var hash=SHA256.Create())digest=BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-","").ToLowerInvariant();
            return expected is JArray values?values.Any(item=>(string)item==digest):(string)expected==digest;
        }
        public static string UpgradeData(string contents, bool wildsPlayer = false, bool wildsMonsters = false)
        {
            var document = JObject.Parse(contents);
            bool changed = wildsMonsters && SmartHunter.Wilds.WildsMonsterDataMigration.Upgrade(document);
            changed |= Normalize(document);
            if (wildsPlayer && document["StatusEffects"] is JArray statuses)
                foreach (var status in statuses.OfType<JObject>())
                {
                    string key = (string)status["NameStringId"], current = PlayerKey(key);
                    if (key != current) {status["NameStringId"]=current;changed=true;}
                }
            return changed ? document.ToString(Formatting.Indented) : contents;
        }
    }
}
