using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SmartHunter.Wilds
{
    public sealed class WildsScanSummary
    {
        public string CapturedAtUtc, GameVersion;
        public long ElapsedMilliseconds;
        public Dictionary<string, string> ResolvedRvas;
        public static WildsScanSummary From(WildsScanReport report) => new WildsScanSummary
        { CapturedAtUtc = report.CapturedAtUtc, GameVersion = report.GameVersion, ElapsedMilliseconds = report.ElapsedMilliseconds,
            ResolvedRvas = new Dictionary<string, string>(report.ResolvedRvas) };
    }
    public static class WildsScanHistory
    {
        // Evidence only. Nothing here is passed to the scanner or address resolver.
        public static WildsScanSummary Read(string path)
        {
            try
            {
                if (!File.Exists(path) || new FileInfo(path).Length > 1024 * 1024) return null;
                var saved = JObject.Parse(File.ReadAllText(path));
                var item = (string)saved["Status"] == "Resolved" ? saved : saved["PreviousSuccessfulScan"] as JObject;
                var summary = item?.ToObject<WildsScanSummary>();
                if (summary?.ResolvedRvas == null || summary.ResolvedRvas.Count > 16 || summary.GameVersion?.Length > 32) return null;
                foreach (var pair in summary.ResolvedRvas)
                    if (pair.Key.Length > 64 || pair.Value == null || !pair.Value.StartsWith("0x", StringComparison.Ordinal) ||
                        !UInt64.TryParse(pair.Value.Substring(2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out _)) return null;
                return summary;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is JsonException || e is ArgumentException) { return null; }
        }
    }
}
