// External, read-only RE Engine metadata inspection. No game methods are invoked.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SmartHunter.Wilds
{
    internal sealed class WildsNetworkTypeInfo
    {
        public string Name;
        public int Size;
        public ulong TypeAddress;
    }
    public sealed class WildsNetworkProperty
    {
        public string Name, Type, GetterRva, GetterBytes;
        public uint Flags, VariableType;
        public int GetterParameter;
        public string GetterHelperRva, GetterHelperBytes;
    }
    public sealed class WildsNetworkBaseType
    {
        public string Name;
        public int Size;
    }
    public sealed class WildsNetworkHierarchy
    {
        public string Status = "Completed";
        public List<WildsNetworkBaseType> BaseTypes = new List<WildsNetworkBaseType>();
    }
    public sealed class WildsNetworkTypeMetadata
    {
        public string Type, Status;
        public int PropertyCount;
        public List<WildsNetworkProperty> Properties = new List<WildsNetworkProperty>();
    }
    internal sealed class WildsNetworkMetadata
    {
        readonly WildsMemoryView m;
        readonly Func<bool> exhausted;
        readonly Dictionary<ulong, WildsNetworkTypeInfo> types = new Dictionary<ulong, WildsNetworkTypeInfo>();
        readonly Dictionary<ulong, WildsNetworkTypeInfo> objects = new Dictionary<ulong, WildsNetworkTypeInfo>();
        readonly Dictionary<ulong, WildsNetworkHierarchy> hierarchies = new Dictionary<ulong, WildsNetworkHierarchy>();
        public int CacheHits;
        public WildsNetworkMetadata(WildsMemoryView memory, Func<bool> exhausted) { m = memory; this.exhausted = exhausted; }
        static bool Pointer(ulong address) => address >= 0x10000 && address <= 0x00007FFFFFFFF000;
        internal string CString(ulong address, int maximum = 256)
        {
            if (!Pointer(address)) return null;
            var text = new StringBuilder(); int at = 0;
            while (at < maximum && !exhausted())
            {
                ulong next = address + (ulong)at;
                int size = Math.Min(Math.Min(32, maximum - at), 4096 - (int)(next & 4095));
                byte[] bytes = m.Bytes(next, size);
                // Small reads are only a fallback at an unreadable boundary.
                while (bytes == null && size > 1 && !exhausted()) { size = Math.Max(1, size / 2); bytes = m.Bytes(next, size); }
                if (bytes == null) return null;
                foreach (byte b in bytes)
                {
                    if (b == 0) return text.Length > 0 ? text.ToString() : null;
                    if (b < 32 || b > 126) return null;
                    text.Append((char)b);
                }
                at += size;
            }
            return null;
        }
        public WildsNetworkTypeInfo Inspect(ulong obj) => Inspect(obj, out _);
        public WildsNetworkTypeInfo Inspect(ulong obj, out string reason)
        {
            reason = "ObjectPointerInvalid";
            if (!Pointer(obj) || (obj & 7) != 0) return null;
            if (objects.TryGetValue(obj, out var cached)) { CacheHits++; reason = cached == null ? "RejectedObject" : "MetadataValidated"; return cached; }
            ulong info = m.Ptr(obj);
            if (types.TryGetValue(info, out cached)) { CacheHits++; Remember(obj, cached); reason = cached == null ? "RejectedType" : "MetadataValidated"; return cached; }
            reason = "MissingTypeDefinition";
            ulong definition = Pointer(info) ? m.Ptr(info) : 0;
            byte[] data = Pointer(definition) ? m.Bytes(definition, 0x48) : null;
            WildsNetworkTypeInfo result = null;
            if (data != null)
            {
                reason = "ManagedTableMismatch";
                if (BitConverter.ToUInt64(data, 0x40) == info)
                {
                    int size = BitConverter.ToInt32(data, 0x14);
                    ulong type = BitConverter.ToUInt64(data, 0x38);
                    byte[] runtime = Pointer(type) ? m.Bytes(type, 0x38) : null;
                    reason = "InvalidTypeOrSize: " + size;
                    if (runtime != null && size >= 16 && size <= 0x10000)
                    {
                        int runtimeSize = BitConverter.ToInt32(runtime, 0x30);
                        reason = "TypeSizeMismatch: definition=" + size + ", runtime=" + runtimeSize;
                        if (runtimeSize == size)
                        {
                            string name = CString(BitConverter.ToUInt64(runtime, 0x20));
                            reason = "InvalidTypeName";
                            if (name != null) { result = new WildsNetworkTypeInfo { Name = name, Size = size, TypeAddress = type }; reason = "MetadataValidated"; }
                        }
                    }
                }
            }
            if (Pointer(info) && types.Count < 512) types[info] = result;
            Remember(obj, result); return result;
        }
        void Remember(ulong obj, WildsNetworkTypeInfo type) { if (objects.Count < 1024) objects[obj] = type; }
        public WildsNetworkHierarchy ReadHierarchy(WildsNetworkTypeInfo type)
        {
            if (hierarchies.TryGetValue(type.TypeAddress, out var cached)) return cached;
            var result = new WildsNetworkHierarchy(); ulong at = type.TypeAddress; int size = type.Size;
            var seen = new HashSet<ulong> { at };
            for (int i = 0; i < 4 && !exhausted(); i++)
            {
                byte[] pointer = m.Bytes(at + 0x40, 8);
                if (pointer == null) { result.Status = "UnreadableBaseType"; break; }
                at = BitConverter.ToUInt64(pointer, 0); if (at == 0) break;
                if (!Pointer(at) || !seen.Add(at)) { result.Status = "InvalidBaseChain"; break; }
                byte[] definition = m.Bytes(at, 0x38);
                if (definition == null) { result.Status = "UnreadableBaseType"; break; }
                int nextSize = BitConverter.ToInt32(definition, 0x30);
                string name = CString(BitConverter.ToUInt64(definition, 0x20));
                if (name == null || nextSize < 16 || nextSize > size) { result.Status = "InvalidBaseType"; break; }
                result.BaseTypes.Add(new WildsNetworkBaseType { Name = name, Size = nextSize }); size = nextSize;
                if(size==16 && (name=="via.clr.ManagedObject" || name=="System.Object"))break;
                if (i == 3) result.Status = "DepthLimit";
            }
            if (exhausted()) result.Status = "ReadBudget";
            hierarchies[type.TypeAddress] = result; return result;
        }
        public WildsNetworkTypeMetadata ReadProperties(WildsNetworkTypeInfo type, ulong module, int moduleSize, bool equipment = false)
        {
            var result = new WildsNetworkTypeMetadata { Type = type.Name, Status = "NotAvailable" };
            ulong fields = m.Ptr(type.TypeAddress + 0x58), list = Pointer(fields) ? m.Ptr(fields + 0x20) : 0;
            byte[] header = Pointer(list) ? m.Bytes(list, 0x18) : null;
            if (header == null) return result;
            int count = BitConverter.ToInt32(header, 0x10); result.PropertyCount = count;
            if (count < 0 || count > 256) { result.Status = "InvalidCount"; return result; }
            ulong data = BitConverter.ToUInt64(header, 8);
            byte[] pointers = Pointer(data) ? m.Bytes(data, count * 8) : null;
            if (pointers == null) return result;
            result.Status = "Completed";
            int maximumProperties = equipment ? 16 : 32;
            for (int i = 0; i < count && result.Properties.Count < maximumProperties && !exhausted(); i++)
            {
                ulong descriptor = BitConverter.ToUInt64(pointers, i * 8);
                byte[] property = Pointer(descriptor) ? m.Bytes(descriptor, 0x30) : null;
                if (property == null) continue;
                string name = CString(BitConverter.ToUInt64(property, 0), 96);
                if (name == null || !Interesting(name) && type.Name != "via.network.session.MemberInfo" && !(equipment &&
                    (type.Name.Contains("ActiveSkill") || type.Name.Contains("ASkill") || name.Contains("ASkill") || name.Contains("ActiveSkill") || name.Contains("Character")))) continue;
                var item = new WildsNetworkProperty { Name = name, Type = CString(BitConverter.ToUInt64(property, 0x20), 128),
                    Flags = BitConverter.ToUInt32(property, 0x18), GetterParameter = BitConverter.ToInt32(property, 0x28), VariableType = BitConverter.ToUInt32(property, 0x2C) };
                ulong getter = BitConverter.ToUInt64(property, 0x10);
                int codeLength = equipment ? 128 : 64;
                if (moduleSize >= codeLength && getter >= module && getter - module <= (ulong)(moduleSize - codeLength))
                {
                    item.GetterRva = "0x" + (getter - module).ToString("X");
                    byte[] bytes = m.Bytes(getter, codeLength);
                    if (bytes != null)
                    {
                        item.GetterBytes = BitConverter.ToString(bytes).Replace('-', ' ');
                        // Exact native identity wrapper: preserve one direct-call
                        // target for offline inspection; never invoke the helper.
                        byte[] wrapper = { 0x56, 0x48, 0x83, 0xEC, 0x20, 0x4C, 0x89, 0xC6, 0x48, 0x89, 0xD1, 0xE8 };
                        if (type.Name.StartsWith("via.network.utility.", StringComparison.Ordinal) && bytes.Take(wrapper.Length).SequenceEqual(wrapper))
                        {
                            long helperRva = checked((long)(getter - module) + 16 + BitConverter.ToInt32(bytes, 12));
                            if (helperRva >= 0 && moduleSize >= 128 && helperRva <= moduleSize - 128)
                            {
                                byte[] helper = m.Bytes(module + (ulong)helperRva, 128);
                                if (helper != null) { item.GetterHelperRva = "0x" + helperRva.ToString("X"); item.GetterHelperBytes = BitConverter.ToString(helper).Replace('-', ' '); }
                            }
                        }
                    }
                }
                result.Properties.Add(item);
            }
            if (exhausted()) result.Status = "ReadBudget";
            else if (result.Properties.Count >= maximumProperties && count > maximumProperties) result.Status = "PropertyLimit";
            return result;
        }
        static bool Interesting(string name)
        {
            string n = name.ToLowerInvariant();
            if (new[] { "password", "passwd", "token", "secret", "auth" }.Any(n.Contains)) return false;
            return new[] { "session", "lobby", "host", "member", "hunter", "user", "index", "id", "state", "value", "string", "valid", "party", "param", "rank", "hash" }.Any(n.Contains);
        }
    }
}
