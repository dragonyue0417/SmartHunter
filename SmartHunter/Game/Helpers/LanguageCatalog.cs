using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SmartHunter.Core;
using SmartHunter.Core.Data;

namespace SmartHunter.Game.Helpers
{
    public sealed class LanguageOption
    {
        public string FileName { get; }
        public string DisplayName { get; }
        public LanguageOption(string fileName, string displayName) { FileName = fileName; DisplayName = displayName; }
    }

    public static class LanguageCatalog
    {
        static readonly ConcurrentDictionary<string, Dictionary<string,string>> bundled = new ConcurrentDictionary<string, Dictionary<string,string>>();
        public static string Canonical(string fileName)
        {
            string name = Path.GetFileName(fileName ?? "");
            foreach (string code in new[] { "zh-TW", "zh-CN", "en-US" })
                if (name == code + ".json" || name == code + ".World.json" || name == code + ".Wilds.json" || name.StartsWith(code + ".ImportedWorld-", StringComparison.Ordinal)) return code + ".json";
            return name;
        }
        public static string ProfileName(string fileName, SmartHunter.World.GameChoice? game = null)
        {
            string canonical = Canonical(fileName);
            if (canonical != "zh-TW.json" && canonical != "zh-CN.json" && canonical != "en-US.json") return fileName;
            return Path.GetFileNameWithoutExtension(canonical) + ((game ?? SmartHunter.World.GameProfile.Active) == SmartHunter.World.GameChoice.World ? ".World.json" : ".Wilds.json");
        }
        // Canonicalize editable keys in all locales. Replace stock wording only
        // in bundled file names and only when it matches a former default.
        public static string UpgradeBuiltInFile(string fileName, string contents)
        {
            string profile = IsWorld(fileName) ? "World" : "Wilds";
            var document = JObject.Parse(contents);
            string originalName = fileName;
            fileName = Canonical(fileName);
            var defaults = ReadBuiltIn(Path.GetFileNameWithoutExtension(fileName) + "." + profile + ".json");
            var preserve = new HashSet<string>(((document["Strings"] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>())
                .Where(p => p.Name.StartsWith("ABNORMALITY_",StringComparison.Ordinal) || p.Name.StartsWith("AILMENT_",StringComparison.Ordinal) || p.Name.StartsWith("PART_",StringComparison.Ordinal) || p.Name.StartsWith("unk",StringComparison.Ordinal))
                .Select(p => TextKeyNames.Canonical(p.Name)));
            bool changed = TextKeyNames.Normalize(document, originalName, defaults);
            if(document["Strings"] is JObject editable)
                foreach(var property in editable.Properties().Where(p=>p.Name.StartsWith("LOC_UI_CHAT_",StringComparison.Ordinal)||p.Name.StartsWith("LOC_LOG_CHAT_",StringComparison.Ordinal)||p.Name.StartsWith("LOC_UI_DAMAGE_",StringComparison.Ordinal)||p.Name=="LOC_LOG_DAMAGE_COPIED"||p.Name=="LOC_UI_SHARING"||p.Name=="LOC_UI_SHARING_DESC" ||
                    p.Name.StartsWith("LOC_UI_UPDATE_MASTER_",StringComparison.Ordinal)||p.Name.StartsWith("LOC_LOG_UPDATE_MASTER_",StringComparison.Ordinal)||p.Name=="LOC_UI_UPDATE_TESTDOWNLOADING" ||
                    p.Name.StartsWith("LOC_UI_UPDATE_REPOSITORY",StringComparison.Ordinal)||p.Name.StartsWith("LOC_UI_UPDATE_BRANCH",StringComparison.Ordinal)).ToArray())
                {property.Remove();changed=true;}
            if (originalName.Contains(".ImportedWorld-") || fileName != "zh-TW.json" && fileName != "zh-CN.json" && fileName != "en-US.json")
                return changed ? document.ToString(Formatting.Indented) : null;
            var strings = document["Strings"] as JObject;
            if (strings == null) return null;
            foreach (var pair in defaults)
            {
                var current = strings[pair.Key];
                if (current != null && preserve.Contains(pair.Key)) continue;
                if (current == null || current.Type == JTokenType.String && (string)current != pair.Value &&
                    TextKeyNames.WasPreviousDefault(originalName,pair.Key,(string)current))
                {
                    strings[pair.Key] = pair.Value;
                    changed = true;
                }
            }
            return changed ? document.ToString(Formatting.Indented) : null;
        }

        public static void NormalizeSeparators(Dictionary<string, string> strings)
        {
            foreach (var key in strings.Keys.ToArray())
                strings[key] = strings[key]?.Replace('\uFF0F', '/');
        }

        // Recognize older generated descriptions without retaining their wording.
        public static void UpgradeLegacyDescriptions(Dictionary<string,string> strings,string fileName=null)
        {
            string profile=IsWorld(fileName)?"World":"Wilds";
            foreach(string key in new[]{"LOC_UI_MONSTERS_DESC","LOC_SETTING_SHOW_ONLY_SELECTED_MONSTER_DESC","LOC_UI_STATE_SAFEZONE"})
            {
                if(!strings.TryGetValue(key,out string current))continue;
                foreach(string code in new[]{"en-US","zh-TW","zh-CN"})
                {
                    string name=code+"."+profile+".json";
                    if(!TextKeyNames.WasPreviousDefault(name,key,current))continue;
                    if(ReadBuiltIn(name).TryGetValue(key,out string replacement))strings[key]=replacement;
                    break;
                }
            }
        }

        public static Dictionary<string, string> ReadBuiltIn(string fileName)
        {
            string code = Path.GetFileNameWithoutExtension(Canonical(fileName));
            bool world = IsWorld(fileName);
            string resource = (world ? "SmartHunter.World.Languages." : "SmartHunter.Languages.") + code + ".json";
            var strings = bundled.GetOrAdd(resource, name =>
            {
                using (var stream = typeof(LanguageCatalog).Assembly.GetManifestResourceStream(name))
                {
                if (stream == null) return new Dictionary<string,string>();
                using (var reader = new StreamReader(stream))
                    return JObject.Parse(reader.ReadToEnd())["Strings"].ToObject<Dictionary<string, string>>();
                }
            });
            // Callers merge user overrides into their own dictionary; never
            // expose the cached baseline to those mutations.
            return new Dictionary<string,string>(strings);
        }

        internal static bool IsWorld(string name)
        {
            string path = "/" + (name ?? "").Replace('\\', '/');
            if (path.IndexOf("/World/", StringComparison.OrdinalIgnoreCase) >= 0 || path.EndsWith(".World.json", StringComparison.OrdinalIgnoreCase) || path.Contains(".ImportedWorld-")) return true;
            if (path.IndexOf("/Wilds/", StringComparison.OrdinalIgnoreCase) >= 0 || path.EndsWith(".Wilds.json", StringComparison.OrdinalIgnoreCase)) return false;
            return SmartHunter.World.GameProfile.Active == SmartHunter.World.GameChoice.World;
        }
        public static bool SameFile(string root, string first, string second) =>
            String.Equals(StorageLayout.Resolve(root, first), StorageLayout.Resolve(root, second), StringComparison.OrdinalIgnoreCase);

        public static IList<LanguageOption> Discover(string directory, string currentFile, SmartHunter.World.GameChoice? game = null)
        {
            var result = new List<LanguageOption>
            {
                new LanguageOption(ProfileName("zh-TW.json",game), "繁體中文 · zh-TW"),
                new LanguageOption(ProfileName("zh-CN.json",game), "简体中文 · zh-CN"),
                new LanguageOption(ProfileName("en-US.json",game), "English · en-US")
            };
            string profileFolder = Path.Combine(directory,"Languages",(game ?? SmartHunter.World.GameProfile.Active).ToString());
            var paths=Directory.EnumerateFiles(directory,"*.json").Concat(Directory.Exists(profileFolder) ? Directory.EnumerateFiles(profileFolder,"*.json") : Enumerable.Empty<string>());
            foreach (string path in paths)
            {
                string name = Path.GetFileName(path);
                if(String.Equals(Path.GetDirectoryName(path),profileFolder,StringComparison.OrdinalIgnoreCase))
                {
                    name=Path.Combine("Languages",(game ?? SmartHunter.World.GameProfile.Active).ToString(),name);
                }
                bool world = (game ?? SmartHunter.World.GameProfile.Active) == SmartHunter.World.GameChoice.World;
                if (world && name.EndsWith(".Wilds.json", StringComparison.OrdinalIgnoreCase) || !world && (name.EndsWith(".World.json", StringComparison.OrdinalIgnoreCase) || name.Contains(".ImportedWorld-"))) continue;
                if (name == "zh-TW.json" || name == "zh-CN.json" || name == "en-US.json") continue;
                if (result.Any(o => SameFile(directory, o.FileName, name))) continue;
                try
                {
                    if (JObject.Parse(File.ReadAllText(path))["Strings"] is JObject)
                        result.Add(new LanguageOption(name, DisplayName(name)));
                }
                catch (Exception) { /* Other configuration files are not languages. */ }
            }
            if (!result.Any(o => SameFile(directory, o.FileName, currentFile)))
                result.Add(new LanguageOption(currentFile, DisplayName(currentFile)));
            return result;
        }

        static string DisplayName(string fileName)
        {
            string code = Path.GetFileNameWithoutExtension(Canonical(fileName));
            try { return CultureInfo.GetCultureInfo(code).NativeName + " · " + code; }
            catch (CultureNotFoundException) { return code; }
        }
    }

    // Indexer bindings are invalidated whenever the active translation is reloaded.
    public sealed class UiText : Bindable
    {
        public static UiText Instance { get; } = new UiText();
        UiText() { ConfigHelper.Localization.Loaded += (s, e) => NotifyPropertyChanged("Item[]"); }
        public string this[string key] => LocalizationHelper.GetString(key);
    }
}
