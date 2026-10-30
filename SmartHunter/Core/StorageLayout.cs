using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using SmartHunter.World;
using SmartHunter.Game.Helpers;
using Newtonsoft.Json.Linq;

namespace SmartHunter.Core
{
    public static class StorageLayout
    {
        public static string LastArchiveDirectory { get; private set; }
        public static int LastArchivedItems { get; private set; }
        public static string[] LastArchiveErrors { get; private set; } = new string[0];
        public static string Resolve(string root, string name)
        {
            if (Path.IsPathRooted(name) || name.IndexOfAny(new[] { '/', '\\' }) >= 0) return Path.GetFullPath(Path.Combine(root, name));
            if (name == "Config.SmartHunter.json") return GameSettingsStore.PathFor(root, GameProfile.Active);
            if (name == "Default.xaml") return Path.Combine(root, "Themes", name);
            if (name == "Log.SmartHunter.txt") return Path.Combine(root, "Logs", name);
            if (name == "Read.World.json" || name == "Read.World.Monsters.json") return Path.Combine(root, "Diagnostics", "World", name);
            if (name == "Scan.Wilds.json" || name == "Network.Wilds.json") return Path.Combine(root, "Diagnostics", "Wilds", name);
            foreach (string game in new[] { "World", "Wilds" })
            {
                foreach (string type in new[] { "MonsterData", "PlayerData", "Memory", "Settings" })
                    if (name == type + "." + game + ".json") return Path.Combine(root, "Config", game, type + ".json");
                if (name.EndsWith("." + game + ".json", StringComparison.OrdinalIgnoreCase))
                    return Path.Combine(root, "Languages", game, name.Substring(0, name.Length - game.Length - 6) + ".json");
            }
            if (name.Contains(".ImportedWorld-")) return Path.Combine(root, "Languages", "World", name);
            return Path.Combine(root, name);
        }
        public static int Prepare(string root)
        {
            root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
            LastArchiveDirectory = null; LastArchivedItems = 0; LastArchiveErrors = new string[0];
            var archive = new LegacyFileArchive(root);
            int migrated = LegacySettingsMigration.Convert(root, archive)+GameSettingsStore.MigrateLegacy(root, archive);
            foreach (string directory in new[] { "Config/World", "Config/Wilds", "Languages/World", "Languages/Wilds", "Logs", "Diagnostics/World", "Diagnostics/Wilds", "Themes" })
            {
                string path = Path.Combine(root, directory);
                if (!archive.SafePath(path)) throw new IOException("Application data path contains a reparse point: " + path);
                Directory.CreateDirectory(path);
            }
            foreach (string source in Directory.GetFiles(root))
            {
                if (!archive.SafePath(source)) continue;
                string name = Path.GetFileName(source), destination = Resolve(root, name);
                if (String.Equals(source, destination, StringComparison.OrdinalIgnoreCase)) continue;
                if (!archive.SafePath(destination)) continue;
                if (name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                { try { JObject.Parse(File.ReadAllText(source)); } catch { continue; } }
                // Existing classified files win. Keep every retired original
                // so the user can inspect or restore it before deletion.
                if (File.Exists(destination) && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    JObject.Parse(File.ReadAllText(destination));
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                if (!File.Exists(destination)) File.Copy(source, destination);
                if (archive.Move(source)) migrated++;
            }
            migrated += RetireLegacyFiles(root, archive);
            foreach (string game in new[] { "World", "Wilds" })
            {
                foreach (string path in Directory.EnumerateFiles(Path.Combine(root, "Languages", game), "*.json"))
                {
                    string upgraded = LanguageCatalog.UpgradeBuiltInFile(path, File.ReadAllText(path));
                    if (upgraded != null) AtomicFile.Write(path, upgraded);
                }
                foreach (string type in new[] { "MonsterData", "PlayerData" })
                {
                    string path = Path.Combine(root, "Config", game, type + ".json");
                    if (File.Exists(path)) AtomicFile.Write(path, TextKeyNames.UpgradeData(File.ReadAllText(path), game == "Wilds" && type == "PlayerData", game == "Wilds" && type == "MonsterData"));
                }
            }
            LastArchiveDirectory = archive.DirectoryPath;
            LastArchivedItems = archive.Count;
            LastArchiveErrors = archive.Errors.ToArray();
            return migrated;
        }

        static JObject LegacyDocument(string path, LegacyFileArchive archive)
        {
            try { return JObject.Parse(File.ReadAllText(path)); }
            catch (Newtonsoft.Json.JsonException) { return null; }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is System.Security.SecurityException)
            { archive.Errors.Add(path + ": " + error.Message); return null; }
        }

        static int RetireLegacyFiles(string root, LegacyFileArchive archive)
        {
            int count = 0;
            foreach (string name in new[] { "Config.json", "Config.Wilds.json" })
            {
                string path = Path.Combine(root, name);
                string target = GameSettingsStore.PathFor(root, name == "Config.json" ? GameChoice.World : GameChoice.Wilds);
                if (archive.SafePath(path) && archive.SafePath(target) && File.Exists(path) && File.Exists(target) &&
                    LegacyDocument(path, archive)?["Overlay"] is JObject && LegacyDocument(target, archive) != null && archive.Move(path)) count++;
            }
            foreach (string name in new[] { "Memory.json", "MonsterData.json", "PlayerData.json", "zh-TW.json", "zh-CN.json", "en-US.json" })
            {
                string path = Path.Combine(root, name);
                if (!archive.SafePath(path) || !File.Exists(path)) continue;
                var document = LegacyDocument(path, archive);
                if (document == null) continue;
                bool language = name == "zh-TW.json" || name == "zh-CN.json" || name == "en-US.json";
                if (language && !(document["Strings"] is JObject)) continue;
                string target = Path.Combine(root, language ? "Languages" : "Config", "World", name);
                if (!archive.SafePath(target)) continue;
                if (File.Exists(target) && LegacyDocument(target, archive) == null) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                if (!File.Exists(target)) File.Copy(path, target);
                if (archive.Move(path)) count++;
            }
            foreach (string path in Directory.GetFiles(root))
            {
                if (!archive.SafePath(path)) continue;
                string name = Path.GetFileName(path);
                bool obsolete = name.Equals("Log.txt", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("Versions.json", StringComparison.OrdinalIgnoreCase) && LegacyDocument(path, archive) != null;
                if (Regex.IsMatch(name, @"^Config\(\d+x\d+\)\.json$", RegexOptions.IgnoreCase))
                    obsolete = LegacyDocument(path, archive)?["Overlay"] is JObject;
                if (obsolete && archive.Move(path)) count++;
            }
            foreach (string name in new[] { "UserDataBackup", "Backups", "Imports" })
                if (archive.Move(Path.Combine(root, name))) count++;
            string nested = archive.NestedInstallation();
            if (nested != null)
            {
                string target = GameSettingsStore.PathFor(root, GameChoice.World);
                if (File.Exists(target) && LegacyDocument(target, archive) != null && archive.Move(nested)) count++;
            }
            return count;
        }
    }
}
