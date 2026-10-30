using System;
using System.Collections.Generic;
namespace SmartHunter.Wilds
{
    internal sealed class WildsMonsterReader
    {
        readonly WildsReadContext game;
        WildsMemoryView Memory => game.Memory;
        public WildsMonsterReader(WildsReadContext game) { this.game = game; }
        public List<WildsMonsterSnapshot> CaptureHeaders(ulong player)
        {
            var output = new List<WildsMonsterSnapshot>();
            var memory = Memory;
            ulong list = game.Slot("EnemyManager", "EnvironmentMonsterList");
            int count = list == 0 ? 0 : memory.Int(list + 8);
            if (count < 0 || count > 700)
                return output;
            ulong array = memory.Ptr(list);
            if (array == 0)
                return output;
            ulong selected = game.Root("CameraManager", "CameraMonsterTarget");
            byte[] pointers = game.BulkReads && count > 0 ? memory.Bytes(array + 32, count * 8) : null;
            float playerX = memory.Float(player + 16), playerY = memory.Float(player + 20), playerZ = memory.Float(player + 24);
            var visited = new HashSet<ulong>();
            for (int slot = 0; slot < count; slot++)
            {
                ulong actor = pointers == null ? memory.Ptr(array + 32 + (ulong)slot * 8) : BitConverter.ToUInt64(pointers, slot * 8);
                if (actor == 0 || !visited.Add(actor) || (uint)game.Follow(actor, "MonsterMagic") != 0x6D0045)
                    continue;
                ulong identity = game.Follow(actor, "MonsterBasicData");
                if (identity == 0 || memory.Byte(identity + 16) != 1 || memory.Int(identity + 0x54) != 0)
                    continue;
                ulong health = game.Follow(actor, "MonsterHealth");
                float current = game.Health.Read(memory, memory.Ptr(health + 16)), maximum = game.Health.Read(memory, memory.Ptr(health + 24));
                if (!WildsReadContext.HealthRange(current, maximum) || current <= 0)
                    continue;
                ulong position = game.Follow(actor, "MonsterContextData");
                float dx = memory.Float(position + 0x30) - playerX, dy = memory.Float(position + 0x34) - playerY, dz = memory.Float(position + 0x38) - playerZ;
                float squaredDistance = dx * dx + dy * dy + dz * dz;
                int species = memory.Int(identity + 0x48), role = memory.Int(identity + 0x4C);
                int size = position == 0 ? 100 : memory.Short(position + (memory.Byte(position + 0x3F0) == 0 ? 0x3ECUL : 0x3F2UL));
                if (species == 16 && role == 1)
                    size = 130;
                ulong context = game.Follow(actor, "MonsterContext");
                output.Add(new WildsMonsterSnapshot
                {
                    Address = actor,
                    Context = context,
                    Id = species,
                    Role = role,
                    Legendary = memory.Int(identity + 0x50),
                    Size = size > 0 ? size : 100,
                    Health = current,
                    MaxHealth = maximum,
                    TargetKey = unchecked((int)game.Follow(actor, "MonsterTargetKey")),
                    Selected = selected != 0 && context == selected,
                    Distance = WildsReadContext.Number(squaredDistance) ? squaredDistance : Single.MaxValue
                });
            }
            return output;
        }
        public void CaptureDetails(WildsMonsterSnapshot monster)
        {
            ulong owner = game.Follow(monster.Address, "MonsterParts");
            var parts = Memory.Array(Memory.Ptr(owner + 0x78), 64);
            var breakStates = Memory.Array(Memory.Ptr(owner + 0x20), 64);
            var severable = Memory.Array(Memory.Ptr(owner + 0x18), 64);
            var severStates = Memory.Array(Memory.Ptr(owner + 0x28), 64);
            CaptureParts(monster, parts, breakStates, 0);
            CaptureParts(monster, severable, severStates, parts.Length);
            AddStatus(monster, game.Follow(monster.Address, "MonsterRage"), 0);
            AddStatus(monster, game.Follow(monster.Address, "MonsterStamina"), 1);
            foreach (ulong status in Memory.Array(game.Follow(monster.Address, "MonsterStatusEffects"), 128))
            {
                int id = Memory.Int(status + 0xB4);
                var names = WildsCatalog.Instance.StatusEffects;
                if (id > 1 && id < names.Length && !names[id].NameStringId.StartsWith("LOC_STATUS_EFFECT_UNKNOWN", StringComparison.Ordinal))
                    AddStatus(monster, status, id);
            }
        }
        void CaptureParts(WildsMonsterSnapshot monster, ulong[] parts, ulong[] damageStates, int firstIndex)
        {
            var definition = WildsCatalog.Instance.Monster(monster.Id);
            var memory = Memory;
            for (int slot = 0; slot < parts.Length; slot++)
            {
                ulong part = parts[slot];
                if (part == 0)
                    continue;
                float current = game.Health.Read(memory, memory.Ptr(part + 0x28)), maximum = game.Health.Read(memory, memory.Ptr(part + 16));
                if (!WildsReadContext.HealthRange(current, maximum))
                    continue;
                ulong state = slot < damageStates.Length ? damageStates[slot] : 0;
                int cycles = memory.Int(part + 0x58), broken = state == 0 ? cycles : memory.Int(state + 24);
                bool enabled = state != 0 && memory.Byte(state + 0x32) != 0, canSever = enabled && memory.Byte(state + 0x31) != 0;
                int multiplier = state == 0 ? 1 : memory.Int(state + 20), breakLimit = state == 0 ? 1 : memory.Int(state + 16);
                if (enabled && multiplier >= 1 && multiplier <= 100 && cycles >= 0 && cycles <= 100 && breakLimit >= 1 && breakLimit <= 100)
                {
                    float segment = maximum;
                    maximum = segment * multiplier * (canSever ? breakLimit : 1);
                    current = !canSever && broken >= breakLimit ? maximum : segment * Math.Max(0, multiplier - 1 - cycles) + current;
                }
                int index = firstIndex + slot;
                monster.Parts.Add(new WildsPartSnapshot
                {
                    Address = part,
                    Slot = index,
                    Health = current,
                    MaxHealth = maximum,
                    Count = enabled ? broken : cycles,
                    Severable = canSever,
                    NameId = definition != null && index < definition.Parts.Length ? definition.Parts[index].StringId : "LOC_PART_UNKNOWN"
                });
            }
        }
        void AddStatus(WildsMonsterSnapshot monster, ulong address, int id)
        {
            if (address == 0)
                return;
            var memory = Memory;
            ulong progress = memory.Ptr(address + 32);
            byte[] state = game.BulkReads ? memory.Bytes(address + 0x48, 20) : null;
            byte[] buildup = game.BulkReads && progress != 0 ? memory.Bytes(progress + 20, 12) : null;
            float duration = state == null ? memory.Float(address + 0x4C) : BitConverter.ToSingle(state, 4), elapsed = state == null ? memory.Float(address + 0x48) : BitConverter.ToSingle(state, 0);
            float limit = buildup == null ? memory.Float(progress + 28) : BitConverter.ToSingle(buildup, 8), amount = buildup == null ? memory.Float(progress + 20) : BitConverter.ToSingle(buildup, 0);
            bool active = (state == null ? memory.Int(address + 0x58) : BitConverter.ToInt32(state, 16)) == 1;
            monster.Ailments.Add(new WildsAilmentSnapshot
            {
                Address = address,
                Id = id,
                Active = active,
                Remaining = active && WildsReadContext.Number(duration) && WildsReadContext.Number(elapsed) ? Math.Max(0, duration - elapsed) : 0,
                MaxDuration = WildsReadContext.Number(duration) ? Math.Max(0, duration) : 0,
                Buildup = WildsReadContext.Number(amount) ? Math.Max(0, amount) : 0,
                MaxBuildup = WildsReadContext.Number(limit) ? Math.Max(0, limit) : 0
            });
        }
    }
}
