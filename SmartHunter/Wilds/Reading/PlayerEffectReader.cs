using System;
using System.Linq;
using SmartHunter.Game.Config;
namespace SmartHunter.Wilds
{
    internal sealed class WildsPlayerEffectReader
    {
        readonly WildsReadContext game;
        sealed class Requirement
        {
            public int Offset; public int? Integer; public byte? Byte;
        }
        sealed class Timer
        {
            public int Output, Offset, Slot, Limit;
            public WildsEffectSource Source;
            public bool Duration, Buildup, Supported;
            public Requirement[] Requirements;
        }
        readonly Timer[] timers;
        public WildsPlayerEffectReader(WildsReadContext game)
        {
            this.game = game;
            timers = WildsCatalog.Instance.PlayerStatusEffects.Select((definition, index) => new Timer
            {
                Output = index,
                Offset = WildsStatusEffectConfig.Hex(definition.TimerOffset),
                Slot = definition.Index,
                Limit = definition.MaxBuildup,
                Source = (WildsEffectSource)definition.Source,
                Duration = definition.HasMaxDuration,
                Buildup = definition.IsBuildup,
                Supported = (definition.Conditions ?? new MemoryConditionConfig[0]).All(c => c.Offsets?.Length == 1),
                Requirements = (definition.Conditions ?? new MemoryConditionConfig[0]).Where(c => c.Offsets?.Length == 1).Select(c => new Requirement
                {
                    Offset = WildsStatusEffectConfig.Hex(c.Offsets[0]),
                    Integer = c.IntValue,
                    Byte = c.ByteValue
                }).ToArray()
            }).ToArray();
        }
        bool Matches(Timer timer, ulong owner)
        {
            if (owner == 0 || !timer.Supported)
                return false;
            foreach (var condition in timer.Requirements)
            {
                ulong field = checked(owner + (ulong)condition.Offset);
                if (condition.Integer.HasValue && game.Memory.Int(field) != condition.Integer.Value)
                    return false;
                if (condition.Byte.HasValue && game.Memory.Byte(field) != condition.Byte.Value)
                    return false;
            }
            return true;
        }
        float Element(ulong array, int index)
        {
            int length = array == 0 ? 0 : game.Memory.Int(array + 28);
            return index >= 0 && index < length && length <= 256 ? game.Memory.Float(array + 32 + (ulong)index * 4) : Single.NaN;
        }
        public void Capture(WildsSnapshot snapshot, ulong local)
        {
            var memory = game.Memory;
            ulong items = game.Follow(local, "PlayerConsumableEffects"), songs = game.Follow(local, "PlayerHornEffects"),
                palico = game.Follow(local, "PlayerPalicoEffects"), skills = game.Follow(local, "PlayerSkillEffects"), ailments = game.Follow(local, "PlayerDebuffs");
            foreach (var timer in timers)
            {
                float remaining = 0, duration = 0;
                bool inferred = !timer.Duration;
                switch (timer.Source)
                {
                    case WildsEffectSource.Consumable:
                        if (items == 0)
                            continue;
                        duration = timer.Duration ? memory.Float(items + (ulong)timer.Offset) : 0;
                        remaining = memory.Float(items + (ulong)timer.Offset + (timer.Duration ? 4UL : 0));
                        break;
                    case WildsEffectSource.HuntingHorn:
                        if (songs == 0)
                            continue;
                        remaining = Element(memory.Ptr(songs + 16), timer.Slot);
                        duration = Element(memory.Ptr(songs + 24), timer.Slot);
                        break;
                    case WildsEffectSource.Palico:
                        if (palico == 0)
                            continue;
                        remaining = Element(palico, timer.Slot);
                        inferred = true;
                        break;
                    case WildsEffectSource.Skill:
                        if (skills == 0)
                            continue;
                        ulong skill = memory.Ptr(skills + (ulong)timer.Offset);
                        if (!Matches(timer, skill))
                            continue;
                        remaining = memory.Float(skill + 16);
                        duration = memory.Float(skill + 20);
                        break;
                    case WildsEffectSource.Debuff:
                        if (ailments == 0 || timer.Slot < 0 || timer.Slot > 64)
                            continue;
                        ulong state = memory.Ptr(ailments + 16 + (ulong)timer.Slot * 8);
                        if (!Matches(timer, state))
                            continue;
                        if (timer.Buildup)
                        {
                            remaining = memory.Float(state + 24);
                            duration = timer.Limit;
                            inferred = false;
                            if (remaining > duration)
                                continue;
                        }
                        else if (memory.Byte(state + 0x2F) == 1)
                        {
                            remaining = memory.Float(state + (ulong)timer.Offset);
                            duration = timer.Duration ? memory.Float(state + (ulong)timer.Offset + 4) : 0;
                        }
                        break;
                    default:
                        continue;
                }
                if (WildsReadContext.Number(remaining) && remaining > 0 && remaining < 864000)
                    snapshot.Effects.Add(new WildsEffectSnapshot
                    {
                        Index = timer.Output,
                        Remaining = remaining,
                        Max = WildsReadContext.Number(duration) && duration > 0 ? duration : remaining,
                        Infer = inferred
                    });
            }
            CaptureTools(snapshot, local);
        }
        void CaptureTools(WildsSnapshot snapshot, ulong local)
        {
            var memory = game.Memory;
            ulong[] equipment = memory.Array(game.Follow(local, "PlayerSpecializedTools"), 8);
            int reserve = ReserveTool();
            for (int slot = 0; slot < equipment.Length && slot < 5; slot++)
            {
                if (slot != 0 && slot != reserve - 1)
                    continue;
                ulong tool = equipment[slot];
                if (tool == 0)
                    continue;
                bool worn = memory.Byte(tool + 0x33) != 0;
                float remaining = game.Health.Read(memory, memory.Ptr(tool + (worn ? 16UL : 24UL))), duration = game.Health.Read(memory, memory.Ptr(tool + (worn ? 40UL : 32UL)));
                if (WildsReadContext.HealthRange(remaining, duration) && remaining > 0)
                    snapshot.Effects.Add(new WildsEffectSnapshot { Index = timers.Length + slot * 2 + (worn ? 0 : 1), Remaining = remaining, Max = duration });
            }
        }
        int ReserveTool()
        {
            var memory = game.Memory;
            if (memory.Int(game.Slot("PlayerManager", "PlayerIsInArena")) < 1)
            {
                int selected = memory.Int(game.Slot("SaveManager", "SaveIndex"));
                if (selected < 0 || selected > 7)
                    return 0;
                ulong saves = game.Slot("SaveManager", "SaveData"), save = saves == 0 ? 0 : memory.Ptr(saves + (ulong)selected * 8);
                ulong equipment = game.Follow(save, "SaveSpecializedTool");
                return equipment == 0 ? 0 : memory.Int(equipment + 64);
            }
            int mission = memory.Int(game.Slot("QuestManager", "QuestMissionId")), loadout = memory.Int(game.Slot("QuestManager", "QuestArenaSelectedLoadout"));
            if (loadout < 0 || loadout >= 32)
                return 0;
            foreach (ulong entry in memory.Array(game.Root("QuestManager", "QuestArenaData"), 64))
            {
                ulong details = memory.Ptr(entry + 32);
                if (details == 0 || memory.Int(details + 16) != mission)
                    continue;
                ulong list = memory.Ptr(entry + 64);
                if (list == 0 || loadout >= memory.Int(list + 28))
                    return 0;
                ulong selected = memory.Ptr(list + 32 + (ulong)loadout * 8), equipment = selected == 0 ? 0 : memory.Ptr(selected + 16);
                return equipment == 0 ? 0 : memory.Int(equipment + 80);
            }
            return 0;
        }
    }
}
