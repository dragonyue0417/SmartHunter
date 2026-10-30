using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;

namespace SmartHunter.Wilds
{
    public sealed class WildsMonsterScalarCandidate
    {
        public string Path;
        public int Value;
    }
    public sealed class WildsMonsterIdentityCheck
    {
        public int TargetKey;
        public string Status, Type;
        public string ContextPath = "Context";
        public string HolderType;
        public int ObjectSize;
        public List<WildsNetworkProperty> Properties = new List<WildsNetworkProperty>();
        public List<WildsNetworkValue> BlockCandidates = new List<WildsNetworkValue>();
        public List<WildsMonsterScalarCandidate> ScalarCandidates = new List<WildsMonsterScalarCandidate>();
    }
    // Offline comparison candidates only. Never invoke getters or use these hashes
    // as an entity ID. Restrict reads to a verified enemy context's object body.
    internal static class WildsMonsterIdentityProbe
    {
        public static WildsMonsterIdentityCheck Capture(WildsMonsterSnapshot monster, WildsMemoryView memory,
            WildsNetworkMetadata metadata, ulong module, int moduleSize, Func<bool> exhausted, Dictionary<string, WildsNetworkTypeMetadata> cache)
        {
            var result = new WildsMonsterIdentityCheck { TargetKey = monster.TargetKey, Status = "ContextUnverified" };
            ulong context=monster.Context;
            var type = metadata.Inspect(context);
            if(type?.Name=="app.cEnemyContextHolder" && type.Size==0x48)
            {
                result.HolderType=type.Name;result.ContextPath="ContextHolder+0x40";
                context=memory.Ptr(context+0x40);type=metadata.Inspect(context);
            }
            result.Type = type?.Name; result.ObjectSize = type?.Size ?? 0;
            if (type == null || type.Name != "app.cEnemyContext") return result;
            if (!cache.TryGetValue(type.Name, out var properties)) { properties = metadata.ReadProperties(type, module, moduleSize); cache[type.Name] = properties; }
            result.Properties = properties.Properties;
            result.Status = properties.Status;
            if (exhausted()) { result.Status = "ReadBudget"; return result; }
            int size = Math.Min(type.Size, 0x400);
            if (size <= 0x20) return result;
            var bytes = memory.Bytes(context + 0x10, size - 0x10);
            if (bytes == null) { result.Status = "BodyUnreadable"; return result; }
            // Narrow scalar candidates use the same already-read, typed enemy
            // body. Never dereference them or use them for production pairing.
            // Omit empty/sentinel values and both halves of pointer-like words.
            for (int at = 0; at + 4 <= bytes.Length && result.ScalarCandidates.Count < 96; at += 4)
            {
                int word = at & ~7;
                if (word + 8 <= bytes.Length && IsPointer(BitConverter.ToUInt64(bytes, word))) continue;
                int value = BitConverter.ToInt32(bytes, at);
                if (value == 0 || value == -1) continue;
                result.ScalarCandidates.Add(new WildsMonsterScalarCandidate { Path = "Context+0x" + (at + 0x10).ToString("X"), Value = value });
            }
            using (var sha = SHA256.Create())
            {
                for (int at = 0; at + 16 <= bytes.Length; at += 8)
                {
                    // Exclude windows containing aligned process pointers. Hashing
                    // prevents exporting arbitrary object contents or strings.
                    if (Enumerable.Range(0, 2).Any(i => IsPointer(BitConverter.ToUInt64(bytes, at + i * 8)))) continue;
                    if (Enumerable.Range(0, 16).All(i => bytes[at + i] == 0)) continue;
                    result.BlockCandidates.Add(new WildsNetworkValue { Path = "Context+0x" + (at + 0x10).ToString("X"), Length = 16,
                        Sha256 = BitConverter.ToString(sha.ComputeHash(bytes, at, 16)).Replace("-", "").ToLowerInvariant() });
                }
            }
            return result;
        }
        static bool IsPointer(ulong value) => value >= 0x10000 && value <= 0x00007FFFFFFFF000;
    }
}
