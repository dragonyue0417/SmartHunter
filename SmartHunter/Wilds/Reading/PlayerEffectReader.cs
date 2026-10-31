using System;
using System.Linq;
using SmartHunter.Game.Config;
namespace SmartHunter.Wilds
{
    internal sealed class WildsPlayerEffectReader
    {
        readonly WildsReadContext game;
        readonly WildsMantleReader mantles;
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
        public WildsPlayerEffectReader(WildsReadContext game, int moduleSize = 0)
        {
            this.game = game;
            mantles = new WildsMantleReader(game, moduleSize);
            timers = WildsCatalog.Instance.PlayerStatusEffects.Select((definition, index) => new Timer
            {
                Output = index,
                Offset = WildsStatusEffectConfig.Hex(definition.TimerOffset),
                Slot = definition.Index,
                Limit = definition.MaxBuildup,
                Source = (WildsEffectSource)definition.Source,
                Duration = definition.HasMaxDuration,
                Buildup = definition.IsBuildup,
                Supported = (definition.Conditions ?? Array.Empty<MemoryConditionConfig>()).All(c => c.Offsets?.Length == 1),
                Requirements = (definition.Conditions ?? Array.Empty<MemoryConditionConfig>()).Where(c => c.Offsets?.Length == 1).Select(c => new Requirement
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
        internal void CaptureMantles(WildsSnapshot snapshot, ulong local) => snapshot.Mantles = mantles.Capture(null, local, true);
        public void Capture(WildsSnapshot snapshot, ulong local, bool collectMantleDiagnostics = false)
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
            snapshot.Mantles = mantles.Capture(snapshot.Effects, local, collectMantleDiagnostics);
        }
    }
}
