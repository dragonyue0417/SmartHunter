using System;
using System.IO;
using SmartHunter.World;
using SmartHunter.Game.Helpers;
using Newtonsoft.Json.Linq;

namespace SmartHunter.Core
{
    public static class StorageLayout
    {
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
            int migrated = LegacySettingsMigration.Convert(root)+GameSettingsStore.MigrateLegacy(root);
            foreach (string directory in new[] { "Config/World", "Config/Wilds", "Languages/World", "Languages/Wilds", "Logs", "Diagnostics/World", "Diagnostics/Wilds", "Themes" })
                Directory.CreateDirectory(Path.Combine(root, directory));
            foreach (string source in Directory.GetFiles(root))
            {
                string name = Path.GetFileName(source), destination = Resolve(root, name);
                if (String.Equals(source, destination, StringComparison.OrdinalIgnoreCase)) continue;
                if (name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                { try { JObject.Parse(File.ReadAllText(source)); } catch { continue; } }
                // Only recognized root-level aliases move. Existing classified
                // files win; source copies are retired after validation.
                if (File.Exists(destination) && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    JObject.Parse(File.ReadAllText(destination));
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                if (!File.Exists(destination)) File.Move(source, destination);
                else File.Delete(source);
                migrated++;
            }
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
            // These directories are obsolete, explicitly retired by the user.
            // Do not follow junctions or symbolic links out of the app folder.
            foreach (string name in new[] { "Backups", "Imports" })
            {
                string path = Path.GetFullPath(Path.Combine(root, name));
                if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Cleanup path outside application folder.");
                if (Directory.Exists(path)) RemoveDirectory(path);
            }
            return migrated;
        }
        static void RemoveDirectory(string path)
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0)
            {
                foreach (string child in Directory.GetDirectories(path)) RemoveDirectory(child);
                foreach (string file in Directory.GetFiles(path))
                {
                    var attributes = File.GetAttributes(file);
                    if ((attributes & FileAttributes.ReparsePoint) == 0 && (attributes & FileAttributes.ReadOnly) != 0)
                        File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
                    File.Delete(file);
                }
            }
            Directory.Delete(path);
        }
    }
}
