using System;
using System.Collections.Generic;
using System.Linq;
using SmartHunter.Game.Config;
using SmartHunter.Game.Data.ViewModels;
using SmartHunter.Game.Helpers;

namespace SmartHunter.Wilds
{
    public sealed class WildsSnapshotApplier
    {
        int stage = -1;
        ulong quest;
        float elapsed;
        int damageResetVersion = -1;
        readonly Dictionary<int, ulong> playerIds = new Dictionary<int, ulong>();
        public void Clear()
        {
            var vm = OverlayViewModel.Instance;
            vm.MonsterWidget.Context.Monsters.Clear(); vm.TeamWidget.Context.ClearPlayers(); vm.PlayerWidget.Context.StatusEffects.Clear();
            vm.IsGameActive = false; stage = -1; quest = 0; elapsed = 0;
            playerIds.Clear(); damageResetVersion = -1;
        }
        public void Apply(WildsSnapshot snapshot)
        {
            var vm = OverlayViewModel.Instance;
            var game = vm.DebugWidget.Context.CurrentGame;
            game.CurrentPlayerName = snapshot.PlayerName;
            vm.DebugWidget.Context.StageId = snapshot.StageId;
            game.CurrentWeaponString = snapshot.Weapon >= 0 && snapshot.Weapon <= 13 ? "wp" + snapshot.Weapon.ToString("D2") : "";
            game.IsValid = snapshot.IsReady;
            if (!snapshot.CanDisplayData)
            {
                if (snapshot.IsReady && quest != 0 && damageResetVersion == snapshot.DamageResetVersion)
                {
                    vm.MonsterWidget.Context.Monsters.Clear(); vm.PlayerWidget.Context.StatusEffects.Clear(); vm.IsGameActive = false;
                }
                else Clear();
                return;
            }
            if (quest == 0 && stage != snapshot.StageId || quest != snapshot.QuestIdentity || snapshot.QuestElapsed < elapsed - 5000 || damageResetVersion != snapshot.DamageResetVersion)
            {
                vm.TeamWidget.Context.ClearPlayers();
                playerIds.Clear();
            }
            damageResetVersion = snapshot.DamageResetVersion;
            stage = snapshot.StageId; quest = snapshot.QuestIdentity; elapsed = snapshot.QuestElapsed;
            game.IsPlayerInExpedition = quest == 0;
            var monsters = snapshot.Monsters.GroupBy(m => m.Address).Select(g => g.First()).ToList();
            var addresses = new HashSet<ulong>(monsters.Select(m => m.Address));
            foreach (var existing in vm.MonsterWidget.Context.Monsters.ToArray())
                if (!addresses.Contains(existing.Address)) vm.MonsterWidget.Context.Monsters.Remove(existing);
            for (int position = 0; position < monsters.Count; position++)
            {
                var data = monsters[position];
                string key = WildsMonsterTypes.Id(data.Id);
                if (!ConfigHelper.MonsterData.Values.Monsters.ContainsKey(key))
                    ConfigHelper.MonsterData.Values.Monsters[key] = new MonsterConfig("LOC_MONSTER_UKNOWN", new MonsterPartConfig[0], new MonsterSoftenPartsConfig[0], 0, null);
                var monster = vm.MonsterWidget.Context.UpdateAndGetMonster(data.Address, key, data.MaxHealth, data.Health, data.Size / 100f, 1);
                monster.IsSelected = data.Selected;
                int currentPosition = vm.MonsterWidget.Context.Monsters.IndexOf(monster);
                if (currentPosition != position) vm.MonsterWidget.Context.Monsters.Move(currentPosition, position);
                monster.VariantKeys = String.Join("|", new[] { data.Legendary == 2 ? "LOC_MONSTER_VARIANT_ARCH_TEMPERED" : data.Legendary == 1 ? "LOC_MONSTER_VARIANT_TEMPERED" : "", data.Role == 3 ? "LOC_MONSTER_VARIANT_FRENZIED" : "" }.Where(s => s.Length > 0));
                var partAddresses = new HashSet<ulong>(data.Parts.Select(p => p.Address));
                foreach (var old in monster.Parts.ToArray()) if (!partAddresses.Contains(old.Address)) monster.Parts.Remove(old);
                foreach (var part in data.Parts.GroupBy(p => p.Address).Select(g => g.First()))
                {
                    var item = monster.UpdateAndGetPart(part.Address, part.Severable, part.MaxHealth, part.Health, part.Count);
                    if (item.NameId != part.NameId) { item.NameId = part.NameId; item.NotifyPropertyChanged(nameof(item.Name)); }
                }
                var ailments = data.Ailments.GroupBy(a => a.Id).Select(g => g.First()).ToArray();
                var ailmentIds = new HashSet<int>(ailments.Select(a => a.Id));
                foreach (var old in monster.StatusEffects.ToArray()) if (!ailmentIds.Contains(old.Index)) monster.StatusEffects.Remove(old);
                var previousAilments = monster.StatusEffects.GroupBy(a => a.Index).ToDictionary(g => g.Key, g => g.First());
                foreach (var ailment in ailments)
                {
                    previousAilments.TryGetValue(ailment.Id, out var previous);
                    int count = (previous?.TimesActivatedCount ?? 0) + (ailment.Active && ailment.Remaining > 0 && (previous == null || previous.Duration.Current <= 0) ? 1 : 0);
                    monster.UpdateAndGetStatusEffect(ailment.Address, ailment.Id, ailment.MaxBuildup, ailment.Buildup, ailment.MaxDuration, ailment.Remaining, count);
                }
            }
            for (int i = 0; i < 4; i++)
            {
                var player = snapshot.Players.FirstOrDefault(p => p.Slot == i);
                if (player != null && playerIds.TryGetValue(i, out var id) && id != player.Address) vm.TeamWidget.Context.UpdateAndGetPlayer(i, "", 0);
                if (player != null) playerIds[i] = player.Address; else playerIds.Remove(i);
                string name = player == null ? "" : player.Name;
                if (player?.IsNpc == true) name = ConfigHelper.Localization.Values.Strings.TryGetValue(player.Name, out var translated) ? translated : LocalizationHelper.GetString("LOC_UI_SUPPORT_HUNTER");
                vm.TeamWidget.Context.UpdateAndGetPlayer(i, name, player?.Damage ?? 0);
            }
            vm.TeamWidget.Context.UpdateFractions();
            var definitions = ConfigHelper.PlayerData.Values.StatusEffects;
            var visibleEffects = snapshot.Effects.Select(e => new { Data = e, Index = e.ConfigIndex(definitions) }).Where(e => e.Index >= 0).ToArray();
            foreach (var old in vm.PlayerWidget.Context.StatusEffects.ToArray()) if (!visibleEffects.Any(e => e.Index == old.Index)) vm.PlayerWidget.Context.StatusEffects.Remove(old);
            foreach (var item in visibleEffects)
            {
                var data = item.Data;
                var effect = vm.PlayerWidget.Context.UpdateAndGetPlayerStatusEffect(item.Index, data.Remaining, true);
                effect.Time.Max = data.Infer ? Math.Max(effect.Time.Max, data.Remaining) : data.Max;
            }
        }
        public static WildsSnapshot Preview(int monsterCount = 6)
        {
            var result = new WildsSnapshot { IsReady = true, InHuntingZone = true, PlayerName = "Preview Hunter", StageId = 1, Weapon = 3 };
            var definitions = WildsCatalog.Instance.Monsters.Where(m => WildsMonsterTypes.Index(m.Key) >= 15).ToArray();
            for (int n = 0; n < monsterCount; n++)
            {
                var definition = definitions[n % definitions.Length];
                var monster = new WildsMonsterSnapshot { Address = 0x100000 + (ulong)n * 0x10000, Id = WildsMonsterTypes.Index(definition.Key), Size = 112, Health = 18450, MaxHealth = 32000, Selected = n == 0 };
                for (int i = 0; i < Math.Min(3, definition.Value.Parts.Length); i++) monster.Parts.Add(new WildsPartSnapshot { Address = monster.Address + 0x1000 + (ulong)i, Slot = i, NameId = definition.Value.Parts[i].StringId, MaxHealth = 1800, Health = 650 });
                monster.Ailments.Add(new WildsAilmentSnapshot { Address = monster.Address + 0x2000, Id = 3, Buildup = 180, MaxBuildup = 300 });
                result.Monsters.Add(monster);
            }
            result.Players.Add(new WildsPlayerSnapshot { Slot = 0, Name = "Preview Hunter", Damage = 12500 });
            result.Players.Add(new WildsPlayerSnapshot { Slot = 1, Name = "Hunter 02", Damage = 9800 });
            result.Players.Add(new WildsPlayerSnapshot { Slot = 2, Name = "Hunter 03", Damage = 7600 });
            result.Effects.Add(new WildsEffectSnapshot { Index = 0, Remaining = 40, Max = 120 });
            return result;
        }
    }
}
