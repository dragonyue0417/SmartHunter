using System;
namespace SmartHunter.Wilds
{
    internal sealed class WildsReadContext
    {
        readonly WildsAddresses locations;
        readonly ulong moduleBase;
        public readonly WildsMemoryView Memory;
        public readonly bool BulkReads;
        public ulong ModuleBase => moduleBase;
        public WildsCrypto Health;
        public WildsReadContext(WildsMemoryView memory, WildsAddresses locations, ulong moduleBase, bool bulkReads)
        { Memory = memory; this.locations = locations; this.moduleBase = moduleBase; BulkReads = bulkReads; }
        public bool Has(string manager) => locations.HasAddress(manager);
        public bool HasPath(string field) => locations.HasOffset(field);
        public int[] Offsets(string field) => locations.Offsets(field);
        public ulong Global(string manager) => locations.Address(manager, moduleBase);
        public ulong Slot(string manager, string field) => HasPath(field) ? Memory.Field(Global(manager), locations.Offsets(field)) : 0;
        public ulong Root(string manager, string field) => Memory.Ptr(Slot(manager, field));
        public ulong Follow(ulong owner, string field) => HasPath(field) ? Memory.Object(owner, locations.Offsets(field)) : 0;
        public static bool Number(float value) => !Single.IsNaN(value) && !Single.IsInfinity(value);
        public static bool HealthRange(float value, float limit) => Number(value) && Number(limit) && limit > 0 && limit < 100000000 && value >= 0 && value <= limit * 1.01f;
    }
}
