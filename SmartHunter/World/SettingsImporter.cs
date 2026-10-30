using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SmartHunter.Core;
using SmartHunter.Game.Config;
using SmartHunter.Game.Helpers;

namespace SmartHunter.World
{
    public sealed class SettingsImportResult
    {
        public OverlayConfig Overlay;
        public JObject ServerChanges;
        public string LanguageFileName;
        public int ConfigFiles, LanguageFiles;
    }
    public static class SettingsImporter
    {
        public static SettingsImportResult Import(IEnumerable<string> selectedFiles, string destination, GameChoice game, string currentLanguage, MainConfig current = null)
        {
            var files = selectedFiles.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (game == GameChoice.Auto || files.Count == 0) throw new InvalidDataException("Select a target profile and JSON files.");
            var documents = files.ToDictionary(p => p, p => JObject.Parse(File.ReadAllText(p)));
            // Include a configuration's selected translation when it wasn't selected manually.
            foreach (var pair in documents.ToArray())
            {
                string name = (string)pair.Value["LocalizationFileName"] ?? (string)pair.Value[game + "LocalizationFileName"];
                if (String.IsNullOrWhiteSpace(name)) continue;
                string folder = Path.GetDirectoryName(pair.Key), path = Path.GetFullPath(Path.Combine(folder, name));
                if (path.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && File.Exists(path) && !documents.ContainsKey(path))
                    documents.Add(path, JObject.Parse(File.ReadAllText(path)));
            }
            var result = new SettingsImportResult();
            var languages = new List<KeyValuePair<string, JObject>>();
            string preferred = null;
            foreach (var pair in documents)
            {
                var overlay = pair.Value["Overlay"] as JObject ?? pair.Value["Overlays"]?[game.ToString()] as JObject;
                if (overlay != null)
                {
                    result.Overlay = overlay.ToObject<OverlayConfig>(); result.ConfigFiles++;
                    if (game == GameChoice.World && overlay["MonsterWidget"]?["CardWidth"] == null)
                        result.Overlay.MonsterWidget.CardWidth = 300;
                    preferred = (string)pair.Value["LocalizationFileName"] ?? (string)pair.Value[game + "LocalizationFileName"] ?? preferred;
                    foreach (var widget in new SmartHunter.Core.Config.WidgetConfig[] { result.Overlay.MonsterWidget, result.Overlay.TeamWidget, result.Overlay.PlayerWidget, result.Overlay.DebugWidget })
                        if (widget == null || Single.IsNaN(widget.X) || Single.IsInfinity(widget.X) || Single.IsNaN(widget.Y) || Single.IsInfinity(widget.Y) || Single.IsNaN(widget.Scale) || Single.IsInfinity(widget.Scale) || widget.Scale <= 0)
                            throw new InvalidDataException("Invalid overlay coordinates or scale: " + Path.GetFileName(pair.Key));
                    var server = pair.Value["Server"] as JObject ?? pair.Value["Servers"]?[game.ToString()] as JObject ?? pair.Value["Relay"] as JObject;
                    if (server != null) result.ServerChanges = (JObject)server.DeepClone();
                    if (pair.Value["ServerUrl"]?.Type == JTokenType.String)
                    {
                        if (result.ServerChanges == null) result.ServerChanges = new JObject();
                        result.ServerChanges["ServerUrl"] = pair.Value["ServerUrl"].DeepClone();
                    }
                    if (overlay["MonsterWidget"]?["UseNetworkServer"]?.Type == JTokenType.Boolean)
                    {
                        if (result.ServerChanges == null) result.ServerChanges = new JObject();
                        result.ServerChanges["Enabled"] = overlay["MonsterWidget"]["UseNetworkServer"].DeepClone();
                    }
                }
                else if (pair.Value["Strings"] is JObject strings && strings.Properties().All(p => p.Value.Type == JTokenType.String)) languages.Add(pair);
                else throw new InvalidDataException("Unsupported settings or language JSON: " + Path.GetFileName(pair.Key));
            }
            string root = Path.GetFullPath(destination);
            string existing = GameSettingsStore.PathFor(root, game);
            if (File.Exists(existing)) JObject.Parse(File.ReadAllText(existing));
            // Build all outputs before writing; originals are never moved or
            // copied into the application directory.
            var settings = new MainConfig();
            var sourceSettings = current ?? GameSettingsStore.Read(root);
            foreach (var profileGame in new[] { GameChoice.World, GameChoice.Wilds })
                GameSettingsStore.Apply(settings, profileGame, GameSettingsStore.Document(sourceSettings, profileGame));
            var serializer = JsonSerializer.Create(new JsonSerializerSettings { Converters = { new Newtonsoft.Json.Converters.StringEnumConverter() } });
            var writes = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            if (result.ServerChanges != null)
            {
                var profile = settings.GetRelay(game);
                JsonConvert.PopulateObject(result.ServerChanges.ToString(), profile);
                result.ServerChanges = JObject.FromObject(profile, serializer);
            }
            foreach (var pair in languages)
            {
                string name = LanguageCatalog.Canonical(Path.GetFileName(pair.Key));
                if (name != "zh-TW.json" && name != "zh-CN.json" && name != "en-US.json") name = Path.GetFileName(pair.Key);
                string relative = Path.Combine("Languages", game.ToString(), name);
                string target = Path.Combine(root, relative);
                if (!String.Equals(pair.Key, target, StringComparison.OrdinalIgnoreCase))
                {
                    var merged = File.Exists(target) ? JObject.Parse(File.ReadAllText(target)) : new JObject();
                    merged.Merge(pair.Value, new JsonMergeSettings { MergeArrayHandling = MergeArrayHandling.Replace });
                    string upgraded = LanguageCatalog.UpgradeBuiltInFile(Path.GetFileNameWithoutExtension(name) + "." + game + ".json", merged.ToString());
                    writes[target] = upgraded ?? merged.ToString(Formatting.Indented);
                }
                result.LanguageFiles++;
                if (result.LanguageFileName == null || LanguageCatalog.Canonical(preferred ?? currentLanguage) == name) result.LanguageFileName = relative;
            }
            if (result.Overlay != null)
            {
                if (game == GameChoice.World) settings.Overlays.World = result.Overlay;
                else settings.Overlays.Wilds = result.Overlay;
            }
            if (result.LanguageFileName != null) settings.SetLanguage(game, result.LanguageFileName);
            writes[GameSettingsStore.PathFor(root, game)] = GameSettingsStore.Document(settings, game).ToString(Formatting.Indented);
            foreach (var write in writes) AtomicFile.Write(write.Key, write.Value);
            return result;
        }
    }
}
