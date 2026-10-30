// Network-member evidence read from the native types and getters captured locally.
using System;
using System.Collections.Generic;
using System.Linq;

namespace SmartHunter.Wilds
{
    public sealed class WildsNetworkManagedMember
    {
        public int ArraySlot, Size, ParameterSize;
        public string Type, ParameterType, Status, ParameterStatus = "NotRead";
        public bool BaseLayoutVerified;
        public WildsNetworkHierarchy Hierarchy;
    }
    public sealed class WildsNetworkNativeMember
    {
        public int TableSlot, UniqueIdSize;
        public int? NativeIndex;
        public bool? IsHost;
        public bool? IsSelf;
        public string Status, UniqueIdType, UniqueIdVtableRva;
    }
    public sealed class WildsNetworkMembersReport
    {
        public string UserListPath = "NetworkManager+0xB8+0x18", UserListStatus = "Unavailable", NativeTableStatus = "Unavailable";
        public int? UserArrayLength;
        public int NativeObjectCount;
        public List<WildsNetworkManagedMember> Users = new List<WildsNetworkManagedMember>();
        public List<WildsNetworkNativeMember> NativeMembers = new List<WildsNetworkNativeMember>();
        public bool ValidatedForSharing = false;
    }
    internal sealed class WildsNetworkObjectEvidence
    {
        public ulong Address;
        public string Path;
        public int Depth;
        public WildsNetworkTypeInfo Type;
    }
    internal sealed class WildsNetworkMembers
    {
        readonly WildsMemoryView m;
        readonly WildsNetworkMetadata metadata;
        readonly Func<bool> exhausted;
        readonly ulong module;
        readonly int moduleSize;
        public readonly List<WildsNetworkObjectEvidence> Objects = new List<WildsNetworkObjectEvidence>();
        public readonly WildsNetworkMembersReport Report = new WildsNetworkMembersReport();
        public WildsNetworkMembers(WildsMemoryView memory, WildsNetworkMetadata metadata, Func<bool> exhausted, ulong module, int moduleSize)
        { m = memory; this.metadata = metadata; this.exhausted = exhausted; this.module = module; this.moduleSize = moduleSize; }
        void Remember(ulong address, string path, int depth, WildsNetworkTypeInfo type)
        { if (type != null && !Objects.Any(o => o.Address == address)) Objects.Add(new WildsNetworkObjectEvidence { Address = address, Path = path, Depth = depth, Type = type }); }
        public void ReadUsers(ulong manager)
        {
            ulong owner=m.Ptr(manager+24);
            var ownerType=metadata.Inspect(owner);
            if(ownerType?.Name!="app.Net_UserInfoList")return;
            Remember(owner,Report.UserListPath,2,ownerType);
            ulong storage=m.Ptr(owner+16);byte[] header=m.Bytes(storage,32);
            if(header==null){Report.UserListStatus="UnreadableArray";return;}
            int capacity=BitConverter.ToInt32(header,28);Report.UserArrayLength=capacity;
            if(capacity<0||capacity>4){Report.UserListStatus="UnexpectedArrayCapacity";return;}
            byte[] slots=m.Bytes(storage+32,capacity*8);
            if(slots==null){Report.UserListStatus="UnreadableArray";return;}
            Report.UserListStatus="Completed";var encountered=new HashSet<ulong>();
            for(int slot=0;slot<capacity&&!exhausted();slot++)
            {
                ulong user=BitConverter.ToUInt64(slots,slot*8);if(user==0)continue;
                var type=metadata.Inspect(user);
                var row=new WildsNetworkManagedMember {ArraySlot=slot,Status="UnexpectedType",Type=type?.Name,Size=type?.Size??0};
                Report.Users.Add(row);
                bool direct=type?.Name=="app.Net_UserInfo",specialized=type?.Name=="app.Net_QuestUserInfo";
                if(!encountered.Add(user)||!direct&&!specialized){Report.UserListStatus="Partial";continue;}
                string path=Report.UserListPath+"+0x10+0x"+(32+slot*8).ToString("X");
                Remember(user,path,4,type);row.Status="TypedObjectOnly";row.BaseLayoutVerified=direct;
                if(specialized)
                {
                    row.Hierarchy=metadata.ReadHierarchy(type);
                    row.BaseLayoutVerified=row.Hierarchy.Status=="Completed"&&row.Hierarchy.BaseTypes.Any(parent=>parent.Name=="app.Net_UserInfo"&&parent.Size>=24);
                }
                if(!row.BaseLayoutVerified){row.ParameterStatus="BaseLayoutUnverified";continue;}
                ulong parameter=type.Size<24?0:m.Ptr(user+16);var parameterType=metadata.Inspect(parameter);
                row.ParameterStatus=parameterType==null?"Unavailable":"TypedObjectOnly";
                if(parameterType==null)continue;
                row.ParameterType=parameterType.Name;row.ParameterSize=parameterType.Size;
                Remember(parameter,path+"+0x10",5,parameterType);
            }
            if(m.Ptr(owner+16)!=storage||m.Int(storage+28)!=capacity)
            {
                Report.Users.Clear();Objects.RemoveAll(item=>item.Path.StartsWith(Report.UserListPath+"+0x10",StringComparison.Ordinal));
                Report.UserListStatus="ChangedDuringRead";
            }
            if(exhausted())Report.UserListStatus="ReadBudget";
        }
        public void ReadNative(ulong quest)
        {
            ulong table = m.Ptr(quest + 0xA8); var type = metadata.Inspect(table);
            if (type?.Name != "via.network.session.MemberInfoTbl" || type.Size != 528) return;
            Remember(table, "NetworkManager+0x90+0x20+0xA8", 3, type);
            byte[] entries = m.Bytes(table + 0x10, 64 * 8);
            if (entries == null) { Report.NativeTableStatus = "UnreadableTable"; return; }
            var positions = Enumerable.Range(0, 64).Where(i => BitConverter.ToUInt64(entries, i * 8) != 0).ToArray();
            Report.NativeObjectCount = positions.Length;
            if (positions.Length > 4) { Report.NativeTableStatus = "UnexpectedObjectCount"; return; }
            Report.NativeTableStatus = "Completed";
            WildsNetworkTypeMetadata properties = null; var seen = new HashSet<ulong>();
            foreach (int slot in positions)
            {
                if (exhausted()) break;
                ulong address = BitConverter.ToUInt64(entries, slot * 8); var info = metadata.Inspect(address);
                var member = new WildsNetworkNativeMember { TableSlot = slot, Status = "UnexpectedType" }; Report.NativeMembers.Add(member);
                if (info?.Name != "via.network.session.MemberInfo" || info.Size != 160 || !seen.Add(address)) { Report.NativeTableStatus = "Partial"; continue; }
                Remember(address, "NetworkManager+0x90+0x20+0xA8+0x" + (0x10 + slot * 8).ToString("X"), 4, info);
                if (properties == null) properties = metadata.ReadProperties(info, module, moduleSize);
                byte[] fields = m.Bytes(address, info.Size);
                if (fields == null) { member.Status = "UnreadableMember"; Report.NativeTableStatus = "Partial"; continue; }
                int indexOffset = ScalarOffset(properties, "Index", false, info.Size), hostOffset = ScalarOffset(properties, "IsHost", true, info.Size);
                if (indexOffset < 0 || hostOffset < 0) { member.Status = "GetterNotRecognized"; Report.NativeTableStatus = "Partial"; continue; }
                int index = BitConverter.ToInt32(fields, indexOffset); byte host = fields[hostOffset];
                if (index < 0 || index >= 64 || host > 1) { member.Status = "InactiveOrInvalidFields"; Report.NativeTableStatus = "Partial"; continue; }
                member.NativeIndex = index; member.IsHost = host == 1; member.Status = "GetterVerifiedFields";
                foreach (string name in new[] { "IsSelf", "IsMyself", "IsMe", "IsLocal" })
                {
                    int selfOffset = ScalarOffset(properties, name, true, info.Size);
                    if (selfOffset >= 0 && fields[selfOffset] <= 1) { member.IsSelf = fields[selfOffset] == 1; break; }
                }
                // This is a native ref-counted identity object, not a managed
                // string. Record only type/vtable evidence, never guess its value.
                var identity = properties.Properties.FirstOrDefault(p => p.Name == "UniqueId");
                byte[] code = Code(identity); byte[] load = { 0x56, 0x57, 0x48, 0x83, 0xEC, 0x28, 0x4C, 0x89, 0xC7, 0x48, 0x8B, 0xB2 };
                if (code != null && code.Length >= 16 && code.Take(load.Length).SequenceEqual(load))
                {
                    int offset = BitConverter.ToInt32(code, 12);
                    if (offset >= 0x10 && offset <= info.Size - 8)
                    {
                        ulong value = BitConverter.ToUInt64(fields, offset); var valueType = metadata.Inspect(value);
                        member.UniqueIdType = valueType?.Name ?? (value == 0 ? "Null" : "NativeOrUnverified");
                        member.UniqueIdSize = valueType?.Size ?? 0;
                        if (valueType?.Name == "via.network.utility.UniqueId") Remember(value,
                            "NetworkManager+0x90+0x20+0xA8+0x" + (0x10 + slot * 8).ToString("X") + "+0x" + offset.ToString("X"), 5, valueType);
                        ulong vtable = m.Ptr(value);
                        if (moduleSize > 0 && vtable >= module && vtable - module < (ulong)moduleSize) member.UniqueIdVtableRva = "0x" + (vtable - module).ToString("X");
                    }
                }
            }
            if (m.Ptr(quest + 0xA8) != table || !Same(entries, m.Bytes(table + 0x10, 64 * 8)))
            { Report.NativeMembers.Clear(); Objects.RemoveAll(o => o.Path.StartsWith("NetworkManager+0x90+0x20+0xA8+", StringComparison.Ordinal)); Report.NativeTableStatus = "ChangedDuringRead"; }
            if (exhausted()) Report.NativeTableStatus = "ReadBudget";
        }
        static bool Same(byte[] a, byte[] b) => b != null && a.SequenceEqual(b);
        static byte[] Code(WildsNetworkProperty property)
        {
            if (property?.GetterBytes == null) return null;
            try { return property.GetterBytes.Split(' ').Select(s => Convert.ToByte(s, 16)).ToArray(); }
            catch (FormatException) { return null; }
        }
        static int ScalarOffset(WildsNetworkTypeMetadata properties, string name, bool boolean, int size)
        {
            var property = properties.Properties.FirstOrDefault(p => p.Name == name && p.Type == (boolean ? "bool" : "s32"));
            byte[] code = Code(property);
            if (code == null || code.Length < 7 || code[0] != (boolean ? 0x8A : 0x8B) || code[1] != 0x42 ||
                code[3] != 0x41 || code[4] != (boolean ? 0x88 : 0x89) || code[5] != 0 || code[6] != 0xC3) return -1;
            int offset = unchecked((sbyte)code[2]); return offset >= 0x10 && offset <= size - (boolean ? 1 : 4) ? offset : -1;
        }
    }
}
