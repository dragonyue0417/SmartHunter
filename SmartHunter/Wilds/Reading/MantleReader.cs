using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace SmartHunter.Wilds
{
    internal static class WildsMantles
    {
        internal static readonly string[] Names = { "GHILLIE", "ROCKSTEADY", "MENDING", "EVASION", "CORRUPTED" };
        internal static string Key(int slot, bool cooling) => "LOC_EQUIPMENT_" + (cooling ? "RECHARGE_" : "") + "MANTLE_" + Names[slot];
    }

    // Read the live counters; never estimate cooldown from elapsed wall time.
    internal sealed class WildsMantleReader
    {
        const string ArrayType = "app.mcActiveSkillController.cActiveSkill[]";
        const string ElementType = "app.mcActiveSkillController.cActiveSkill";
        const string ManagedArrayType = "via.clr.ManagedObject";
        readonly WildsReadContext game;
        readonly int moduleSize;
        readonly Stopwatch clock = Stopwatch.StartNew();
        int[] discoveredPath;
        readonly List<string> lastObjects = new List<string>();
        readonly HashSet<ulong> verifiedElementTypes = new HashSet<ulong>();
        long nextDiscovery;
        ulong previousLocal, checkedArray;
        string arrayType;
        public WildsMantleReader(WildsReadContext game, int moduleSize) { this.game = game; this.moduleSize = moduleSize; }
        static bool Timer(float value) => WildsReadContext.Number(value) && value >= 0 && value <= 86400;
        static float? Optional(float value) => Timer(value) ? value : (float?)null;
        static bool Pointer(ulong address) => address >= 0x10000 && address <= 0x7FFFFFFFF000 && (address & 7) == 0;
        string ArrayPath => discoveredPath == null ? "PlayerSpecializedTools" : "Local" + String.Concat(discoveredPath.Select(o => "+0x" + o.ToString("X")));
        bool Array(ulong address, out ulong[] entries)
        {
            entries = null;
            byte[] header = game.Memory.Bytes(address, 32);
            if (header == null) return false;
            int count = BitConverter.ToInt32(header, 28);
            if (count < 1 || count > 8) return false;
            byte[] slots = game.Memory.Bytes(address + 32, count * 8);
            if (slots == null) return false;
            if (checkedArray != address)
            {
                checkedArray = address;
                arrayType = new WildsNetworkMetadata(game.Memory, () => false).Inspect(address)?.Name;
            }
            if (arrayType != null && arrayType != ArrayType && arrayType != ManagedArrayType) return false;
            entries = Enumerable.Range(0, count).Select(i => BitConverter.ToUInt64(slots, i * 8)).ToArray();
            // Real 1.42.0.3 arrays report only ManagedObject in reflection.
            // Validate every non-null element before accepting that representation.
            if (arrayType == ManagedArrayType && entries.Any(p => p != 0 && !MantleElement(p)))
            { entries = null; return false; }
            if (entries.Any(Pointer)) return true;
            entries = null;
            return false;
        }
        bool MantleElement(ulong address)
        {
            if (!Pointer(address)) return false;
            ulong nativeType = game.Memory.Ptr(address);
            if (verifiedElementTypes.Contains(nativeType)) return true;
            if (new WildsNetworkMetadata(game.Memory, () => false).Inspect(address)?.Name != ElementType) return false;
            if (verifiedElementTypes.Count < 16) verifiedElementTypes.Add(nativeType);
            return true;
        }
        public WildsMantleSample Capture(List<WildsEffectSnapshot> effects, ulong local, bool diagnostics)
        {
            if (local != previousLocal) { previousLocal = local; discoveredPath = null; nextDiscovery = 0; checkedArray = 0; arrayType = null; lastObjects.Clear(); }
            var sample = diagnostics ? new WildsMantleSample { Probe = new WildsMantleProbe(game, moduleSize).Capture(local) } : null;
            ulong array = discoveredPath == null ? game.Follow(local, "PlayerSpecializedTools") : game.Memory.Object(local, discoveredPath);
            if (!Array(array, out var entries) && clock.ElapsedMilliseconds >= nextDiscovery)
            {
                nextDiscovery = clock.ElapsedMilliseconds + 5000;
                array = Discover(local);
                Array(array, out entries);
            }
            if (sample != null) { sample.ArrayPath = ArrayPath; sample.ArrayNativeType = arrayType; sample.Objects.AddRange(lastObjects); }
            if (entries == null) return sample;
            int? reserve = ReserveTool();
            if (sample != null) { sample.Status = "ArrayRead"; sample.SelectedMantle = reserve; sample.ArrayLength = entries.Length; }
            var metadata = diagnostics ? new WildsNetworkMetadata(game.Memory, () => false) : null;
            var types = diagnostics ? new HashSet<string>() : null;
            for (int slot = 0; slot < Math.Min(entries.Length, WildsMantles.Names.Length); slot++)
            {
                ulong tool = entries[slot];
                if (!Pointer(tool)) continue;
                var row = sample == null ? null : new WildsMantleReading { Slot = slot, Status = "Unreadable" };
                sample?.Mantles.Add(row);
                byte[] fields = game.Memory.Bytes(tool, 0x38);
                if (fields == null) continue;
                byte use = fields[0x33];
                bool worn = use == 1;
                // The live counter is shared by active duration and cooldown.
                // +0x18 retains the initial cooldown even after it is ready.
                float remaining = Decode(fields, 0x10),
                    maxCooldown = !worn || diagnostics ? Decode(fields, 0x20) : Single.NaN,
                    maxActive = worn || diagnostics ? Decode(fields, 0x28) : Single.NaN;
                if (row != null)
                {
                    var type = metadata.Inspect(tool);
                    row.NativeType = type?.Name;
                    if (type != null && types.Add(type.Name)) sample.Types.Add(metadata.ReadProperties(type, game.ModuleBase, moduleSize, true));
                    row.Flags = BitConverter.ToString(fields, 0x30, 8).Replace('-', ' ');
                    row.InUse = use <= 1 ? worn : (bool?)null;
                    row.ActiveRemaining = worn ? Optional(remaining) : null;
                    row.CooldownRemaining = worn ? null : Optional(remaining);
                    row.ActiveMaximum = Optional(maxActive); row.CooldownMaximum = Optional(maxCooldown);
                }
                float maximum = worn ? maxActive : maxCooldown;
                string status = use > 1 ? "InvalidUseFlag" : !Timer(remaining) || !Timer(maximum) || maximum <= 0 || remaining > maximum * 1.01f ? "InvalidTimer" :
                    remaining <= 0 ? "Ready" : !worn && reserve.HasValue && slot != 0 && slot != reserve.Value - 1 ? "NotEquipped" : worn ? "Active" : "Cooldown";
                if (row != null) { row.Status = status; row.NameStringId = WildsMantles.Key(slot, !worn); }
                if (status != "Active" && status != "Cooldown") continue;
                effects?.Add(new WildsEffectSnapshot { Index = WildsCatalog.Instance.PlayerStatusEffects.Length + slot * 2 + (worn ? 0 : 1),
                    NameStringId = WildsMantles.Key(slot, !worn), Remaining = remaining, Max = maximum });
            }
            return sample;
        }
        float Decode(byte[] fields, int offset) => game.Health.Read(game.Memory, BitConverter.ToUInt64(fields, offset));

        // A changed component offset can be recovered by its native type. This
        // bounded, cold-path search is cached as offsets, never as object pointers.
        ulong Discover(ulong local)
        {
            lastObjects.Clear();
            var elapsed = Stopwatch.StartNew();
            var budget = new ProbeMemory(game.Memory);
            bool Exhausted() => budget.Reads >= 512 || elapsed.ElapsedMilliseconds >= 30;
            var view = new WildsMemoryView(budget);
            var metadata = new WildsNetworkMetadata(view, Exhausted);
            var queue = new Queue<Tuple<ulong, int[]>>();
            ulong character = view.Ptr(local + 0x18);
            ulong component = view.Ptr(character + 0x3A8);
            queue.Enqueue(Tuple.Create(view.Ptr(component + 0xB0), new[] { 0x18, 0x3A8, 0xB0 }));
            queue.Enqueue(Tuple.Create(component, new[] { 0x18, 0x3A8 }));
            queue.Enqueue(Tuple.Create(character, new[] { 0x18 }));
            var seen = new HashSet<ulong>();
            while (queue.Count > 0 && !Exhausted() && seen.Count < 128)
            {
                var node = queue.Dequeue();
                if (!seen.Add(node.Item1)) continue;
                var type = metadata.Inspect(node.Item1);
                if (type == null) continue;
                string path = "Local" + String.Concat(node.Item2.Select(o => "+0x" + o.ToString("X")));
                if (lastObjects.Count < 24) lastObjects.Add(path + ": " + type.Name);
                if ((type.Name == ArrayType || type.Name == ManagedArrayType) && Array(node.Item1, out _))
                { discoveredPath = node.Item2; return node.Item1; }
                if (node.Item2.Length >= 4 || !type.Name.StartsWith("app.", StringComparison.Ordinal) || type.Size < 24) continue;
                byte[] body = view.Bytes(node.Item1, Math.Min(type.Size, 0x1000));
                if (body == null) continue;
                var offsets = Enumerable.Range(2, body.Length / 8 - 2).Select(i => i * 8).OrderBy(o => Math.Abs(o - 0x3A8));
                foreach (int offset in offsets)
                {
                    if (queue.Count >= 256) break;
                    ulong pointer = BitConverter.ToUInt64(body, offset);
                    if (!Pointer(pointer) || seen.Contains(pointer)) continue;
                    queue.Enqueue(Tuple.Create(pointer, node.Item2.Concat(new[] { offset }).ToArray()));
                }
            }
            return 0;
        }
        sealed class ProbeMemory : IWildsMemory
        {
            readonly WildsMemoryView memory;
            public int Reads;
            public ProbeMemory(WildsMemoryView memory) { this.memory = memory; }
            public bool Read(ulong address, byte[] buffer, int size) => ++Reads <= 512 && memory.ReadInto(address, buffer, size);
        }
        int? ReserveTool()
        {
            var memory = game.Memory;
            int value = 0;
            if (memory.Int(game.Slot("PlayerManager", "PlayerIsInArena")) < 1)
            {
                int selected = memory.Int(game.Slot("SaveManager", "SaveIndex"));
                if (selected < 0 || selected > 7) return null;
                ulong saves = game.Slot("SaveManager", "SaveData"), save = saves == 0 ? 0 : memory.Ptr(saves + (ulong)selected * 8);
                ulong equipment = game.Follow(save, "SaveSpecializedTool");
                if (equipment != 0) value = memory.Int(equipment + 64);
            }
            else
            {
                int mission = memory.Int(game.Slot("QuestManager", "QuestMissionId")), loadout = memory.Int(game.Slot("QuestManager", "QuestArenaSelectedLoadout"));
                if (loadout < 0 || loadout >= 32) return null;
                foreach (ulong entry in memory.Array(game.Root("QuestManager", "QuestArenaData"), 64))
                {
                    ulong details = memory.Ptr(entry + 32);
                    if (details == 0 || memory.Int(details + 16) != mission) continue;
                    ulong list = memory.Ptr(entry + 64);
                    if (list == 0 || loadout >= memory.Int(list + 28)) return null;
                    ulong selected = memory.Ptr(list + 32 + (ulong)loadout * 8), equipment = selected == 0 ? 0 : memory.Ptr(selected + 16);
                    if (equipment != 0) value = memory.Int(equipment + 80);
                    break;
                }
            }
            return value >= 1 && value <= WildsMantles.Names.Length ? value : (int?)null;
        }
    }
}
