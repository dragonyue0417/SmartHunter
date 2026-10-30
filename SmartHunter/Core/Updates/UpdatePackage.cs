using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace SmartHunter.Core.Updates
{
    public sealed class UpdatePackage
    {
        public Version Version;
        public string Repository, Branch, Revision, ManifestJson;
        public Dictionary<string, string> Files;
        public Dictionary<string, long> Sizes;
        public long Size => Sizes.Values.Sum();
        public static readonly string[] PayloadFiles = { "SmartHunter.exe", "SmartHunter.exe.config", "Themes/Default.xaml", "Newtonsoft.Json.dll", "Microsoft.Xaml.Behaviors.dll", "OxyPlot.dll", "OxyPlot.Wpf.dll" };
        public const string ManifestName = "UpdateManifest.json";
        public const long MaximumFileSize = 32 * 1024 * 1024, MaximumTotalSize = 64 * 1024 * 1024;
        public static bool ValidRepository(string value) => Regex.IsMatch(value ?? "", @"^[A-Za-z0-9][A-Za-z0-9-]{0,38}/[A-Za-z0-9][A-Za-z0-9_.-]{0,99}$");
        public static bool ValidBranch(string value) => Regex.IsMatch(value ?? "", @"^[A-Za-z0-9][A-Za-z0-9._/-]{0,199}$") &&
            !value.Contains("..") && !value.Contains("//") && value.Split('/').All(p => !p.StartsWith(".") && !p.EndsWith(".") && !p.EndsWith(".lock", StringComparison.OrdinalIgnoreCase) && p.Length > 0);
        public static bool ValidRevision(string value) => Regex.IsMatch(value ?? "", "^[a-fA-F0-9]{40}$");
        public static UpdatePackage Parse(string json, bool requireSizes = true)
        {
            var manifest = JObject.Parse(json, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
            string versionText = (string)manifest["Version"];
            if ((string)manifest["Product"] != "SmartHunter-Unified" || !Regex.IsMatch(versionText ?? "", @"^\d+\.\d+\.\d+\.\d+$") || !Version.TryParse(versionText, out var version)) throw new InvalidDataException("Wrong update product or version.");
            var hashes = manifest["Files"] as JObject; var sizes = manifest["Sizes"] as JObject;
            bool Matches(JObject values) => values != null && values.Properties().Select(p => p.Name).OrderBy(n => n).SequenceEqual(PayloadFiles.OrderBy(n => n));
            if (!Matches(hashes) || (requireSizes || sizes != null) && !Matches(sizes)) throw new InvalidDataException("Wrong update file list.");
            var result = new UpdatePackage { Version = version, ManifestJson = json, Files = new Dictionary<string, string>(), Sizes = new Dictionary<string, long>() };
            foreach (string name in PayloadFiles)
            {
                if (hashes[name].Type != JTokenType.String || !Regex.IsMatch((string)hashes[name], "^[a-fA-F0-9]{64}$")) throw new InvalidDataException("Invalid update file checksum.");
                result.Files[name] = ((string)hashes[name]).ToLowerInvariant();
                if (sizes == null) continue;
                if (sizes[name].Type != JTokenType.Integer || !Int64.TryParse(sizes[name].ToString(), out long size) || size <= 0 || size > MaximumFileSize) throw new InvalidDataException("Invalid update file size.");
                result.Sizes[name] = size;
            }
            if (result.Size > MaximumTotalSize) throw new InvalidDataException("Update exceeds the total size limit.");
            return result;
        }
        public Uri FileUri(string name)
        {
            if (!ValidRepository(Repository) || !ValidRevision(Revision) || name != ManifestName && !PayloadFiles.Contains(name)) throw new InvalidDataException("Invalid update file location.");
            return new Uri("https://raw.githubusercontent.com/" + Repository + "/" + Revision + "/release/" + name);
        }
    }
    public sealed class UpdateSourceMissingException : IOException
    {
        public UpdateSourceMissingException(string source) : base(source) { }
    }
}
