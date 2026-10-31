using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace SmartHunter.Wilds
{
    public sealed class WildsMantleProbeReport
    {
        public string Purpose = "DiagnosticOnly: AOB-rooted native object and timer candidates; never used as unverified overlay values";
        public string Source = "AOB:PlayerManager", Status = "Completed", PlayerManagerRva;
        public int ReadAttempts, ObjectCount, PendingObjects;
        public long BytesRead, ElapsedMilliseconds;
        public int ReadLimit = 4096, ObjectLimit = 160, DepthLimit = 7;
        public List<WildsMantlePathCheck> Paths = new List<WildsMantlePathCheck>();
        public List<WildsMantleObjectCheck> Objects = new List<WildsMantleObjectCheck>();
        public List<WildsNetworkTypeMetadata> Types = new List<WildsNetworkTypeMetadata>();
        public List<WildsMantleParameterCheck> Parameters = new List<WildsMantleParameterCheck>();
    }
    public sealed class WildsMantlePathCheck
    {
        public string Path, Status, ReadAddress, Pointer, NativeType, Header;
    }
    public sealed class WildsMantleObjectCheck
    {
        public string Path, Address, Type, Bytes;
        public int Size, Depth;
        public int? ArrayLength;
        public List<WildsMantleValueCheck> Values = new List<WildsMantleValueCheck>();
    }
    public sealed class WildsMantleValueCheck
    {
        public string Offset, Storage;
        public float Value;
    }
    public sealed class WildsMantleParameterCheck
    {
        public int Slot;
        public string Purpose = "ConfigurationOnly: not a live countdown", Type;
        public float? Duration, Cooldown;
    }

    // Diagnostic-only exploration rooted in the current AOB result. Neither
    // guessed fields nor the CT parameter table can become overlay countdowns.
    internal sealed class WildsMantleProbe
    {
        const int MaximumBytes = 512 * 1024, MaximumMilliseconds = 150;
        readonly WildsReadContext game;
        readonly int moduleSize;
        sealed class LimitedMemory : IWildsMemory
        {
            readonly WildsMemoryView source;
            readonly Stopwatch clock;
            public int Attempts;
            public long BytesRead;
            public bool Exhausted => Attempts >= 4096 || BytesRead >= MaximumBytes || clock.ElapsedMilliseconds >= MaximumMilliseconds;
            public LimitedMemory(WildsMemoryView source, Stopwatch clock) { this.source = source; this.clock = clock; }
            public bool Read(ulong address, byte[] buffer, int size)
            {
                if (Exhausted || size < 0 || size > 4096 || BytesRead + size > MaximumBytes) return false;
                Attempts++;
                if (!Pointer(address) || !source.ReadInto(address, buffer, size)) return false;
                BytesRead += size;
                return true;
            }
        }
        sealed class Pending
        {
            public ulong Address;
            public string Path;
            public int Depth;
        }
        static bool Pointer(ulong address) => address >= 0x10000 && address <= 0x7FFFFFFFF000;
        static string Hex(ulong address) => "0x" + address.ToString("X");
        static string Step(int offset) => "+0x" + offset.ToString("X");
        static bool Related(string name) => name != null &&
            (name.IndexOf("ActiveSkill", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("ASkill", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Mantle", StringComparison.OrdinalIgnoreCase) >= 0);
        static bool Follow(string name) => name != null && !name.Contains("Delegate") && !name.Contains("System.String") &&
            !name.Contains("Network") && !name.Contains("Net_") && !name.Contains("Resource") && !name.Contains("GUI") &&
            (name.StartsWith("app.", StringComparison.Ordinal) || name.StartsWith("via.", StringComparison.Ordinal) || name.StartsWith("System.Collections.Generic.", StringComparison.Ordinal));
        static int Priority(string name) => Related(name) ? 0 : name != null &&
            (name.Contains("Hunter") || name.Contains("Character") || name.Contains("Player") || name.Contains("Component") || name.Contains("GameObject")) ? 1 : 2;
        static float? Time(float value) => WildsReadContext.Number(value) && value >= 0 && value <= 86400 ? value : (float?)null;

        public WildsMantleProbe(WildsReadContext game, int moduleSize) { this.game = game; this.moduleSize = moduleSize; }
        public WildsMantleProbeReport Capture(ulong local)
        {
            var report = new WildsMantleProbeReport { PlayerManagerRva = Hex(game.Global("PlayerManager") - game.ModuleBase) };
            var clock = Stopwatch.StartNew();
            var budget = new LimitedMemory(game.Memory, clock);
            var memory = new WildsMemoryView(budget);
            var metadata = new WildsNetworkMetadata(memory, () => budget.Exhausted);
            var queue = new List<Pending>();
            var seen = new HashSet<ulong>();
            var described = new HashSet<string>();
            void Enqueue(ulong address, string path, int depth)
            {
                if (Pointer(address) && (address & 7) == 0 && !seen.Contains(address) && queue.Count < 1024)
                    queue.Add(new Pending { Address = address, Path = path, Depth = depth });
            }
            ulong Trace(ulong start, string path, int[] offsets, bool slotRoot)
            {
                ulong address = start;
                foreach (int offset in offsets)
                {
                    ulong cell = slotRoot ? address : address + (ulong)offset;
                    byte[] bytes = memory.Bytes(cell, 8);
                    ulong pointer = bytes == null ? 0 : BitConverter.ToUInt64(bytes, 0);
                    path += Step(offset);
                    var type = Pointer(pointer) ? metadata.Inspect(pointer, out _) : null;
                    report.Paths.Add(new WildsMantlePathCheck { Path = path, ReadAddress = Hex(cell), Pointer = Hex(pointer),
                        Status = bytes == null ? "UnreadablePointer" : pointer == 0 ? "NullPointer" : type == null ? "TypeUnverified" : "TypedPointer",
                        NativeType = type?.Name, Header = type == null && Pointer(pointer) ? Bytes(memory.Bytes(pointer, 32)) : null });
                    if (!Pointer(pointer)) return 0;
                    address = slotRoot ? pointer + (ulong)offset : pointer;
                }
                return address;
            }
            ulong manager = memory.Ptr(game.Global("PlayerManager"));
            Enqueue(local, "Local", 0);
            Trace(game.Global("PlayerManager"), "PlayerLocalSlot", game.Offsets("PlayerLocal"), true);
            Trace(local, "Local", game.Offsets("PlayerSpecializedTools"), false);
            foreach (int offset in new[] { 0x18, 0x30 })
                Enqueue(Trace(local, "Local", new[] { offset }, false), "Local" + Step(offset), 1);
            // The supplied 1.2.7 table has a second player route. It is evidence
            // to inspect by type, not an alternative unverified production root.
            ulong playerSlot = Trace(game.Global("PlayerManager"), "PlayerManagerSlot", new[] { 0x58, 0x10, 0x20, 0x10 }, true);
            Enqueue(memory.Ptr(playerSlot), "PlayerManagerSlot+0x58+0x10+0x20+0x10->", 1);
            Enqueue(manager, "PlayerManager", 0);
            ulong parameterSlot = Trace(game.Global("PlayerManager"), "Parameters", new[] { 0x70, 0xA8, 0x288, 0x50, 0x10 }, true);
            ulong parameters = memory.Ptr(parameterSlot);
            for (int slot = 0; slot < WildsMantles.Names.Length && !budget.Exhausted; slot++)
            {
                ulong value = memory.Ptr(parameters == 0 ? 0 : parameters + 0x20 + (ulong)slot * 8);
                if (!Pointer(value)) continue;
                report.Parameters.Add(new WildsMantleParameterCheck { Slot = slot, Type = metadata.Inspect(value)?.Name,
                    Cooldown = Time(memory.Float(value + 0x14)), Duration = Time(memory.Float(value + 0x18)) });
            }
            bool depthLimited = false;
            while (queue.Count > 0 && !budget.Exhausted && report.Objects.Count < report.ObjectLimit)
            {
                var pending = queue[0]; queue.RemoveAt(0);
                if (!seen.Add(pending.Address)) continue;
                var type = metadata.Inspect(pending.Address, out string reason);
                if (type == null)
                {
                    if (pending.Depth <= 1 && report.Paths.Count < 64) report.Paths.Add(new WildsMantlePathCheck {
                        Path = pending.Path, Pointer = Hex(pending.Address), Status = reason, Header = Bytes(memory.Bytes(pending.Address, 32)) });
                    continue;
                }
                var node = new WildsMantleObjectCheck { Address = Hex(pending.Address), Path = pending.Path, Type = type.Name, Size = type.Size, Depth = pending.Depth };
                report.Objects.Add(node);
                bool related = Related(type.Name);
                if ((related || pending.Path == "Local" || type.Name.Contains("HunterCharacter")) && described.Count < 8 && described.Add(type.Name))
                    report.Types.Add(metadata.ReadProperties(type, game.ModuleBase, moduleSize, true));
                byte[] body = memory.Bytes(pending.Address, Math.Min(type.Size, 4096));
                if (body == null) continue;
                if (related && report.Objects.Count(n => n.Bytes != null) < 16)
                {
                    node.Bytes = Bytes(body.Take(256).ToArray());
                    for (int offset = 16; offset + 4 <= Math.Min(body.Length, 128); offset += 4)
                    {
                        float value = BitConverter.ToSingle(body, offset);
                        if (Time(value).HasValue && value >= 0.001f) node.Values.Add(new WildsMantleValueCheck { Offset = Step(offset), Storage = "InlineFloatCandidate", Value = value });
                    }
                    for (int offset = 16; offset + 8 <= Math.Min(body.Length, 128) && !budget.Exhausted; offset += 8)
                    {
                        ulong pointer = BitConverter.ToUInt64(body, offset);
                        if (!Pointer(pointer) || (pointer & 7) != 0) continue;
                        float value = game.Health.Read(memory, pointer);
                        if (Time(value).HasValue) node.Values.Add(new WildsMantleValueCheck { Offset = Step(offset), Storage = "EncryptedFloatCandidate", Value = value });
                    }
                }
                if (pending.Depth >= report.DepthLimit) { depthLimited = true; continue; }
                if (!Follow(type.Name)) continue;
                var children = new List<Tuple<int, ulong, string>>();
                if (type.Name.EndsWith("[]", StringComparison.Ordinal) || type.Name == "via.clr.ManagedObject")
                {
                    int count = memory.Int(pending.Address + 28);
                    node.ArrayLength = count;
                    if (count < 0 || count > 64) continue;
                    byte[] pointers = memory.Bytes(pending.Address + 32, count * 8);
                    if (pointers == null) continue;
                    for (int i = 0; i < count; i++) children.Add(Tuple.Create(32 + i * 8, BitConverter.ToUInt64(pointers, i * 8), (string)null));
                }
                else
                    for (int offset = 16; offset + 8 <= body.Length; offset += 8)
                        children.Add(Tuple.Create(offset, BitConverter.ToUInt64(body, offset), (string)null));
                var ordered = new List<Tuple<int, ulong, string>>();
                int inspected = 0;
                foreach (var child in children.OrderBy(c => Math.Abs(c.Item1 - 0x388)))
                {
                    if (budget.Exhausted) break;
                    if (!Pointer(child.Item2) || (child.Item2 & 7) != 0 || seen.Contains(child.Item2)) continue;
                    if (++inspected > 64) break;
                    var childType = metadata.Inspect(child.Item2);
                    if (childType != null && Follow(childType.Name)) ordered.Add(Tuple.Create(child.Item1, child.Item2, childType.Name));
                }
                foreach (var child in ordered.OrderBy(c => Priority(c.Item3)))
                    Enqueue(child.Item2, pending.Path + Step(child.Item1), pending.Depth + 1);
                // Relevant components take precedence over ordinary player branches.
                queue = queue.OrderBy(p => Priority(metadata.Inspect(p.Address)?.Name)).ToList();
            }
            report.PendingObjects = queue.Count; report.ObjectCount = report.Objects.Count;
            report.ReadAttempts = budget.Attempts; report.BytesRead = budget.BytesRead; report.ElapsedMilliseconds = clock.ElapsedMilliseconds;
            report.Status = budget.Attempts >= report.ReadLimit ? "ReadBudget" : budget.BytesRead >= MaximumBytes ? "ByteBudget" : clock.ElapsedMilliseconds >= MaximumMilliseconds ? "TimeBudget" :
                report.Objects.Count >= report.ObjectLimit ? "ObjectLimit" : depthLimited ? "DepthLimit" : "Completed";
            return report;
        }
        static string Bytes(byte[] bytes) => bytes == null ? null : BitConverter.ToString(bytes).Replace('-', ' ');
    }
}
