using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using SmartHunter.Core;
using SmartHunter.Game.Config;
using SmartHunter.World;
using static TestSupport;

internal static class LegacyArchiveTests
{
    const string Original = "{\"Overlay\":{\"MonsterWidget\":{\"X\":177,\"Y\":255,\"Scale\":1.25}},\"LocalizationFileName\":\"zh-TW.json\",\"ServerUrl\":\"http://legacy.invalid/index.php\"}";
    static Dictionary<string, byte[]> Contents(string root) => Directory.GetFiles(root, "*", SearchOption.AllDirectories)
        .ToDictionary(path => path.Substring(root.Length + 1), File.ReadAllBytes);
    static void Same(Dictionary<string, byte[]> expected, string root)
    {
        foreach (var item in expected)
            Assert(File.Exists(Path.Combine(root, item.Key)) && File.ReadAllBytes(Path.Combine(root, item.Key)).SequenceEqual(item.Value), "archived bytes retained: " + item.Key);
    }
    static string Nested(string root)
    {
        string old = Path.Combine(root, "SmartHunter"); Directory.CreateDirectory(old);
        File.Copy(typeof(StorageLayout).Assembly.Location, Path.Combine(old, "SmartHunter.exe"));
        File.WriteAllText(Path.Combine(old, "Config.json"), Original);
        File.WriteAllText(Path.Combine(old, "zh-TW.json"), "{\"Strings\":{\"LOC_PART_HEAD\":\"測試頭部\"},\"Author\":\"User\"}");
        File.WriteAllText(Path.Combine(old, "Config(1920x1080).json"), Original);
        File.WriteAllText(Path.Combine(old, "Versions.json"), "{\"SmartHunter.exe\":\"2024.10.8.0\"}");
        File.WriteAllText(Path.Combine(old, "Log.txt"), "old log");
        Directory.CreateDirectory(Path.Combine(old, "UserDataBackup"));
        File.WriteAllBytes(Path.Combine(old, "UserDataBackup/save.zip"), new byte[] { 0, 1, 2, 3 });
        return old;
    }

    internal static void FlatFiles()
    {
        string root = Folder();
        File.WriteAllText(Path.Combine(root, "Config.json"), Original);
        File.WriteAllText(Path.Combine(root, "Config(1920x1080).json"), Original);
        File.WriteAllText(Path.Combine(root, "Config(2560x1440).json"), Original);
        File.WriteAllText(Path.Combine(root, "Versions.json"), "{}");
        File.WriteAllText(Path.Combine(root, "Log.txt"), "log bytes");
        File.WriteAllText(Path.Combine(root, "Default.xaml"), "<ResourceDictionary/>");
        File.WriteAllText(Path.Combine(root, "zh-TW.json"), "{\"Strings\":{\"CUSTOM\":\"保留\"}}");
        foreach (string name in new[] { "UserDataBackup", "Backups", "Imports" })
        { Directory.CreateDirectory(Path.Combine(root, name)); File.WriteAllText(Path.Combine(root, name, "original.dat"), name); }
        File.WriteAllText(Path.Combine(root, "notes.txt"), "unrelated");
        var original = Contents(root); original.Remove("notes.txt");
        StorageLayout.Prepare(root);
        string archive = StorageLayout.LastArchiveDirectory;
        Assert(archive != null && archive.StartsWith(Path.Combine(root, "LegacyFiles") + Path.DirectorySeparatorChar), "archive stays inside this installation");
        Same(original, archive);
        var current = GameSettingsStore.Read(root);
        Assert(current.Overlays.World.MonsterWidget.X == 177 && current.Servers.World.ServerUrl == "http://legacy.invalid/index.php", "active settings converted before the source is moved");
        Assert(File.ReadAllText(Path.Combine(root, "notes.txt")) == "unrelated" && !Directory.Exists(Path.Combine(root, "UserDataBackup")), "unknown files stay put; old save backups are moved intact");
        Assert(StorageLayout.Prepare(root) == 0 && StorageLayout.LastArchivedItems == 0 && Directory.GetDirectories(Path.Combine(root, "LegacyFiles")).Length == 1, "another launch neither rearchives files nor creates empty batches");
    }

    internal static void NestedMigration()
    {
        string root = Folder(), old = Nested(root); var original = Contents(old);
        StorageLayout.Prepare(root);
        string archived = Path.Combine(StorageLayout.LastArchiveDirectory, "SmartHunter");
        Same(original, archived);
        Assert(!Directory.Exists(old) && GameSettingsStore.Read(root).Overlays.World.MonsterWidget.X == 177, "nested legacy settings migrate and the old installation moves as one folder");
        var language = JObject.Parse(File.ReadAllText(Path.Combine(root, "Languages/World/zh-TW.json")));
        Assert((string)language["Strings"]["LOC_PART_HEAD"] == "測試頭部", "nested custom translations reach the active profile");
        Assert(StorageLayout.Prepare(root) == 0, "nested migration runs once");
    }

    internal static void CurrentFilesWin()
    {
        string root = Folder(); var values = new MainConfig(); values.Overlays.World.MonsterWidget.X = 600;
        GameSettingsStore.Write(root, values); string target = GameSettingsStore.PathFor(root, GameChoice.World), before = File.ReadAllText(target);
        string old = Nested(root); var original = Contents(old);
        StorageLayout.Prepare(root);
        Same(original, Path.Combine(StorageLayout.LastArchiveDirectory, "SmartHunter"));
        Assert(File.ReadAllText(target) == before, "an obsolete nested configuration cannot overwrite the active one");
        string project = Path.Combine(root, "SmartHunter"); Directory.CreateDirectory(project);
        File.Copy(typeof(StorageLayout).Assembly.Location, Path.Combine(project, "SmartHunter.exe"));
        File.WriteAllText(Path.Combine(project, "Config.json"), Original); File.WriteAllText(Path.Combine(project, "SmartHunter.csproj"), "<Project/>");
        StorageLayout.Prepare(root);
        Assert(Directory.Exists(project) && StorageLayout.LastArchivedItems == 0, "a source project is never treated as a retired installation");
        File.Delete(Path.Combine(project, "SmartHunter.csproj")); File.WriteAllText(Path.Combine(project, ".git"), "gitdir: ../managed-worktree");
        StorageLayout.Prepare(root);
        Assert(Directory.Exists(project) && StorageLayout.LastArchivedItems == 0, "a Git worktree marker also protects a source checkout");
    }

    internal static void ActiveAssets()
    {
        string root = Folder(), old = Nested(root), asset = Path.Combine(old, "custom.xaml");
        File.WriteAllText(asset, "<ResourceDictionary/>");
        var settings = JObject.Parse(Original); settings["SkinFileName"] = "custom.xaml";
        File.WriteAllText(Path.Combine(old, "Config.json"), settings.ToString());
        StorageLayout.Prepare(root);
        Assert(Directory.Exists(old) && File.Exists(asset) && StorageLayout.LastArchivedItems == 0, "an installation containing the active custom theme remains in place");
        var current = JObject.Parse(File.ReadAllText(GameSettingsStore.PathFor(root, GameChoice.World)));
        Assert((string)current["SkinFileName"] == asset, "a nested relative theme keeps its correct source path");
        current["SkinFileName"] = "Default.xaml"; File.WriteAllText(GameSettingsStore.PathFor(root, GameChoice.World), current.ToString());
        StorageLayout.Prepare(root);
        Assert(!Directory.Exists(old) && File.Exists(Path.Combine(StorageLayout.LastArchiveDirectory, "SmartHunter/custom.xaml")), "switching away from the old asset allows it to be archived next time");
    }

    internal static void LockedFile()
    {
        string root = Folder(), path = Path.Combine(root, "Log.txt"); File.WriteAllText(path, "retained log");
        using (var locked = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            StorageLayout.Prepare(root);
            Assert(File.Exists(path) && StorageLayout.LastArchiveErrors.Length == 1, "a locked obsolete file does not stop startup and produces a retry report");
        }
        StorageLayout.Prepare(root);
        Assert(!File.Exists(path) && File.ReadAllText(Path.Combine(StorageLayout.LastArchiveDirectory, "Log.txt")) == "retained log", "an unlocked file is archived on the next launch");
    }

    internal static void LinkedArchive()
    {
        string root = Folder(), outside = Folder(), link = Path.Combine(root, "LegacyFiles");
        var info = new ProcessStartInfo("cmd.exe", "/d /c mklink /J \"" + link + "\" \"" + outside + "\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        using (var process = Process.Start(info)) { process.StandardOutput.ReadToEnd(); process.StandardError.ReadToEnd(); Assert(process.WaitForExit(5000) && process.ExitCode == 0, "create a junction fixture"); }
        try
        {
            File.WriteAllText(Path.Combine(root, "Log.txt"), "keep local"); StorageLayout.Prepare(root);
            Assert(File.ReadAllText(Path.Combine(root, "Log.txt")) == "keep local" && !Directory.EnumerateFileSystemEntries(outside).Any() && StorageLayout.LastArchiveErrors.Length == 1, "archiving cannot follow a junction outside the app folder");
        }
        finally { Directory.Delete(link); }
    }
}
