using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using SmartHunter.Core.Config;
using SmartHunter.Game.Config;
using SmartHunter.World;

namespace SmartHunter.Core
{
    // The combined model exists only in memory. Each file contains one game.
    public static class GameSettingsStore
    {
        public static string PathFor(string root, GameChoice game) => Path.Combine(root, "Config", game.ToString(), "Settings.json");
        static JsonSerializer Serializer() => JsonSerializer.Create(new JsonSerializerSettings
        {
            ContractResolver = new ContractResolver(),
            Converters = { new StringEnumConverter() }
        });
        public static JObject Document(MainConfig values, GameChoice game)
        {
            var serializer = Serializer();
            var document = JObject.FromObject(values.GetOptions(game), serializer);
            document.AddFirst(new JProperty("Schema", 1));
            document["Game"] = values.Game.ToString();
            document["SettingsGame"] = values.SettingsGame.ToString();
            string language = values.GetLanguage(game);
            string canonical = SmartHunter.Game.Helpers.LanguageCatalog.Canonical(language);
            bool builtIn = !(language ?? "").Contains(".ImportedWorld-") && (canonical == "zh-TW.json" || canonical == "zh-CN.json" || canonical == "en-US.json");
            document["LocalizationFileName"] = builtIn ? canonical : (language ?? "").Replace('\\','/').StartsWith("Languages/" + game + "/", StringComparison.OrdinalIgnoreCase) ? Path.GetFileName(language) : language;
            document["Overlay"] = JObject.FromObject(values.GetOverlay(game), serializer);
            document["Server"] = JObject.FromObject(values.GetRelay(game), serializer);
            return document;
        }
        public static bool UpgradeDocument(JObject document)
        {
            string before=document.ToString(Formatting.None);
            if(document["Updates"] is JObject updates && (updates["Repository"] != null || updates["Branch"] != null))
            {
                if(updates["Url"] == null)
                {
                    string repository=(string)updates["Repository"]??"dragonyue0417/SmartHunter", branch=(string)updates["Branch"]??"unified";
                    if(repository=="dragonyue0417/SmartHunter" && branch=="master")branch="unified";
                    updates["Url"]="https://raw.githubusercontent.com/"+repository+"/"+branch+"/release/";
                }
                updates.Remove("Repository");updates.Remove("Branch");
                updates["PendingDirectory"]="";updates["PendingSource"]="";
            }
            document.Remove("DamageSharing");
            if(document["Keybinds"] is JObject keys)
                foreach(var key in keys.Properties().Where(p=>p.Name.StartsWith("Copy",StringComparison.Ordinal)).ToArray())key.Remove();
            if(((int?)document["ReleaseDefaultsVersion"]??0)<1)
            {
                var debug=document["Debug"] as JObject??new JObject();
                foreach(string name in new[]{"UseInternalSkin","UseSampleData","CollectNetworkDiagnostics","ShowServerLogs","TraceUniquePointers","ShowWeirdRemovableParts"})debug[name]=false;
                document["Debug"]=debug;
                var overlay=document["Overlay"] as JObject??new JObject();
                var widget=overlay["DebugWidget"] as JObject??new JObject();widget["IsVisible"]=false;overlay["DebugWidget"]=widget;document["Overlay"]=overlay;
                document["ReleaseDefaultsVersion"]=1;
            }
            return before!=document.ToString(Formatting.None);
        }
        static void Populate(JToken document, object target)
        {
            if (document == null) return;
            using (var reader = document.CreateReader()) Serializer().Populate(reader, target);
        }
        static void Validate(JObject document)
        {
            // Preflight a separate model so an invalid hot-reload cannot leave
            // half of the live reader's configuration modified.
            var options = document.ToObject<GameOptions>(Serializer());
            if (options.Keybinds == null || options.Debug == null || String.IsNullOrWhiteSpace(options.SkinFileName))
                throw new InvalidDataException("Invalid game options.");
            if (document["Overlay"] != null)
            {
                var overlay = document["Overlay"].ToObject<OverlayConfig>(Serializer());
                if (overlay == null) throw new InvalidDataException("Overlay cannot be null.");
                foreach (var widget in new WidgetConfig[] { overlay.MonsterWidget, overlay.TeamWidget, overlay.PlayerWidget, overlay.DebugWidget })
                    if (widget == null || Single.IsNaN(widget.X) || Single.IsInfinity(widget.X) || Single.IsNaN(widget.Y) || Single.IsInfinity(widget.Y) ||
                        Single.IsNaN(widget.Scale) || Single.IsInfinity(widget.Scale) || widget.Scale <= 0)
                        throw new InvalidDataException("Invalid overlay coordinates or scale.");
            }
            if (document["Server"] != null && document["Server"].ToObject<RelayConfig>(Serializer()) == null)
                throw new InvalidDataException("Server cannot be null.");
            if (options.Updates == null) throw new InvalidDataException("Updates cannot be null.");
        }
        public static void Apply(MainConfig values, GameChoice game, JObject document, bool selection = true)
        {
            document=(JObject)document.DeepClone();UpgradeDocument(document);
            Validate(document);
            Populate(document, values.GetOptions(game));
            Populate(document["Overlay"], values.GetOverlay(game));
            Populate(document["Server"], values.GetRelay(game));
            values.GetRelay(game).ApplyGameDefaults(game);
            string language = (string)document["LocalizationFileName"];
            if (!String.IsNullOrWhiteSpace(language))
            {
                if (language.IndexOfAny(new[] {'/', '\\'}) < 0)
                {
                    string canonical = SmartHunter.Game.Helpers.LanguageCatalog.Canonical(language);
                    language = !language.Contains(".ImportedWorld-") && (canonical == "zh-TW.json" || canonical == "zh-CN.json" || canonical == "en-US.json")
                        ? SmartHunter.Game.Helpers.LanguageCatalog.ProfileName(canonical, game) : Path.Combine("Languages", game.ToString(), language);
                }
                values.SetLanguage(game, language);
            }
            if (!selection) return;
            if (Enum.TryParse((string)document["Game"], out GameChoice mode) && Enum.IsDefined(typeof(GameChoice), mode)) values.Game = mode;
            if (Enum.TryParse((string)document["SettingsGame"], out GameChoice editing) && Enum.IsDefined(typeof(GameChoice), editing)) values.SettingsGame = editing;
        }
        public static MainConfig Read(string root, MainConfig values = null)
        {
            values = values ?? new MainConfig();
            // The most recently edited profile supplies the startup selection.
            foreach (var game in new[] { GameChoice.World, GameChoice.Wilds }.OrderBy(g => File.GetLastWriteTimeUtc(PathFor(root, g))))
            {
                string path = PathFor(root, game);
                if (File.Exists(path))
                    try
                    {
                        var document = JObject.Parse(File.ReadAllText(path));
                        bool upgraded=UpgradeDocument(document);
                        Apply(values, game, document);
                        if (upgraded || document["Server"] is JObject currentServer && (string)currentServer["ServerUrl"] != values.GetRelay(game).ServerUrl)
                        {
                            // Persist only the corrected URL. Retain unknown
                            // fields and every other user preference's value.
                            DateTime modified = File.GetLastWriteTimeUtc(path);
                            if(document["Server"] is JObject server) server["ServerUrl"] = values.GetRelay(game).ServerUrl;
                            AtomicFile.Write(path, document.ToString(Formatting.Indented));
                            // Profile timestamps select the startup game. A URL
                            // correction must not make an inactive profile win.
                            File.SetLastWriteTimeUtc(path, modified);
                        }
                    }
                    catch (Exception error) { Log.WriteException(error); }
            }
            return values;
        }
        public static void Write(string root, MainConfig values, GameChoice? only = null)
        {
            foreach (var game in new[] { GameChoice.World, GameChoice.Wilds })
                if (!only.HasValue || only == game) AtomicFile.Write(PathFor(root, game), Document(values, game).ToString(Formatting.Indented));
        }
        internal static int MigrateLegacy(string root)
        {
            string[] sources = { Path.Combine(root, "Config", "Shared", "Settings.json"), Path.Combine(root, "Config.SmartHunter.json") };
            string source = sources.FirstOrDefault(File.Exists);
            if (source == null) return 0;
            // Validate all source/target documents before removing anything.
            var legacyDocument=JObject.Parse(File.ReadAllText(source));
            legacyDocument.Remove("DamageSharing");
            if(legacyDocument["Keybinds"] is JObject oldKeys)
                foreach(var key in oldKeys.Properties().Where(p=>p.Name.StartsWith("Copy",StringComparison.Ordinal)).ToArray())key.Remove();
            var legacy = legacyDocument.ToObject<MainConfig>(Serializer());
            var options = legacy.GetOptions(GameProfile.Active).Copy();
            legacy.WorldOptions = options.Copy(); legacy.WildsOptions = options.Copy();
            legacy.WorldOptions.Debug=new DebugConfig();legacy.WildsOptions.Debug=new DebugConfig();
            legacy.Overlays.World.DebugWidget.IsVisible=false;legacy.Overlays.Wilds.DebugWidget.IsVisible=false;
            foreach (var game in new[] { GameChoice.World, GameChoice.Wilds })
            {
                string path = PathFor(root, game);
                if (File.Exists(path)) JObject.Parse(File.ReadAllText(path));
            }
            foreach (var game in new[] { GameChoice.World, GameChoice.Wilds })
                if (!File.Exists(PathFor(root, game))) Write(root, legacy, game);
            int count = 0;
            foreach (string path in sources) if (File.Exists(path)) { File.Delete(path); count++; }
            string shared = Path.Combine(root, "Config", "Shared");
            if (Directory.Exists(shared) && !Directory.EnumerateFileSystemEntries(shared).Any()) Directory.Delete(shared);
            return count;
        }
    }
}
