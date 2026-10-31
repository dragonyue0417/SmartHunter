using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using SmartHunter.Game.Config;
using SmartHunter.Game.Data.ViewModels;
using SmartHunter.Game.Helpers;
using SmartHunter.Wilds;
using SmartHunter.World;

namespace SmartHunter.Core
{
    public sealed class WebShareControl { public string Key; public bool Enabled; public long Revision; }
    public static class WebShareIdentity
    {
        static readonly object gate = new object();
        // Sharing belongs to this process, never to a copied installation or
        // settings file. Keep each game's identity stable until the app exits.
        static readonly Dictionary<GameChoice, string> keys = new Dictionary<GameChoice, string>();
        static long revision;
        public static string GetKey(GameChoice game, bool create = true)
        {
            lock (gate)
            {
                if (keys.TryGetValue(game, out string key)) return key;
                if (!create) return null;
                var bytes = new byte[32];
                using (var random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
                key = BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
                keys.Add(game, key);
                return key;
            }
        }
        public static WebShareControl Control(GameChoice game, bool enabled)
        {
            string key = GetKey(game, enabled); if (key == null) return null;
            long before, next;
            do { before = Interlocked.Read(ref revision); next = Math.Max(DateTime.UtcNow.Ticks, before + 1); } while (Interlocked.CompareExchange(ref revision, next, before) != before);
            return new WebShareControl { Key = key, Enabled = enabled, Revision = next };
        }
        public static string Token(string key)
        {
            using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(Encoding.ASCII.GetBytes("SmartHunter-WebShare-v1:" + key))).Replace("-", "").ToLowerInvariant();
        }
        public static string Link(GameChoice game, string server)
        {
            if (game != GameChoice.Wilds) return "";
            if (String.IsNullOrWhiteSpace(server)) server = RelayConfig.DefaultWildsServerUrl;
            var endpoint = WildsRelayClient.Endpoint(server); if (endpoint == null) return "";
            string path = endpoint.AbsolutePath;
            path = path.Substring(0, path.Length - "/wilds/v1/sync".Length) + "/watch";
            // The built-in HTTP relay and HTTPS public viewer are separate
            // endpoints backed by the same server. Keep custom relay prefixes.
            if (RelayConfig.IsBuiltInEndpoint(endpoint, new Uri(RelayConfig.DefaultServerUrl).Host) ||
                RelayConfig.IsBuiltInEndpoint(endpoint, new Uri(RelayConfig.DefaultWatchBaseUrl).Host))
            {
                endpoint = new Uri(RelayConfig.DefaultWatchBaseUrl);
                path = "/watch";
            }
            return new UriBuilder(endpoint) { Path = path, Query = "share=" + Token(GetKey(game)) + "&game=wilds" }.Uri.AbsoluteUri;
        }
    }
    public sealed class WebViewSnapshot
    {
        public List<WebViewMonster> Monsters = new List<WebViewMonster>();
        public List<WebViewPlayer> Players = new List<WebViewPlayer>();
        public List<WebViewEffect> Effects = new List<WebViewEffect>();
    }
    public sealed class WebViewMonster
    {
        public string Id, Name;
        public int TargetKey;
        public float Health, MaxHealth;
        public bool Selected;
        public List<WildsRelayPart> Parts;
        public List<string> PartNames = new List<string>();
        public List<WebViewStatus> Ailments = new List<WebViewStatus>();
    }
    public sealed class WebViewStatus
    {
        public int Id, Count;
        public string Name;
        public bool Active;
        public float Buildup, MaxBuildup, Remaining, MaxDuration;
    }
    public sealed class WebViewPlayer { public int Slot, Damage; public string Name; public bool IsNpc, Available = true; }
    public sealed class WebViewEffect { public int Id; public string Name; public float Remaining, Max; }
    public static class WebViewData
    {
        static float Clean(float value) => Single.IsNaN(value) || Single.IsInfinity(value) ? 0 : Math.Max(0, Math.Min(100000000, value));
        static string Text(string value, int max = 100) => (value ?? "").Substring(0, Math.Min(max, (value ?? "").Length));
        public static WebViewSnapshot Wilds(WildsSnapshot snapshot, List<WildsRelayMonster> source)
        {
            var result = new WebViewSnapshot();
            var usable = new HashSet<int>(source.Select(m => m.TargetKey));
            foreach (var m in snapshot.Monsters.Where(m => usable.Contains(m.TargetKey)).GroupBy(m => m.TargetKey).Select(g => g.First()).Take(64))
            {
                var displayed = OverlayViewModel.Instance.MonsterWidget.Context.Monsters.FirstOrDefault(existing => existing.Address == m.Address);
                var previous = displayed?.StatusEffects.GroupBy(s => s.Index).ToDictionary(g => g.Key, g => g.First());
                string id = WildsMonsterTypes.Id(m.Id);
                string variant = String.Join(" ", new[] { m.Legendary == 2 ? "LOC_MONSTER_VARIANT_ARCH_TEMPERED" : m.Legendary == 1 ? "LOC_MONSTER_VARIANT_TEMPERED" : "", m.Role == 3 ? "LOC_MONSTER_VARIANT_FRENZIED" : "" }.Where(s => s.Length > 0).Select(LocalizationHelper.GetString));
                var item = new WebViewMonster { Id = id, Name = Text((variant + " " + LocalizationHelper.GetMonsterName(id)).Trim()), TargetKey = m.TargetKey, Health = Clean(m.Health), MaxHealth = Clean(m.MaxHealth), Selected = m.Selected };
                item.PartNames = m.Parts.Take(128).Select(p => Text(LocalizationHelper.GetString(p.NameId))).ToList();
                item.Ailments = m.Ailments.Take(64).Select(a => {
                    SmartHunter.Game.Data.MonsterStatusEffect before = null; previous?.TryGetValue(a.Id, out before);
                    return new WebViewStatus { Id = a.Id, Name = Text(LocalizationHelper.GetMonsterStatusEffectName(a.Id)), Active = a.Active,
                        Buildup = Clean(a.Buildup), MaxBuildup = Clean(a.MaxBuildup), Remaining = Clean(a.Remaining), MaxDuration = Clean(a.MaxDuration),
                        Count = Math.Max(0, (before?.TimesActivatedCount ?? 0) + (a.Active && a.Remaining > 0 && (before == null || before.Duration.Current <= 0) ? 1 : 0)) };
                }).ToList();
                result.Monsters.Add(item);
            }
            result.Players = snapshot.Players.Where(p => p.Slot >= 0 && p.Slot < 4).GroupBy(p => p.Slot).Select(g => g.First()).Take(4).Select(p => new WebViewPlayer { Slot = p.Slot, Damage = Math.Max(0, p.Damage), Name = Text(p.IsNpc ? LocalizationHelper.GetString(p.Name) : p.Name, 64), IsNpc = p.IsNpc, Available = p.DamageAvailable }).ToList();
            result.Effects = snapshot.Effects.Select(e => new { Data = e, Index = e.ConfigIndex(ConfigHelper.PlayerData.Values.StatusEffects) })
                .Where(e => e.Index >= 0).Take(256).Select(e => new WebViewEffect { Id = e.Index, Name = Text(LocalizationHelper.GetPlayerStatusEffectName(e.Index)), Remaining = Clean(e.Data.Remaining), Max = Clean(e.Data.Max) }).ToList();
            return result;
        }
        public static WebViewSnapshot World()
        {
            var vm = OverlayViewModel.Instance; var result = new WebViewSnapshot(); int key = 0;
            foreach (var m in vm.MonsterWidget.Context.Monsters.Take(3))
            {
                if (m.Health.Max <= 0) continue;
                var item = new WebViewMonster { Id = m.Id, Name = Text(m.Name), TargetKey = ++key, Health = Clean(m.Health.Current), MaxHealth = Clean(m.Health.Max), Selected = m.IsSelected };
                item.Parts = m.Parts.Take(128).Select((p, slot) => new WildsRelayPart { Slot = slot, NameId = p.NameId ?? "", Severable = p.IsRemovable, Health = Clean(p.Health.Current), MaxHealth = Clean(p.Health.Max), Count = Math.Max(0, p.TimesBrokenCount) }).ToList();
                item.PartNames = m.Parts.Take(128).Select(p => Text(p.Name)).ToList();
                item.Ailments = m.StatusEffects.Take(64).Select(s => new WebViewStatus { Id = s.Index, Name = Text(s.Name), Active = s.Duration.Current > 0,
                    Buildup = Clean(s.Buildup.Current), MaxBuildup = Clean(s.Buildup.Max), Remaining = Clean(s.Duration.Current), MaxDuration = Clean(s.Duration.Max), Count = Math.Max(0, s.TimesActivatedCount) }).ToList();
                result.Monsters.Add(item);
            }
            result.Players = vm.TeamWidget.Context.Players.Where(p => p.Index >= 0 && p.Index < 4).Take(4).Select(p => new WebViewPlayer { Slot = p.Index, Damage = Math.Max(0, p.Damage), Name = Text(p.Name, 64) }).ToList();
            result.Effects = vm.PlayerWidget.Context.StatusEffects.Take(256).Select(s => new WebViewEffect { Id = s.Index, Name = Text(LocalizationHelper.GetPlayerStatusEffectName(s.Index)), Remaining = Clean(s.Time.Current), Max = Clean(s.Time.Max) }).ToList();
            return result;
        }
    }
}
