using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SmartHunter.Core;
using SmartHunter.Core.Config;
using SmartHunter.Game;
using SmartHunter.Game.Config;
using SmartHunter.Game.Data.ViewModels;
using SmartHunter.Game.Helpers;
using SmartHunter.Ui.Windows;
using SmartHunter.Wilds;
using SmartHunter.World;
using static TestSupport;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

static class WildsReaderTests
{
internal static void AesVectors()
    {
        using (var input = new BinaryReader(File.OpenRead(Path.Combine(Root, "aes-vectors.bin"))))
        {
            int count = input.ReadInt32(); Assert(count == 1000, "reference vector count");
            for (int i = 0; i < count; i++)
            {
                var cipher = new WildsCrypto(input.ReadBytes(16), input.ReadBytes(16));
                float actual = cipher.Decode(input.ReadBytes(16)); uint expected = input.ReadUInt32();
                Assert(BitConverter.ToUInt32(BitConverter.GetBytes(actual), 0) == expected, "AES reference vector " + i);
            }
        }
    }
internal static void CryptoInputs()
    {
        using (var input = new BinaryReader(File.OpenRead(Path.Combine(Root, "aes-vectors.bin"))))
        {
            input.ReadInt32();
            byte[] key = input.ReadBytes(16), round = input.ReadBytes(16), encrypted = input.ReadBytes(16);
            uint expected = input.ReadUInt32();
            var cipher = new WildsCrypto(key, round);
            Array.Clear(key, 0, key.Length); Array.Clear(round, 0, round.Length);
            Assert(BitConverter.ToUInt32(BitConverter.GetBytes(cipher.Decode(encrypted)), 0) == expected, "mutating caller buffers cannot change a prepared decoder");
            byte[] unchanged = (byte[])encrypted.Clone();
            cipher.Decode(encrypted);
            Assert(encrypted.SequenceEqual(unchanged), "decoding never changes the captured game block");
            Assert(float.IsNaN(cipher.Decode(null)) && float.IsNaN(cipher.Decode(new byte[15])) && float.IsNaN(cipher.Decode(new byte[17])), "incomplete blocks cannot be decoded");
            bool rejected = false;
            try { new WildsCrypto(new byte[15], new byte[16]); } catch (ArgumentException) { rejected = true; }
            Assert(rejected, "malformed health vectors fail before any read");
        }
    }
internal static void NativeRead()
    {
        IntPtr data = Marshal.AllocHGlobal(16);
        try
        {
            Marshal.WriteInt32(data, 123456);
            using (var memory = new WildsProcessMemory(Process.GetCurrentProcess().Id))
            {
                var view = new WildsMemoryView(memory);
                Assert(view.Int(unchecked((ulong)data.ToInt64())) == 123456, "native scalar");
                Assert(view.Bytes(1, 16) == null && float.IsNaN(view.Float(1)), "failed read never decodes stale buffer");
            }
        }
        finally { Marshal.FreeHGlobal(data); }
    }
internal static void BuiltInLayout()
    {
        var expected = LayoutFixtures.ExpectedOffsets(); var actual = WildsMemoryLayout.CopyOffsets();
        Assert(actual.Count == 40 && actual.Keys.OrderBy(k => k).SequenceEqual(expected.Keys.OrderBy(k => k)), "complete built-in fields");
        foreach (var pair in expected) Assert(actual[pair.Key].SequenceEqual(pair.Value), "field chain " + pair.Key);
        actual["MonsterHealth"][0] = -1;
        Assert(WildsMemoryLayout.CopyOffsets()["MonsterHealth"][0] == 0x30, "profile fields cannot be mutated by copies");
        Assert(!Directory.Exists(Path.Combine(Root, "Address")) || !Directory.EnumerateFileSystemEntries(Path.Combine(Root, "Address")).Any(), "no external address files");
    }
internal static void UnknownVersion()
    {
        Assert(!WildsMemoryLayout.Supports("99.0.0.0") && !WildsMemoryLayout.Supports("1.42"), "unknown / incomplete version mislabeled as verified");
        Assert(WildsMemoryLayout.Supports("1.42.0.2"), "supported exact patch");
    }
internal static void PointerChains()
    {
        var memory = new FakeMemory(); ulong p = memory.Alloc(), a = memory.Alloc(), b = memory.Alloc(); var view = new WildsMemoryView(memory);
        memory.Ptr(p, a); memory.Ptr(a + 0x10, b);
        Assert(view.Field(p, 0x10, 0x20) == b + 0x20, "dereference then add");
        memory.Ptr(p + 0x10, a); memory.Ptr(a + 0x20, b);
        Assert(view.Object(p, 0x10, 0x20) == b, "add then dereference");
        Assert(view.Field(0xFEED, 0x10, 0x20) == 0 && view.Object(0, 0x10) == 0, "null chain");
    }
internal static void BoundedArrays()
    {
        var game = new FakeGame(); game.Monster(16);
        game.M.Int(game.MonsterHeader + 8, int.MaxValue);
        Assert(game.Read().Monsters.Count == 0, "dynamic array cap");
        var view = new WildsMemoryView(game.M); ulong p = game.M.Array(1, 2); game.M.Int(p + 0x1C, int.MaxValue);
        Assert(view.Array(p).Length == 0 && view.Bytes(p, int.MaxValue) == null, "managed array cap");
    }
internal static void NotReady()
    {
        var game = new FakeGame(); game.M.Put(game.A("HealthEncryptionKey"), new byte[16]);
        Assert(!game.Read().IsReady, "uninitialized key retried");
        var reader = new WildsReader(new FakeMemory(), LayoutFixtures.Create(), 0x140000000);
        Assert(!reader.Read().IsReady, "missing local player");
    }
internal static void DynamicPopulation()
    {
        foreach (int count in new[] { 0, 1, 4, 12, 32, 64 })
        {
            var game = new FakeGame();
            for (int i = 0; i < count; i++) game.Monster(16, 1000 - i);
            game.Monster(20, category: 1); game.Monster(21, enabled: false);
            var data = game.Read();
            Assert(data.IsReady && data.Monsters.Count == count, "dynamic count " + count);
            Assert(data.Monsters.All(m => m.Id == 16) && data.Monsters.Select(m => m.Address).Distinct().Count() == count, "same species has separate actors, no small/disabled monsters");
            Assert(data.Monsters.Select(m => m.Health).SequenceEqual(Enumerable.Range(0, count).Select(i => (float)(1000 - i))), "every actor health updated");
        }
    }
internal static void DynamicLifecycle()
    {
        var game = new FakeGame(); var applier = new WildsSnapshotApplier(); applier.Clear();
        for (int i = 0; i < 12; i++) game.Monster(16, 1000 - i);
        ulong[] original = game.Monsters.ToArray(); var context = OverlayViewModel.Instance.MonsterWidget.Context;
        game.Select(original[11]); applier.Apply(game.Read());
        Assert(context.Monsters.Count == 12 && context.Monsters[0].Address == original[11], "selected actor outside original three is first");
        var survivor = context.Monsters.Single(m => m.Address == original[8]);
        game.Select(original[8]); game.SetHealth(original[8], 321); applier.Apply(game.Read());
        Assert(ReferenceEquals(survivor, context.Monsters[0]) && survivor.Health.Current == 321 && context.Monsters.Count == 12, "selection reorders existing models without merging same-species actors");
        game.SetPopulation(original[2], original[8], original[11], original[8]); applier.Apply(game.Read());
        Assert(context.Monsters.Count == 3 && ReferenceEquals(survivor, context.Monsters[0]), "departed actors removed and duplicate pointers ignored");
        game.Monster(17); game.Monster(18); applier.Apply(game.Read());
        Assert(context.Monsters.Count == 5 && context.Monsters.Any(m => m.Id == "em151_00"), "new actors appear after despawn");
        game.SetHealth(original[8], 0); applier.Apply(game.Read());
        Assert(context.Monsters.Count == 4 && context.Monsters.All(m => m.Address != original[8]), "dead actor removed independently");
        game.SetPopulation(); applier.Apply(game.Read()); Assert(context.Monsters.Count == 0, "empty live population clears all actors"); applier.Clear();
    }
internal static void Selection()
    {
        var game = new FakeGame(); game.Monster(16); game.Monster(16); game.Monster(17); ulong selected = game.Monster(18); game.Select(selected);
        Assert(game.Read().Monsters[0].Address == selected, "selected fourth monster has priority");
        var noPriority = game.Read(false, false);
        Assert(noPriority.Monsters.Count == 4 && noPriority.Monsters[0].Address != selected && noPriority.Monsters.Single(m => m.Address == selected).Selected, "priority can be disabled independently of tracking and target identity");
        Assert(game.Read(true).Monsters.Single().Address == selected, "selected-only retains one instance");
        Assert(game.Read(true, false).Monsters.Single().Address == selected, "selected-only works with priority off");
    }
internal static void InvalidHealth()
    {
        var game = new FakeGame(); game.Monster(16, float.NaN); game.Monster(17, -1); game.Monster(18, 1500); game.Monster(19, 0); game.Monster(20, 700);
        Assert(game.Read().Monsters.Single().Health == 700, "reject NaN, negative, over-max and dead actors");
    }
internal static void SafeZone()
    {
        var game = new FakeGame(); game.Monster(16); game.M.Byte(game.Stage + 0xC5, 1);
        Assert(!game.Read().InHuntingZone && game.Read().Monsters.Count == 0, "safe area");
        game.M.Byte(game.Stage + 0xC5, 0); game.M.Int(game.F("PauseManager", "PauseState"), 6);
        Assert(!game.Read().InHuntingZone, "loading screen");
    }
internal static void Parts()
    {
        var game = new FakeGame(); for (int i = 0; i < 5; i++) game.Monster(17);
        ulong monster = game.Monster(16), component = game.M.Alloc(), part = game.M.Alloc(), association = game.M.Alloc();
        game.O(monster, "MonsterParts", component);
        game.M.Ptr(component + 0x78, game.M.Array(part)); game.M.Ptr(component + 0x20, game.M.Array(association));
        game.M.Ptr(part + 0x10, game.Encrypted(500)); game.M.Ptr(part + 0x28, game.Encrypted(250));
        game.M.Int(association + 0x10, 2); game.M.Int(association + 0x14, 3); game.M.Byte(association + 0x32, 1);
        var p = game.Read().Monsters.Single(m => m.Address == monster).Parts.Single();
        Assert(p.MaxHealth == 1500 && p.Health == 1250 && p.NameId.StartsWith("LOC_PART_"), "sixth monster also receives full part details and break multipliers");
    }
internal static void Ailments()
    {
        var game = new FakeGame(); ulong monster = game.Monster(16), ailment = game.M.Alloc(), buildup = game.M.Alloc();
        game.O(monster, "MonsterStatusEffects", game.M.Array(ailment)); game.M.Ptr(ailment + 0x20, buildup);
        game.M.Int(ailment + 0xB4, 3); game.M.Int(ailment + 0x58, 1); game.M.Float(ailment + 0x48, 5); game.M.Float(ailment + 0x4C, 20);
        game.M.Float(buildup + 0x14, 70); game.M.Float(buildup + 0x1C, 100);
        var a = game.Read().Monsters.Single().Ailments.Single(); Assert(a.Id == 3 && a.Remaining == 15 && a.Buildup == 70 && a.MaxBuildup == 100, "status timing");
        int unknown=Array.FindIndex(WildsCatalog.Instance.StatusEffects,2,v=>v.NameStringId.StartsWith("LOC_STATUS_EFFECT_UNKNOWN",StringComparison.Ordinal));
        Assert(unknown>1,"unknown native status fixture exists");game.M.Int(ailment+0xB4,unknown);
        Assert(game.Read().Monsters.Single().Ailments.Count==0,"LOC rename preserves unknown native status filtering");
    }
internal static void AilmentExpiry()
    {
        var applier = new WildsSnapshotApplier(); applier.Clear(); var data = WildsSnapshotApplier.Preview(1); var ailment = data.Monsters[0].Ailments[0];
        ailment.Active = true; ailment.Remaining = 10; ailment.MaxDuration = 30; applier.Apply(data);
        var status = OverlayViewModel.Instance.MonsterWidget.Context.Monsters[0].StatusEffects.Single();
        Assert(status.Duration.Current == 10 && status.TimesActivatedCount == 1, "active timer");
        ailment.Active = false; ailment.Remaining = ailment.MaxDuration = ailment.Buildup = ailment.MaxBuildup = 0; applier.Apply(data);
        Assert(status.Duration.Current == 0 && status.Duration.Max == 0 && status.Buildup.Current == 0, "valid 0/0 clears stale timer and buildup");
        ailment.Active = true; ailment.Remaining = 8; ailment.MaxDuration = 15; ailment.Buildup = 70; ailment.MaxBuildup = 100; applier.Apply(data); applier.Apply(data);
        Assert(status.Duration.Current == 8 && status.TimesActivatedCount == 2, "next activation recognized once after clearing"); applier.Clear();
    }
internal static void PartySlots()
    {
        var game = new FakeGame(); game.Monster(16);
        ulong limited = game.M.Alloc(), members = game.M.Alloc(), indexes = game.M.Alloc(), remote = game.M.Alloc(), context = game.M.Alloc(), network = game.M.Alloc();
        game.M.Ptr(game.F("PlayerManager", "PlayerParty"), limited);
        game.M.Ptr(limited + 0x10, members); game.M.Int(limited + 0x18, 2); game.M.Int(members + 0x1C, 4); game.M.Int(members + 0x20, 0); game.M.Int(members + 0x24, 2);
        game.M.Ptr(game.F("PlayerManager", "PlayerQuestPlayerIndexes"), indexes); game.M.Int(indexes + 0x1C, 4); game.M.Int(indexes + 0x30, 0); game.M.Int(indexes + 0x30 + 2 * 0x18, 3);
        ulong list = game.F("PlayerManager", "PlayerList"), selfWrapper = game.M.Alloc(), remoteWrapper = game.M.Alloc();
        game.M.Ptr(list, selfWrapper); game.M.Ptr(list + 16, remoteWrapper); game.M.Ptr(selfWrapper + 0x10, game.Local); game.M.Int(selfWrapper + 0x4C, 6);
        game.M.Ptr(remoteWrapper + 0x10, remote); game.M.Int(remoteWrapper + 0x4C, 6);
        game.O(remote, "PlayerContext", context); game.M.Ptr(context + 0x38, game.M.String("Remote 玩家")); game.M.Ptr(context + 0x40, network); game.M.Int(network + 0x28, 2);
        ulong selfNet = game.M.Alloc(); game.M.Ptr(game.Context + 0x40, selfNet);
        ulong quest = game.M.Alloc(); game.M.Ptr(game.F("QuestManager", "QuestCurrentInformation"), quest); game.M.Float(quest + 0xE0, 1000); game.M.Float(quest + 0xE4, 300000);
        game.M.Float(game.F("QuestManager", "QuestLocalPlayerDamage"), 100);
        game.M.Float(game.F("QuestManager", "QuestRemotePlayerDamage") + 3 * 0x78, 250);
        var players = game.Read().Players;
        Assert(players.Count == 2 && players.Single(p => p.Slot == 3).Damage == 250 && players.Single(p => p.Slot == 0).Damage == 100, "remote mapping and quest damage");
    }
internal static void History()
    {
        var game = new FakeGame(); game.Monster(16);
        ulong history = game.M.Alloc(), hit = game.M.Alloc(), unrelated = game.M.Alloc(); game.O(game.Local, "PlayerDamageHistory", history);
        game.M.Ptr(history + 0x10, game.M.Array(hit, unrelated)); game.M.Int(history + 0x18, 2);
        game.M.Float(hit + 0x10, 456); game.M.Int(hit + 0x18, 116); game.M.Float(unrelated + 0x10, 9000); game.M.Int(unrelated + 0x18, 9999);
        Assert(game.Read().Players.Single().Damage == 456, "target filtered history");
    }
internal static void PlayerEffects()
    {
        var game = new FakeGame(); game.Monster(16);
        ulong p = game.M.Alloc(); game.O(game.Local, "PlayerConsumableEffects", p);
        var catalog = WildsCatalog.Instance.PlayerStatusEffects;
        int first = Array.FindIndex(catalog, b => b.Source == (uint)WildsEffectSource.Consumable && b.HasMaxDuration);
        int offset = WildsStatusEffectConfig.Hex(catalog[first].TimerOffset); game.M.Float(p + (ulong)offset, 120); game.M.Float(p + (ulong)offset + 4, 42);
        var effect = game.Read().Effects.Single(e => e.Index == first);
        Assert(effect.Remaining == 42 && effect.Max == 120, "consumable max/current offsets");
        ulong songs = game.M.Alloc(), timers = game.M.Alloc(), maxima = game.M.Alloc(); game.O(game.Local, "PlayerHornEffects", songs);
        game.M.Ptr(songs + 0x10, timers); game.M.Ptr(songs + 0x18, maxima); game.M.Int(timers + 0x1C, 59); game.M.Int(maxima + 0x1C, 59);
        game.M.Float(timers + 0x20 + 11 * 4, 30); game.M.Float(maxima + 0x20 + 11 * 4, 90);
        int song = Array.FindIndex(catalog, b => b.Source == (uint)WildsEffectSource.HuntingHorn && b.Index == 11);
        Assert(game.Read().Effects.Single(e => e.Index == song).Remaining == 30, "sparse song IDs retain original array index");
        int skill = Array.FindIndex(catalog, b => b.Source == (uint)WildsEffectSource.Skill);
        ulong skills = game.M.Alloc(), skillData = game.M.Alloc(); game.O(game.Local, "PlayerSkillEffects", skills);
        game.M.Ptr(skills + (ulong)WildsStatusEffectConfig.Hex(catalog[skill].TimerOffset), skillData);
        game.M.Float(skillData + 0x10, 15); game.M.Float(skillData + 0x14, 60);
        foreach (var condition in catalog[skill].Conditions) game.M.Int(skillData + (ulong)WildsStatusEffectConfig.Hex(condition.Offsets.Single()), condition.IntValue ?? 0);
        Assert(game.Read().Effects.Any(e => e.Index == skill && e.Remaining == 15), "skill timer schema");
    }
internal static void SnapshotUpdates()
    {
        var applier = new WildsSnapshotApplier(); var snapshot = WildsSnapshotApplier.Preview(); applier.Apply(snapshot);
        Assert(OverlayViewModel.Instance.MonsterWidget.Context.Monsters.Count == 6, "initial six");
        snapshot.Monsters[0].Legendary = 2; snapshot.Monsters[0].Role = 3; applier.Apply(snapshot);
        Assert(OverlayViewModel.Instance.MonsterWidget.Context.Monsters.First().Name.Contains("歷戰王") && OverlayViewModel.Instance.MonsterWidget.Context.Monsters.First().Name.Contains("狂龍化"), "Wilds variant names");
        snapshot.Monsters.RemoveAt(1); snapshot.Players.RemoveAt(1); applier.Apply(snapshot);
        Assert(OverlayViewModel.Instance.MonsterWidget.Context.Monsters.Count == 5 && OverlayViewModel.Instance.TeamWidget.Context.Players.Count == 2, "despawn and leave cleanup");
        var oldPlayer = OverlayViewModel.Instance.TeamWidget.Context.Players.First(p => p.Index == 0);
        snapshot.Players[0].Address = 0xFFAA; applier.Apply(snapshot);
        Assert(!ReferenceEquals(oldPlayer, OverlayViewModel.Instance.TeamWidget.Context.Players.First(p => p.Index == 0)), "same-name replacement has a fresh damage history");
        applier.Apply(new WildsSnapshot()); Assert(OverlayViewModel.Instance.MonsterWidget.Context.Monsters.Count == 0 && OverlayViewModel.Instance.TeamWidget.Context.Players.Count == 0, "clear on read loss");
    }
internal static void ConditionalEffects()
    {
        var game = new FakeGame(); game.Monster(0);
        ulong debuffs = game.M.Alloc(), frenzy = game.M.Alloc(), fire = game.M.Alloc(); game.O(game.Local, "PlayerDebuffs", debuffs);
        game.M.Ptr(debuffs + 0x10 + 5 * 8, frenzy); game.M.Int(frenzy + 0x38, 1); game.M.Byte(frenzy + 0x2F, 1); game.M.Float(frenzy + 0x3C, 20); game.M.Float(frenzy + 0x40, 60);
        game.M.Ptr(debuffs + 0x10 + 11 * 8, fire); game.M.Byte(fire + 0x2F, 1); game.M.Float(fire + 0x38, 9); game.M.Float(fire + 0x18, 20);
        ulong palico = game.M.Alloc(); game.O(game.Local, "PlayerPalicoEffects", palico); game.M.Int(palico + 0x1C, 6); game.M.Float(palico + 0x20 + 4, 17);
        ulong skills = game.M.Alloc(), warCry = game.M.Alloc(); game.O(game.Local, "PlayerSkillEffects", skills); game.M.Ptr(skills + 0x88, warCry);
        game.M.Int(warCry + 0x1C, 1077936128); game.M.Float(warCry + 0x10, 15); game.M.Float(warCry + 0x14, 60);
        var definitions = WildsCatalog.Instance.PlayerStatusEffects;
        int Id(string key) => Array.FindIndex(definitions, d => d.NameStringId == key);
        bool Has(WildsSnapshot snapshot, string key) => snapshot.Effects.Any(e => e.Index == Id(key));
        var first = game.Read();
        Assert(Has(first, "LOC_STATUS_EFFECT_FRENZY") && !Has(first, "LOC_STATUS_EFFECT_FRENZY_IMMUNITY"), "frenzy and immunity share a native slot but retain distinct conditions");
        Assert(Has(first, "LOC_STATUS_EFFECT_WAR_CRY_I") && !Has(first, "LOC_STATUS_EFFECT_WAR_CRY_II"), "first skill variant uses the original 32-bit condition");
        var timer = first.Effects.Single(e => e.Index == Id("LOC_STATUS_EFFECT_FIREBLIGHT")); var buildup = first.Effects.Single(e => e.Index == Id("LOC_STATUS_EFFECT_FIREBLIGHT_BUILDUP"));
        Assert(timer.Remaining == 9 && timer.Infer && buildup.Remaining == 20 && buildup.Max == 100 && !buildup.Infer, "boolean JSON fields preserve inferred timers and fixed buildup bounds");
        Assert(first.Effects.Single(e => e.Index == Id("LOC_STATUS_EFFECT_SPORENET_PURRTECTION")).Remaining == 17, "Palico songs retain their sparse native array index");
        game.M.Int(frenzy + 0x38, 2); game.M.Int(warCry + 0x1C, 1086324736); var second = game.Read();
        Assert(!Has(second, "LOC_STATUS_EFFECT_FRENZY") && Has(second, "LOC_STATUS_EFFECT_FRENZY_IMMUNITY") && !Has(second, "LOC_STATUS_EFFECT_WAR_CRY_I") && Has(second, "LOC_STATUS_EFFECT_WAR_CRY_II"), "switching native conditions replaces both variants correctly");
    }
internal static void Preview()
    {
        var data = WildsSnapshotApplier.Preview(); Assert(data.Monsters.Count == 6 && data.Monsters.All(m => WildsCatalog.Instance.Monster(m.Id) != null), "real Wilds schemas with six instances");
        Assert(UiText.Instance["LOC_UI_STATE_PREVIEW"].Contains("範例"), "explicit sample-data indicator");
    }
}

static class QuestLifecycleTests
{
    static ulong Quest(FakeGame game)
    {
        game.Monster(16);
        ulong quest = game.M.Alloc();
        game.M.Ptr(game.F("QuestManager", "QuestCurrentInformation"), quest);
        game.M.Float(quest + 0xE0, 30000); game.M.Float(quest + 0xE4, 300000);
        game.M.Float(game.F("QuestManager", "QuestLocalPlayerDamage"), 100);
        ulong history = game.M.Alloc(), hit = game.M.Alloc(); game.O(game.Local, "PlayerDamageHistory", history);
        game.M.Ptr(history + 0x10, game.M.Array(hit)); game.M.Int(history + 0x18, 1);
        game.M.Float(hit + 0x10, 9000); game.M.Int(hit + 0x18, 116);
        return quest;
    }
internal static void QuestEnd()
    {
        foreach (int state in new[] { 1, 2, 3, 4 })
        {
            var game = new FakeGame(); ulong quest = Quest(game); var reader = new WildsReader(game.M, game.Addresses, game.Base);
            var apply = new WildsSnapshotApplier(); var active = reader.Read(); apply.Apply(active);
            var player = OverlayViewModel.Instance.TeamWidget.Context.Players.Single();
            Assert(player.Damage == 100, "quest source before completion");
            game.M.Int(quest + 0x108, state); game.M.Float(game.F("QuestManager", "QuestLocalPlayerDamage"), 125);
            var final = reader.Read(); apply.Apply(final);
            Assert(final.IsQuestDamageFrozen && final.Players.Single().Damage == 125, "final hit retained for state " + state);
            Assert(ReferenceEquals(player, OverlayViewModel.Instance.TeamWidget.Context.Players.Single()), "no result chart reset");
            int points = player.DamagePoints.Count;
            game.M.Float(game.F("QuestManager", "QuestLocalPlayerDamage"), 0);
            for (int i = 0; i < 3; i++) { final = reader.Read(); apply.Apply(final); }
            game.M.Ptr(game.F("QuestManager", "QuestCurrentInformation"), 0); final = reader.Read(); apply.Apply(final);
            Assert(final.QuestIdentity == quest && player.Damage == 125 && player.DamagePoints.Count == points, "teardown cannot switch to 9000 history or reset totals");
            Assert(player.DamageFraction == 1, "final percentages remain stable"); apply.Clear();
        }
        var failureGame = new FakeGame(); ulong failedQuest = Quest(failureGame); var failureReader = new WildsReader(failureGame.M, failureGame.Addresses, failureGame.Base);
        failureReader.Read(); failureGame.M.Int(failedQuest + 0x114, 2);
        Assert(failureReader.Read().IsQuestDamageFrozen, "failure flag can precede success state");
    }
internal static void QuestRestart()
    {
        var game = new FakeGame(); ulong quest = Quest(game); var reader = new WildsReader(game.M, game.Addresses, game.Base);
        reader.Read(); game.M.Int(quest + 0x108, 2); reader.Read();
        game.M.Int(quest + 0x108, 0); game.M.Float(quest + 0xE0, 0);
        game.M.Float(game.F("QuestManager", "QuestLocalPlayerDamage"), 0);
        var next = reader.Read();
        Assert(!next.IsQuestDamageFrozen && next.Players.Single().Damage == 0, "reused quest pointer starts at real zero, not old history");
        game.M.Float(game.F("QuestManager", "QuestLocalPlayerDamage"), 50); reader.Read();
        game.M.Float(game.F("QuestManager", "QuestLocalPlayerDamage"), 0);
        Assert(reader.Read().Players.Single().Damage == 50, "transient zero before the end flag cannot erase a total");
        game.M.Float(game.F("QuestManager", "QuestLocalPlayerDamage"), float.NaN);
        Assert(reader.Read().Players.Single().Damage == 50, "missing quest total keeps the last same-player value");
        game.M.Ptr(game.F("QuestManager", "QuestCurrentInformation"), 0);
        Assert(reader.Read().Players.Single().Damage == 50, "missing quest structure freezes the result");
        game.M.Int(game.Stage + 0x88, 12); reader.Read(); game.M.Int(game.Stage + 0x88, 1);
        Assert(reader.Read().Players.Single().Damage == 0, "returning after settlement excludes old free-roam hit history");
    }
internal static void Roster()
    {
        var tracker = new WildsQuestDamage();
        var snapshot = new WildsSnapshot { QuestIdentity = 1, QuestElapsed = 20000 };
        snapshot.Players.Add(new WildsPlayerSnapshot { Address = 10, Slot = 0, Name = "self", Damage = 100, DamageAvailable = true });
        snapshot.Players.Add(new WildsPlayerSnapshot { Address = 20, Slot = 2, Name = "remote", Damage = 300, DamageAvailable = true });
        snapshot.Players.Add(new WildsPlayerSnapshot { Address = 30, Slot = 3, Name = "npc", IsNpc = true, Damage = 50, DamageAvailable = true });
        tracker.Update(snapshot, true, true, 7);
        snapshot.Players.Clear(); tracker.Update(snapshot, true, false, 0); // mission field can clear before the quest object
        Assert(snapshot.Players.Count == 3 && snapshot.Players.Sum(p => p.Damage) == 450, "departed quest members remain in final fractions");
        snapshot.Players[0].Damage = 999999;
        tracker.Update(snapshot, false, false, 7);
        Assert(snapshot.Players[0].Damage == 100, "snapshots cannot mutate the result cache");
    }
}

static class QuestReturnTests
{
    static ulong Quest(FakeGame game)
    {
        game.Monster(16);ulong quest=game.M.Alloc();game.M.Ptr(game.F("QuestManager","QuestCurrentInformation"),quest);
        game.M.Float(quest+0xE0,30000);game.M.Float(quest+0xE4,300000);game.M.Float(game.F("QuestManager","QuestLocalPlayerDamage"),100);
        return quest;
    }
    static void Camp(FakeGame game,bool value) => game.M.Byte(game.Stage+0xC5,(byte)(value?1:0));
    static void Loading(FakeGame game,bool value) => game.M.Int(game.F("PauseManager","PauseState"),value?6:0);
    static void Finish(FakeGame game,ulong quest,bool fail=false)
    {game.M.Int(quest+(fail?0x114UL:0x108UL),2);game.M.Float(game.F("QuestManager","QuestLocalPlayerDamage"),125);}
internal static void ActiveCamp()
    {
        var game=new FakeGame();Quest(game);var reader=new WildsReader(game.M,game.Addresses,game.Base);var apply=new WildsSnapshotApplier();
        try {apply.Apply(reader.Read(showInCamp:true));var player=OverlayViewModel.Instance.TeamWidget.Context.Players.Single();int points=player.DamagePoints.Count;
            Camp(game,true);apply.Apply(reader.Read(showInCamp:true));Assert(ReferenceEquals(player,OverlayViewModel.Instance.TeamWidget.Context.Players.Single()) && player.Damage==100 && player.DamagePoints.Count==points,"camp during active quest keeps totals and chart");
            apply.Apply(reader.Read(showInCamp:false));Assert(ReferenceEquals(player,OverlayViewModel.Instance.TeamWidget.Context.Players.Single()),"hiding camp overlay does not erase quest history");
            Loading(game,true);apply.Apply(reader.Read(showInCamp:true));Loading(game,false);Camp(game,false);apply.Apply(reader.Read(showInCamp:true));
            Assert(ReferenceEquals(player,OverlayViewModel.Instance.TeamWidget.Context.Players.Single()) && player.Damage==100,"mid-quest fast travel is not a settlement return");
        }finally{apply.Clear();}
    }
internal static void ReturnToCamp()
    {
        var game=new FakeGame();ulong quest=Quest(game);var reader=new WildsReader(game.M,game.Addresses,game.Base);var apply=new WildsSnapshotApplier();
        try {apply.Apply(reader.Read(showInCamp:true));Finish(game,quest);var result=reader.Read(showInCamp:true);apply.Apply(result);var final=OverlayViewModel.Instance.TeamWidget.Context.Players.Single();
            Assert(result.IsQuestDamageFrozen && final.Damage==125,"last hit retained during settlement");
            Loading(game,true);apply.Apply(reader.Read(showInCamp:true));Assert(final.Damage==125,"loading alone does not clear final result");
            Loading(game,false);Camp(game,true);result=reader.Read(showInCamp:true);apply.Apply(result);
            Assert(!result.IsQuestDamageFrozen && result.Players.All(p=>p.Damage==0) && OverlayViewModel.Instance.TeamWidget.Context.Players.All(p=>p.DamagePoints.Count==0 && p.DamageFraction==0) && !ReferenceEquals(final,OverlayViewModel.Instance.TeamWidget.Context.Players.Single()),"return to camp clears totals, fractions and prior chart together");
            for(int i=0;i<3;i++) Assert(reader.Read(showInCamp:true).Players.All(p=>p.Damage==0),"stale quest buffer cannot restore old statistics");
        }finally{apply.Clear();}
    }
internal static void ReturnToHub()
    {
        foreach(bool fail in new[]{false,true}) foreach(bool show in new[]{false,true})
        {
            var game=new FakeGame();ulong quest=Quest(game);var reader=new WildsReader(game.M,game.Addresses,game.Base);var apply=new WildsSnapshotApplier();
            try {apply.Apply(reader.Read(showInCamp:show));Finish(game,quest,fail);apply.Apply(reader.Read(showInCamp:show));
                game.M.Int(game.Stage+0x88,14);Camp(game,true);var returned=reader.Read(showInCamp:show);apply.Apply(returned);
                Assert(!returned.IsQuestDamageFrozen && OverlayViewModel.Instance.TeamWidget.Context.Players.All(p=>p.Damage==0),"clear/fail hub return resets regardless of camp visibility");
            }finally{apply.Clear();}
        }
    }
internal static void EndWhileInCamp()
    {
        var game=new FakeGame();ulong quest=Quest(game);var reader=new WildsReader(game.M,game.Addresses,game.Base);reader.Read(showInCamp:true);Camp(game,true);reader.Read(showInCamp:true);Finish(game,quest);
        var final=reader.Read(showInCamp:true);Assert(final.IsQuestDamageFrozen && final.Players.Single().Damage==125,"finishing while in camp is not itself the return transition");
        Loading(game,true);reader.Read(showInCamp:true);Loading(game,false);Assert(reader.Read(showInCamp:true).Players.All(p=>p.Damage==0),"subsequent settlement return clears it");
    }
internal static void Exploration()
    {
        var game=new FakeGame();ulong quest=Quest(game),history=game.M.Alloc(),hit=game.M.Alloc();game.O(game.Local,"PlayerDamageHistory",history);game.M.Ptr(history+0x10,game.M.Array(hit));game.M.Int(history+0x18,1);game.M.Float(hit+0x10,9000);game.M.Int(hit+0x18,116);
        var reader=new WildsReader(game.M,game.Addresses,game.Base);reader.Read(showInCamp:true);Finish(game,quest);reader.Read(showInCamp:true);Loading(game,true);reader.Read(showInCamp:true);Loading(game,false);Camp(game,true);reader.Read(showInCamp:true);
        Camp(game,false);game.M.Ptr(game.F("QuestManager","QuestCurrentInformation"),0);Assert(reader.Read().Players.Single().Damage==0,"old exploration hit history is excluded");
        game.M.Float(hit+0x10,9040);Assert(reader.Read().Players.Single().Damage==40,"new exploration damage begins from the return baseline");
        game.M.Ptr(game.F("QuestManager","QuestCurrentInformation"),quest);game.M.Int(quest+0x108,0);game.M.Float(quest+0xE0,0);game.M.Float(game.F("QuestManager","QuestLocalPlayerDamage"),25);
        Assert(reader.Read().Players.Single().Damage==25,"new quest resumes real quest totals with no historical subtraction");
        var rolling=new FakeGame();ulong rollingQuest=Quest(rolling),rollingHistory=rolling.M.Alloc();rolling.O(rolling.Local,"PlayerDamageHistory",rollingHistory);
        var hits=Enumerable.Range(0,101).Select(i=>rolling.M.Alloc()).ToArray();for(int i=0;i<101;i++){rolling.M.Float(hits[i]+0x10,i==0?9000:i==100?5:10);rolling.M.Int(hits[i]+0x18,116);}
        rolling.M.Ptr(rollingHistory+0x10,rolling.M.Array(hits));rolling.M.Int(rollingHistory+0x18,100);var rollingReader=new WildsReader(rolling.M,rolling.Addresses,rolling.Base);rollingReader.Read(showInCamp:true);Finish(rolling,rollingQuest);rollingReader.Read(showInCamp:true);Loading(rolling,true);rollingReader.Read(showInCamp:true);Loading(rolling,false);Camp(rolling,true);rollingReader.Read(showInCamp:true);
        Camp(rolling,false);rolling.M.Ptr(rolling.F("QuestManager","QuestCurrentInformation"),0);rolling.M.Int(rollingHistory+0x18,101);
        Assert(rollingReader.Read().Players.Single().Damage==5,"a rolling history window cannot bring back old hits when its aggregate decreases");
        rolling.M.Ptr(rollingHistory+0x10,rolling.M.Array(hits.Skip(1).ToArray()));rolling.M.Int(rollingHistory+0x18,100);Assert(rollingReader.Read().Players.Single().Damage==5,"trimming old history retains the same total without replaying records");
        ulong newest=rolling.M.Alloc();rolling.M.Float(newest+0x10,7);rolling.M.Int(newest+0x18,116);rolling.M.Ptr(rollingHistory+0x10,rolling.M.Array(hits.Skip(2).Concat(new[]{newest}).ToArray()));
        Assert(rollingReader.Read().Players.Single().Damage==12 && rollingReader.Read().Players.Single().Damage==12,"fixed-size rolling history counts each new entry exactly once");
    }
}
