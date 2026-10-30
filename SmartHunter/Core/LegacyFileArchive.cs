using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace SmartHunter.Core
{
    // Keep retired installation files separate from the active configuration.
    // Nothing in this folder is deleted automatically.
    internal sealed class LegacyFileArchive
    {
        readonly string root;
        public string DirectoryPath { get; private set; }
        public int Count { get; private set; }
        public List<string> Errors { get; } = new List<string>();

        public LegacyFileArchive(string root)
        {
            this.root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        public bool SafePath(string path)
        {
            path = Path.GetFullPath(path);
            if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return false;
            for (string current = path; current != null; current = Path.GetDirectoryName(current))
            {
                if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return false;
                if (String.Equals(current, root, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        bool Referenced(string source)
        {
            foreach (string game in new[] { "World", "Wilds" })
            {
                string settings = Path.Combine(root, "Config", game, "Settings.json");
                if (!File.Exists(settings)) continue;
                JObject document;
                try { document = JObject.Parse(File.ReadAllText(settings)); }
                catch { return true; }
                foreach (string key in new[] { "SkinFileName", "BackgroundImagePath" })
                {
                    string value = (string)document[key];
                    if (String.IsNullOrWhiteSpace(value)) continue;
                    if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && !uri.IsFile) continue;
                    string active;
                    try { active = StorageLayout.Resolve(root, value); }
                    catch (Exception error) when (error is ArgumentException || error is NotSupportedException || error is PathTooLongException) { return true; }
                    if (String.Equals(active, source, StringComparison.OrdinalIgnoreCase) || active.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            return false;
        }

        public bool Move(string source)
        {
            source = Path.GetFullPath(source);
            try
            {
                if (!File.Exists(source) && !Directory.Exists(source)) return false;
                if (!SafePath(source) || Referenced(source)) return false;
                string relative = source.Substring(root.Length + 1);
                if (relative.Split(Path.DirectorySeparatorChar)[0].Equals("LegacyFiles", StringComparison.OrdinalIgnoreCase)) return false;
                string batch = DirectoryPath ?? Path.Combine(root, "LegacyFiles", DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                string destination = Path.Combine(batch, relative);
                if (!SafePath(destination)) throw new IOException("Archive path contains a reparse point.");
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                if (Directory.Exists(source)) Directory.Move(source, destination);
                else File.Move(source, destination);
                DirectoryPath = batch;
                Count++;
                return true;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is System.Security.SecurityException)
            {
                Errors.Add(source + ": " + error.Message);
                return false;
            }
        }

        public string NestedInstallation()
        {
            string folder = Path.Combine(root, "SmartHunter"), exe = Path.Combine(folder, "SmartHunter.exe");
            if (!Directory.Exists(folder) || !SafePath(exe) || !File.Exists(exe) ||
                Directory.Exists(Path.Combine(folder, ".git")) || File.Exists(Path.Combine(folder, ".git")) ||
                Directory.GetFiles(folder, "*.csproj").Any() || Directory.GetFiles(folder, "*.sln").Any() ||
                !File.Exists(Path.Combine(folder, "Config.json")) ||
                typeof(LegacyFileArchive).Assembly.Location.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return null;
            try
            {
                if (!Version.TryParse(FileVersionInfo.GetVersionInfo(exe).FileVersion, out var version) || version > typeof(LegacyFileArchive).Assembly.GetName().Version) return null;
                return folder;
            }
            catch (IOException) { return null; }
        }
    }
}
