using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SmartHunter.Core;
using SmartHunter.Game.Config;
using SmartHunter.Game.Data;
using SmartHunter.Game.Data.ViewModels;
using SmartHunter.Game.Helpers;
using SmartHunter.Wilds;
using SmartHunter.World;
using static TestSupport;

static class MantleTests
{
    sealed class Fixture
    {
        public readonly FakeGame G = new FakeGame();
        public readonly ulong[] Tools;
        public readonly ulong Array;
        public readonly WildsReader Reader;
        public Fixture(bool oldPath = true)
        {
            Tools = Enumerable.Range(0, 5).Select(i => G.M.Alloc()).ToArray();
            Array = G.M.Array(Tools);
            if (oldPath) G.O(G.Local, "PlayerSpecializedTools", Array);
            Reader = new WildsReader(G.M, G.Addresses, G.Base);
        }
        public void Timer(int slot, bool worn, float remaining, float max)
        {
            G.M.Byte(Tools[slot] + 0x33, worn ? (byte)1 : (byte)0);
            G.M.Ptr(Tools[slot] + 0x10, G.Encrypted(remaining));
            G.M.Ptr(Tools[slot] + 0x18, G.Encrypted(worn ? (slot == 0 ? 300 : 600) : max));
            G.M.Ptr(Tools[slot] + (worn ? 0x28UL : 0x20UL), G.Encrypted(max));
        }
        public void Select(int value)
        {
            G.M.Int(G.F("SaveManager", "SaveIndex"), 0);
            ulong save = G.M.Alloc(), equipment = G.M.Alloc();
            G.M.Ptr(G.F("SaveManager", "SaveData"), save);
            G.O(save, "SaveSpecializedTool", equipment); G.M.Int(equipment + 0x40, value);
        }
        public void Type(ulong obj, string name, int size)
        {
            ulong info = G.M.Alloc(), definition = G.M.Alloc(), type = G.M.Alloc(), text = G.M.Alloc();
            G.M.Ptr(obj, info); G.M.Ptr(info, definition); G.M.Ptr(definition + 0x40, info); G.M.Ptr(definition + 0x38, type);
            G.M.Int(definition + 0x14, size); G.M.Int(type + 0x30, size); G.M.Ptr(type + 0x20, text);
            G.M.Put(text, Encoding.ASCII.GetBytes(name + "\0"));
        }
    }
    internal static void Timers()
    {
        foreach (int slot in Enumerable.Range(0, 5))
        {
            var f = new Fixture();
            f.Timer(slot, true, 42.5f, 90);
            var active = f.Reader.Read().Effects.Single();
            Assert(active.NameStringId == WildsMantles.Key(slot, false) && active.Remaining == 42.5f && active.Max == 90, "every active mantle retains its actual counter");
            f.Timer(slot, false, 321, 600);
            var cooldown = f.Reader.Read().Effects.Single();
            Assert(cooldown.NameStringId == WildsMantles.Key(slot, true) && cooldown.Remaining == 321 && cooldown.Max == 600, "the shared live counter changes from active duration to cooldown");
            f.Timer(slot, false, 0, 600);
            Assert(!f.Reader.Read().Effects.Any(), "ready mantle removes countdown at zero");
        }
    }
    internal static void SharedCountdown()
    {
        var f = new Fixture();
        var applier = new WildsSnapshotApplier();
        var context = OverlayViewModel.Instance.PlayerWidget.Context;
        try
        {
            // Captured 1.42.0.3 values: +0x18 stays at 600 while +0x10
            // changes from active duration to cooldown, then reaches zero.
            f.Timer(1, false, 0, 600);
            Assert(!f.Reader.Read().Effects.Any(), "a ready mantle with a retained 600-second reference has no countdown");
            f.Timer(1, true, 89.65035f, 90);
            var active = f.Reader.Read(); applier.Apply(active);
            Assert(active.Effects.Single().Remaining == 89.65035f && context.StatusEffects.Single().Time.Current == 89.65035f, "captured worn timer reaches the player overlay");
            float previous = 600;
            foreach (float remaining in new[] { 598.767944f, 596.6597f, 594.5769f, 575.8234f, 0.25f })
            {
                f.Timer(1, false, remaining, 600);
                var snapshot = f.Reader.Read(); applier.Apply(snapshot);
                var effect = snapshot.Effects.Single();
                Assert(effect.NameStringId == WildsMantles.Key(1, true) && effect.Remaining == remaining && effect.Remaining < previous && effect.Max == 600, "captured cooldown decreases using the live counter instead of the fixed reference");
                Assert(context.StatusEffects.Count == 1 && context.StatusEffects[0].Time.Current == remaining && context.StatusEffects[0].Name == LocalizationHelper.GetString(WildsMantles.Key(1, true)), "cooldown replaces the worn row without retaining the active duration");
                Assert(WebViewData.Wilds(snapshot, new System.Collections.Generic.List<WildsRelayMonster>()).Effects.Single().Remaining == remaining, "web sharing retains the same live cooldown");
                previous = remaining;
            }
            f.Timer(1, false, 0, 600);
            var ready = f.Reader.Read(collectMantleDiagnostics: true); applier.Apply(ready);
            var row = ready.Mantles.Mantles.Single(m => m.Slot == 1);
            Assert(!ready.Effects.Any() && context.StatusEffects.Count == 0 && !WebViewData.Wilds(ready, new System.Collections.Generic.List<WildsRelayMonster>()).Effects.Any(), "cooldown completion removes local and web countdowns even though the reference remains 600");
            Assert(row.Status == "Ready" && row.CooldownRemaining == 0 && row.ActiveRemaining == null && row.CooldownMaximum == 600, "diagnostics label the shared timer according to the current mantle phase");
            foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, -1, 607 })
            {
                f.Timer(1, false, invalid, 600);
                Assert(!f.Reader.Read().Effects.Any(), "an invalid live cooldown cannot fall back to the retained reference");
            }
            f.G.M.Ptr(f.Tools[1] + 0x10, 0x123456780);
            Assert(!f.Reader.Read().Effects.Any(), "an unreadable live cooldown cannot reuse a previous timer or the fixed reference");
        }
        finally { applier.Clear(); }
    }
    internal static void Selection()
    {
        var f = new Fixture(); f.Timer(0, false, 100, 300); f.Timer(1, false, 200, 600); f.Timer(2, false, 300, 600);
        Assert(f.Reader.Read().Effects.Count == 3, "missing save selection cannot discard valid non-ghillie counters");
        f.Select(2);
        Assert(f.Reader.Read().Effects.Count == 2 && f.Reader.Read().Effects.Any(e => e.NameStringId == WildsMantles.Key(1, true)), "known loadout excludes inactive unequipped cooldowns");
        f.Timer(2, true, 12, 80);
        Assert(f.Reader.Read().Effects.Any(e => e.NameStringId == WildsMantles.Key(2, false)), "live worn state wins over a retained save selection");
        f.Select(99);
        Assert(f.Reader.Read().Effects.Count == 3, "out-of-range selection is unavailable, never treated as a valid slot");
    }
    internal static void Invalid()
    {
        var f = new Fixture(); f.Timer(0, true, 10, 120);
        Assert(f.Reader.Read().Effects.Count == 1, "initial live timer");
        f.G.M.Ptr(f.Tools[0] + 0x10, 0x123456780);
        Assert(!f.Reader.Read().Effects.Any(), "unreadable encrypted value never reuses the last timer");
        foreach (float value in new[] { float.NaN, float.PositiveInfinity, -1, 999999, 150 })
        {
            f.Timer(0, true, value, 120);
            Assert(!f.Reader.Read().Effects.Any(), "invalid or over-maximum time excluded");
        }
        f.Timer(0, true, 10, 120); f.G.M.Byte(f.Tools[0] + 0x33, 2);
        Assert(!f.Reader.Read().Effects.Any(), "invalid native boolean cannot imply an equipped mantle");
        f.G.M.Int(f.Array + 0x1C, int.MaxValue);
        Assert(!f.Reader.Read().Effects.Any(), "array count cannot cause unbounded equipment reads");
    }
    internal static void TypedRecovery()
    {
        var f = new Fixture(false); f.Timer(1, true, 55, 90);
        ulong character = f.G.M.Alloc(), controller = f.G.M.Alloc();
        f.G.M.Ptr(f.G.Local + 0x18, character); f.G.M.Ptr(character + 0x3B0, controller); f.G.M.Ptr(controller + 0xC0, f.Array);
        f.Type(character, "app.HunterCharacter", 0x500); f.Type(controller, "app.mcActiveSkillController", 0x100);
        f.Type(f.Array, "app.mcActiveSkillController.cActiveSkill[]", 32);
        var recovered = f.Reader.Read(collectMantleDiagnostics: true);
        Assert(recovered.Effects.Single().Remaining == 55 && recovered.Mantles.ArrayPath == "Local+0x18+0x3B0+0xC0", "changed component offsets recover through exact native array type");
        int coldCalls = f.Reader.LastReadStatistics.Calls;
        ulong replacement = f.G.M.Array(f.Tools); f.G.M.Ptr(controller + 0xC0, replacement); f.Timer(1, true, 44, 90);
        Assert(f.Reader.Read().Effects.Single().Remaining == 44 && f.Reader.LastReadStatistics.Calls < coldCalls, "warm reads follow fresh pointers through cached offsets without repeated graph search");
        f.G.M.Ptr(controller + 0xC0, 0);
        Assert(!f.Reader.Read().Effects.Any(), "removed controller array cannot leave a stale countdown");
    }
    internal static void Definitions()
    {
        var document = new JObject { ["StatusEffects"] = new JArray(new JObject { ["GroupId"] = "Custom", ["NameStringId"] = WildsMantles.Key(1, false), ["KeepMe"] = true }) };
        string upgraded = TextKeyNames.UpgradeData(document.ToString(), true);
        var rows = (JArray)JObject.Parse(upgraded)["StatusEffects"];
        Assert(rows.Count == 10 && (string)rows[0]["GroupId"] == "Custom" && (bool)rows[0]["KeepMe"], "only missing mantle definitions are appended, preserving custom rows");
        Assert(TextKeyNames.UpgradeData(upgraded, true) == upgraded, "repeat startup cannot duplicate equipment definitions");
        Assert(JToken.DeepEquals(document, JObject.Parse(TextKeyNames.UpgradeData(document.ToString()))), "World data is not given Wilds equipment");
        var original = ConfigHelper.PlayerData.Values;
        var applier = new WildsSnapshotApplier();
        try
        {
            ConfigHelper.PlayerData.Values = new PlayerDataConfig(GameChoice.Wilds) { StatusEffects = rows.ToObject<StatusEffectConfig[]>() };
            var f = new Fixture(); f.Timer(1, true, 30, 90); applier.Apply(f.Reader.Read());
            var displayed = OverlayViewModel.Instance.PlayerWidget.Context.StatusEffects.Single();
            Assert(displayed.Index == 0 && displayed.Time.Current == 30, "equipment resolves configured keys instead of assuming catalog array position");
            var web = WebViewData.Wilds(f.Reader.Read(), new System.Collections.Generic.List<WildsRelayMonster>()).Effects.Single();
            Assert(web.Id == 0 && web.Remaining == 30 && web.Name == LocalizationHelper.GetString(WildsMantles.Key(1, false)), "web-view payload uses the same custom mantle identity and name as the local overlay");
            ConfigHelper.PlayerData.Values.StatusEffects = new StatusEffectConfig[0];
            applier.Apply(f.Reader.Read());
            Assert(!OverlayViewModel.Instance.PlayerWidget.Context.StatusEffects.Any(), "explicitly missing definitions cannot cause an index exception");
        }
        finally { applier.Clear(); ConfigHelper.PlayerData.Values = original; }
    }
    internal static void RecoveryBounds()
    {
        var unrelated = new Fixture(); unrelated.Timer(0, true, 40, 120); unrelated.Type(unrelated.Array, "other.Unrelated[]", 32);
        Assert(!unrelated.Reader.Read().Effects.Any(), "the legacy path also rejects an explicitly different native array type");
        var f = new Fixture(false); f.Timer(1, true, 40, 90);
        ulong character = f.G.M.Alloc(), controller = f.G.M.Alloc();
        f.G.M.Ptr(f.G.Local + 0x18, character); f.G.M.Ptr(character + 0x3B0, controller); f.G.M.Ptr(controller + 0xC0, f.Array);
        f.Type(character, "app.HunterCharacter", 0x500); f.Type(controller, "app.mcActiveSkillController", 0x100);
        f.Type(f.Array, "other.Unrelated[]", 32);
        Assert(!f.Reader.Read().Effects.Any(), "an unrelated typed array cannot be accepted by the recovery search");
        for (int i = 0; i < 100; i++)
        {
            ulong noise = f.G.M.Alloc(); f.Type(noise, "app.OtherComponent", 0x300);
            f.G.M.Ptr(character + 0x20 + (ulong)i * 8, noise);
            f.G.M.Ptr(noise + 0x20, character);
        }
        var fresh = new WildsReader(f.G.M, f.G.Addresses, f.G.Base);
        fresh.Read(); int cold = fresh.LastReadStatistics.Calls;
        fresh.Read(); int warm = fresh.LastReadStatistics.Calls;
        Assert(cold - warm <= 520 && warm < cold, "cycles, candidate count and memory-read budget stay bounded; failed searches wait before retrying");
    }
    internal static void Diagnostics()
    {
        var f = new Fixture(); f.Timer(1, false, 123, 600);
        Assert(f.Reader.Read().Mantles == null && !new DebugConfig().CollectMantleDiagnostics, "release defaults collect no equipment diagnostic payload");
        var sample = f.Reader.Read(collectMantleDiagnostics: true).Mantles;
        Assert(sample.Mantles.Single(m => m.Slot == 1).CooldownRemaining == 123 && sample.ArrayLength == 5, "opt-in report records actual decrypted counters and selection availability");
        Assert(!JsonConvert.SerializeObject(f.Reader.Read(collectMantleDiagnostics: true)).Contains("ArrayPath"), "native diagnostic evidence cannot leak through snapshot serialization");
        f.G.M.Byte(f.G.Stage + 0xC5, 1);
        var normal = f.Reader.Read(); var camp = f.Reader.Read(collectMantleDiagnostics: true);
        Assert(camp.Mantles != null && !camp.Effects.Any() && JToken.DeepEquals(JObject.FromObject(normal), JObject.FromObject(camp)), "camp diagnostics do not change the production overlay or sharing payload");
        var report = new WildsMantleDiagnostics(); report.Add(sample);
        for (int i = 0; i < 110; i++) report.Add(new WildsMantleSample());
        Assert(report.Samples.Count == 96 && ReferenceEquals(report.LastWithTimers, sample), "bounded history retains last usable values after leaving the quest");
        string path = StorageLayout.Resolve(Folder(), "Equipment.Wilds.json"); report.Save(path); report.Save(path);
        Assert(path.Contains(Path.Combine("Diagnostics", "Wilds")) && JObject.Parse(File.ReadAllText(path))["LastWithTimers"] != null && Directory.GetFiles(Path.GetDirectoryName(path)).Length == 1, "classified diagnostic file is atomic and leaves no backup or temporary files");
    }
    internal static void Display()
    {
        var f = new Fixture(); f.Timer(0, true, 42.1f, 120); f.Timer(1, false, 123.4f, 600);
        var applier = new WildsSnapshotApplier(); applier.Apply(f.Reader.Read());
        var context = OverlayViewModel.Instance.PlayerWidget.Context;
        var host = new ContentControl { Content = context, DataContext = context };
        var window = new Window { Content = host, Width = 420, Height = 100, Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
        try
        {
            window.Show(); Render(host, "mantle-timers.png", 420, 100);
            var texts = Visuals<TextBlock>(host).Select(t => t.Text).ToArray();
            Assert(context.StatusEffects.Count == 2 && context.StatusEffects.All(e => e.IsVisible && e.GroupId == "Equipment"), "both mantle duration and cooldown use the existing player overlay group");
            Assert(texts.Any(t => t.Contains("43") && t.Contains("秒")) && texts.Any(t => t.Contains("124") && t.Contains("秒")), "real overlay template shows countdown seconds, rounded upward above zero: " + String.Join(" / ", texts));
            f.Timer(0, true, 0, 120); f.Timer(1, false, 0, 600); applier.Apply(f.Reader.Read());
            Assert(context.StatusEffects.Count == 0, "overlay removes both active and cooling rows when ready");
        }
        finally { window.Close(); applier.Clear(); }
    }
    internal static void NativeProbe()
    {
        var f = new Fixture(false);
        f.Timer(1, true, 12.5f, 90);
        ulong character = f.G.M.Alloc(), controller = f.G.M.Alloc();
        f.G.M.Ptr(f.G.Local + 0x18, character); f.G.M.Ptr(character + 0x388, controller); f.G.M.Ptr(controller + 0xB8, f.Array);
        f.Type(f.G.Local, "app.cPlayerManageInfo", 0x60); f.Type(character, "app.HunterCharacter", 0x500);
        f.Type(controller, "app.mcActiveSkillController", 0x100); f.Type(f.Array, "app.mcActiveSkillController.cActiveSkill[]", 32);
        foreach (ulong tool in f.Tools) f.Type(tool, "app.mcActiveSkillController.cActiveSkill", 0x80);
        var snapshot = f.Reader.Read(collectMantleDiagnostics: true);
        var probe = snapshot.Mantles.Probe;
        Assert(probe.Source == "AOB:PlayerManager" && probe.PlayerManagerRva == "0x" + f.G.Addresses.Address("PlayerManager", 0).ToString("X"), "probe root is the current relocated AOB result");
        Assert(probe.Objects.Any(o => o.Type == "app.mcActiveSkillController") && probe.Objects.Count(o => o.Type == "app.mcActiveSkillController.cActiveSkill") == 5, "the five native mantle objects and the controller are recorded independently of the former field chain");
        var target = probe.Objects.Single(o => o.Address == "0x" + f.Tools[1].ToString("X"));
        Assert(target.Bytes != null && target.Values.Any(v => v.Storage == "EncryptedFloatCandidate" && v.Value == 12.5f), "native object bytes and independently encrypted timer candidates are retained");
        Assert(probe.ReadAttempts <= probe.ReadLimit && probe.BytesRead <= 512 * 1024 && probe.ObjectCount <= probe.ObjectLimit, "probe work stays inside the per-capture limits");
        Assert(!JsonConvert.SerializeObject(probe).Contains("測試 Hunter"), "diagnostic graph does not export the player-name string");
    }
    internal static void DeepProbe()
    {
        var f = new Fixture(false); f.Timer(4, false, 110, 600);
        f.Type(f.G.Local, "app.cPlayerManageInfo", 0x60);
        ulong previous = f.G.Local;
        for (int i = 0; i < 5; i++)
        {
            ulong next = f.G.M.Alloc(); f.Type(next, i == 4 ? "app.mcActiveSkillController" : "app.cPlayerComponent", 0x80);
            f.G.M.Ptr(previous + 0x58, next); f.G.M.Ptr(next + 0x70, f.G.Local); previous = next;
        }
        f.G.M.Ptr(previous + 0x58, f.Array); f.Type(f.Array, "app.mcActiveSkillController.cActiveSkill[]", 32);
        foreach (ulong tool in f.Tools) f.Type(tool, "app.mcActiveSkillController.cActiveSkill", 0x80);
        var normal = f.Reader.Read(); var captured = f.Reader.Read(collectMantleDiagnostics: true);
        Assert(captured.Mantles.Probe.Objects.Any(o => o.Type == "app.mcActiveSkillController.cActiveSkill" && o.Depth == 7), "a longer native route can reach equipment beyond the old four-link fallback");
        Assert(JToken.DeepEquals(JObject.FromObject(normal), JObject.FromObject(captured)), "new unverified diagnostic candidates never become overlay countdowns");
    }
    internal static void ParameterProbe()
    {
        var f = new Fixture(false);
        ulong table = f.G.M.Alloc(), value = f.G.M.Alloc();
        ulong slot = f.G.M.Field(f.G.A("PlayerManager"), new[] { 0x70, 0xA8, 0x288, 0x50, 0x10 });
        f.G.M.Ptr(slot, table); f.G.M.Ptr(table + 0x20, value); f.G.M.Float(value + 0x14, 600); f.G.M.Float(value + 0x18, 90);
        var captured = f.Reader.Read(collectMantleDiagnostics: true);
        var parameter = captured.Mantles.Probe.Parameters.Single();
        Assert(parameter.Cooldown == 600 && parameter.Duration == 90 && parameter.Purpose.StartsWith("ConfigurationOnly"), "the supplied CT's parameter route is recorded as configuration evidence");
        Assert(!captured.Effects.Any(), "full duration and cooldown parameters cannot masquerade as live remaining time");
        Assert(captured.Mantles.Probe.Paths.Any(p => p.Status == "NullPointer" || p.Status == "UnreadablePointer"), "failed routes retain the actual failing pointer step");
    }
    internal static void ProbeRetention()
    {
        var report = new WildsMantleDiagnostics();
        var usable = new WildsMantleSample { Probe = new WildsMantleProbeReport { Objects = { new WildsMantleObjectCheck { Type = "app.mcActiveSkillController" } } } };
        report.Add(usable);
        for (int i = 0; i < 100; i++) report.Add(new WildsMantleSample());
        Assert(report.Schema == 2 && report.Samples.Count == 96 && ReferenceEquals(report.LastWithCandidates, usable), "schema 2 retains a discovered controller even after later empty captures");
    }
    internal static void RealManagedArrays()
    {
        var f = new Fixture();
        f.Type(f.Array, "via.clr.ManagedObject", 16);
        foreach (ulong tool in f.Tools) f.Type(tool, "app.mcActiveSkillController.cActiveSkill", 0x80);
        f.Timer(0, true, 75, 120); f.Timer(1, false, 250, 600);
        var current = f.Reader.Read();
        Assert(current.Effects.Count == 2 && current.Effects.Any(e => e.NameStringId == WildsMantles.Key(0, false) && e.Remaining == 75) &&
            current.Effects.Any(e => e.NameStringId == WildsMantles.Key(1, true) && e.Remaining == 250), "the user's actual ManagedObject array representation accepts verified mantle elements");
        f.Type(f.Tools[2], "app.UnrelatedObject", 0x80);
        Assert(!f.Reader.Read().Effects.Any(), "a changed element type rejects the array instead of trusting a cached actor address");
        var other = new Fixture(); other.Type(other.Array, "via.clr.ManagedObject", 16); other.Timer(0, true, 75, 120);
        Assert(!other.Reader.Read().Effects.Any(), "generic managed metadata alone cannot authorize untyped timer fields");
    }
}
