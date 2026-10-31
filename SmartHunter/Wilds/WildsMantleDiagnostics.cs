using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using SmartHunter.Core;

namespace SmartHunter.Wilds
{
    public sealed class WildsMantleSample
    {
        public DateTime CapturedAtUtc = DateTime.UtcNow;
        public string Status = "Unavailable", ArrayPath, ArrayNativeType;
        public int? SelectedMantle;
        public int ArrayLength;
        public List<WildsMantleReading> Mantles = new List<WildsMantleReading>();
        public List<string> Objects = new List<string>();
        public List<WildsNetworkTypeMetadata> Types = new List<WildsNetworkTypeMetadata>();
        public WildsMantleProbeReport Probe;
    }
    public sealed class WildsMantleReading
    {
        public int Slot;
        public string NameStringId, Status, NativeType, Flags;
        public bool? InUse;
        public float? ActiveRemaining, CooldownRemaining, ActiveMaximum, CooldownMaximum;
    }

    internal sealed class WildsMantleDiagnostics
    {
        public int Schema = 2;
        public string AppVersion = typeof(WildsReader).Assembly.GetName().Version.ToString();
        public string GameVersion;
        public string Purpose = "LocalOnly: mantle counters and native getter evidence; never uploaded";
        public WildsMantleSample LastWithTimers;
        public WildsMantleSample LastWithCandidates;
        public List<WildsMantleSample> Samples = new List<WildsMantleSample>();
        internal void Add(WildsMantleSample sample)
        {
            if (sample == null) return;
            if (sample.Mantles.Any(m => m.Status == "Active" || m.Status == "Cooldown")) LastWithTimers = sample;
            if (sample.Probe?.Objects.Any(o => o.Type.Contains("ActiveSkill") || o.Type.Contains("ASkill")) == true) LastWithCandidates = sample;
            if (Samples.Count >= 96) Samples.RemoveAt(0);
            Samples.Add(sample);
        }
        internal void Save(string path)
        {
            const int maximumBytes = 8 * 1024 * 1024;
            string json;
            while (true)
            {
                json = JsonConvert.SerializeObject(this, Formatting.Indented);
                int bytes = Encoding.UTF8.GetByteCount(json);
                if (bytes <= maximumBytes || Samples.Count <= 1) break;
                int remove = Math.Min(Samples.Count - 1, Math.Max(1, (int)Math.Ceiling(Samples.Count * (1 - maximumBytes / (double)bytes))));
                Samples.RemoveRange(0, remove);
            }
            AtomicFile.Write(path, json);
        }
    }
}
