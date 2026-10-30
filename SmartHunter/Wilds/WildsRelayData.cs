using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;

namespace SmartHunter.Wilds
{
    public sealed class WildsSessionIdentity
    {
        public string Session;
        public bool? IsHost;
        public int? HostIndex;
    }
    // Wire DTOs deliberately exclude process addresses, player data, monster
    // current health and active timers. MaxHealth is an instance descriptor only.
    public sealed class WildsRelayRequest
    {
        public int Protocol = 1, StageId;
        public string Session, Client, Role;
        public bool AckOnly;
        public SmartHunter.Core.WebViewSnapshot View;
        public SmartHunter.Core.WebShareControl WebShare;
        public List<WildsRelayMonster> Monsters = new List<WildsRelayMonster>();
    }
    public sealed class WildsRelayResponse
    {
        public string Status, Session, Role;
        public int Protocol, Peers;
        public bool HasHost, HasPlayer;
        public int? AgeMs;
        public List<WildsRelayMonster> Monsters;
    }
    public sealed class WildsRelayMonster
    {
        public int Id, TargetKey, Size, Role, Legendary;
        public float MaxHealth;
        public List<WildsRelayPart> Parts = new List<WildsRelayPart>();
        public List<WildsRelayAilment> Ailments = new List<WildsRelayAilment>();
    }
    public sealed class WildsRelayPart
    {
        public int Slot, Count;
        public string NameId;
        public bool Severable;
        public float Health, MaxHealth;
    }
    public sealed class WildsRelayAilment
    {
        public int Id;
        public bool Active;
        public float Buildup, MaxBuildup;
    }
    public sealed class WildsRelayMatchReport
    {
        public DateTime CapturedAtUtc = DateTime.UtcNow;
        public int LocalMonsters, SourceMonsters, Applied, Skipped, Approximate;
        public int MissingDescriptor, AmbiguousDescriptor, InvalidLocalKey, BindingConflict, NoUsableFields;
        public List<WildsRelayMatchPair> Pairs = new List<WildsRelayMatchPair>();
    }
    public sealed class WildsRelayMatchPair
    {
        public int Id, SourceKey, LocalKey;
        public bool Approximate;
    }
    // Keys are stable only inside their own game process. Remember associations
    // for this quest so a remaining old actor cannot be rebound to a new wave.
    public sealed class WildsRelayPairing
    {
        readonly Dictionary<int, string> sourceToLocal = new Dictionary<int, string>();
        readonly Dictionary<string, int> localToSource = new Dictionary<string, int>();
        public void Clear() { sourceToLocal.Clear(); localToSource.Clear(); }
        public bool IsBound(WildsMonsterSnapshot local, WildsRelayMonster source)
            => sourceToLocal.TryGetValue(source.TargetKey, out var value) && value == local.TargetKey + ":" + local.Address.ToString("X", CultureInfo.InvariantCulture);
        public bool Bind(WildsMonsterSnapshot local, WildsRelayMonster source)
        {
            string key = local.TargetKey + ":" + local.Address.ToString("X", CultureInfo.InvariantCulture);
            if (sourceToLocal.TryGetValue(source.TargetKey, out var oldLocal)) return oldLocal == key;
            if (localToSource.ContainsKey(key) || sourceToLocal.Count >= 512) return false;
            sourceToLocal.Add(source.TargetKey, key); localToSource.Add(key, source.TargetKey); return true;
        }
    }
    public static class WildsRelayData
    {
        static bool Bounded(float number) => WildsReader.Valid(number) && number >= 0 && number <= 100000000;
        public static bool Valid(WildsRelayMonster m) => m != null && m.Id >= 0 && m.Id <= 4096 && m.Size >= 1 && m.Size <= 1000 && m.Role >= 0 && m.Role <= 32 && m.Legendary >= 0 && m.Legendary <= 8 &&
            Bounded(m.MaxHealth) && m.MaxHealth > 0 && m.Parts != null && m.Parts.Count <= 128 && m.Ailments != null && m.Ailments.Count <= 64 &&
            m.Parts.All(p => p != null && p.Slot >= 0 && p.Slot < 128 && p.NameId != null && p.NameId.Length <= 100 && p.Count >= 0 && p.Count <= 1000000 && Bounded(p.MaxHealth) && p.MaxHealth > 0 && Bounded(p.Health) && p.Health <= p.MaxHealth * 1.01f) &&
            m.Parts.Select(p => p.Slot).Distinct().Count() == m.Parts.Count &&
            m.Ailments.All(a => a != null && a.Id >= 0 && a.Id <= 255 && Bounded(a.Buildup) && Bounded(a.MaxBuildup)) && m.Ailments.Select(a => a.Id).Distinct().Count() == m.Ailments.Count;
        static string Key(int id, int size, int role, int legendary, float max) => id + ":" + size + ":" + role + ":" + legendary + ":" + max.ToString("R", CultureInfo.InvariantCulture);
        static string Key(WildsRelayMonster m) => Key(m.Id, m.Size, m.Role, m.Legendary, m.MaxHealth);
        static string Key(WildsMonsterSnapshot m) => Key(m.Id, m.Size, m.Role, m.Legendary, m.MaxHealth);
        public static WildsRelayRequest Create(WildsSnapshot snapshot, string session, string client, bool isHost, bool ailments)
        {
            var request = new WildsRelayRequest { Session = session, Client = client, Role = isHost ? "host" : "guest", StageId = snapshot.StageId };
            if (!isHost) return request;
            foreach (var m in snapshot.Monsters.Take(64))
            {
                var item = new WildsRelayMonster { Id = m.Id, TargetKey = m.TargetKey, Size = m.Size, Role = m.Role, Legendary = m.Legendary, MaxHealth = m.MaxHealth };
                item.Parts = m.Parts.Take(128).Select(p => new WildsRelayPart { Slot = p.Slot, NameId = p.NameId, Severable = p.Severable, MaxHealth = p.MaxHealth, Health = p.Health, Count = p.Count }).ToList();
                if (ailments) item.Ailments = m.Ailments.Where(a => a.Id >= 2 && !a.Active && a.MaxBuildup > 0).Take(64).Select(a => new WildsRelayAilment { Id = a.Id, Active = false, Buildup = a.Buildup, MaxBuildup = a.MaxBuildup }).ToList();
                if (Valid(item)) request.Monsters.Add(item);
            }
            return request;
        }
        // Paired live logs proved TargetKey differs between host and guest.
        // Match descriptors, trying stable one-to-one associations for duplicates. Local keys
        // and addresses are used solely to track an established association.
        public static int Merge(WildsSnapshot local, List<WildsRelayMonster> remote, bool ailments, out int skipped)
            => Merge(local, remote, ailments, out skipped, out _, null);
        public static int Merge(WildsSnapshot local, List<WildsRelayMonster> remote, bool ailments, out int skipped, out WildsRelayMatchReport report, WildsRelayPairing pairing = null)
        {
            skipped = 0; int applied = 0;
            report = new WildsRelayMatchReport { LocalMonsters = local.Monsters.Count, SourceMonsters = remote?.Count ?? 0 };
            if (remote == null || remote.Count > 64 || remote.Any(m => !Valid(m))) return 0;
            var groups = remote.GroupBy(Key).ToDictionary(g => g.Key, g => g.ToArray());
            var sourceKeys = remote.GroupBy(m => m.TargetKey).ToDictionary(g => g.Key, g => g.Count());
            foreach (var group in local.Monsters.GroupBy(Key))
            {
                var actors = group.ToArray();
                if (actors.Any(m => m.TargetKey == 0 || m.TargetKey == -1)) { skipped += actors.Length; report.InvalidLocalKey += actors.Length; continue; }
                if (!groups.TryGetValue(group.Key, out var candidates)) { skipped += actors.Length; report.MissingDescriptor += actors.Length; continue; }
                bool approximate = actors.Length != 1 || candidates.Length != 1;
                var available = candidates.Where(c => c.TargetKey != 0 && c.TargetKey != -1 && sourceKeys[c.TargetKey] == 1).OrderBy(c => c.TargetKey).ToList();
                // Stable per-process key order is a temporary heuristic, not a shared entity ID.
                // Reserve previous bindings first, then pair remaining actors one to one.
                var ordered = actors.OrderByDescending(m => available.Any(c => pairing != null && pairing.IsBound(m, c))).ThenBy(m => m.TargetKey).ThenBy(m => m.Address);
                foreach (var m in ordered)
                {
                var data = available.FirstOrDefault(c => pairing != null && pairing.IsBound(m, c));
                if (data == null) data = available.FirstOrDefault(c => pairing == null || pairing.Bind(m, c));
                if (data == null) { skipped++; report.BindingConflict++; continue; }
                available.Remove(data);
                bool updated = false;
                foreach (var p in data.Parts)
                {
                    var part = m.Parts.FirstOrDefault(x => x.Slot == p.Slot);
                    if (part != null && (SmartHunter.Game.Helpers.TextKeyNames.Canonical(part.NameId) != SmartHunter.Game.Helpers.TextKeyNames.Canonical(p.NameId) || part.Severable != p.Severable)) continue;
                    if (part == null)
                    {
                        part = new WildsPartSnapshot { Slot = p.Slot, Address = 0xF000000000000000UL + (ulong)p.Slot, NameId = p.NameId, Severable = p.Severable };
                        m.Parts.Add(part);
                    }
                    part.Health = p.Health; part.MaxHealth = p.MaxHealth; part.Count = p.Count;
                    updated = true;
                }
                if (ailments) foreach (var a in data.Ailments.Where(a => a.Id >= 2 && !a.Active && a.MaxBuildup > 0))
                {
                    var value = m.Ailments.FirstOrDefault(x => x.Id == a.Id);
                    if (value == null) { value = new WildsAilmentSnapshot { Id = a.Id, Address = 0xE000000000000000UL + (ulong)a.Id }; m.Ailments.Add(value); }
                    if (!value.Active) { value.Buildup = a.Buildup; value.MaxBuildup = a.MaxBuildup; updated = true; }
                }
                if (updated)
                {
                    applied++; if (approximate) report.Approximate++;
                    report.Pairs.Add(new WildsRelayMatchPair { Id = m.Id, SourceKey = data.TargetKey, LocalKey = m.TargetKey, Approximate = approximate });
                }
                else { skipped++; report.NoUsableFields++; }
                }
            }
            report.Applied = applied; report.Skipped = skipped;
            return applied;
        }
    }
}
