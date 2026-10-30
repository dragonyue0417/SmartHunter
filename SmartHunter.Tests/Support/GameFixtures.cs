using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SmartHunter.Wilds;
using static TestSupport;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

sealed class FakeMemory : IWildsMemory
{
    readonly Dictionary<ulong, byte> bytes = new Dictionary<ulong, byte>();
    ulong next = 0x100000;
    public ulong Alloc() { ulong address = next; next += 0x2000; Put(address, new byte[0x1000]); return address; }
    public void Put(ulong address, byte[] value) { for (int i = 0; i < value.Length; i++) bytes[address + (ulong)i] = value[i]; }
    public void Ptr(ulong address, ulong value) => Put(address, BitConverter.GetBytes(value));
    public void Int(ulong address, int value) => Put(address, BitConverter.GetBytes(value));
    public void Short(ulong address, short value) => Put(address, BitConverter.GetBytes(value));
    public void Float(ulong address, float value) => Put(address, BitConverter.GetBytes(value));
    public void Byte(ulong address, byte value) => Put(address, new[] { value });
    public ulong Get(ulong address) { var b = new byte[8]; return Read(address, b, 8) ? BitConverter.ToUInt64(b, 0) : 0; }
    public bool Read(ulong address, byte[] buffer, int size)
    {
        for (int i = 0; i < size; i++) if (!bytes.TryGetValue(address + (ulong)i, out buffer[i])) return false;
        return true;
    }
    public ulong Field(ulong address, int[] offsets)
    {
        foreach (int offset in offsets) { ulong p = Get(address); if (p == 0) { p = Alloc(); Ptr(address, p); } address = p + (ulong)offset; }
        return address;
    }
    public void Object(ulong address, int[] offsets, ulong value)
    {
        for (int i = 0; i < offsets.Length; i++)
        {
            ulong cell = address + (ulong)offsets[i];
            if (i == offsets.Length - 1) { Ptr(cell, value); return; }
            ulong p = Get(cell); if (p == 0) { p = Alloc(); Ptr(cell, p); } address = p;
        }
    }
    public ulong String(string value)
    {
        ulong p = Alloc(); Int(p + 0x10, value.Length); Put(p + 0x14, System.Text.Encoding.Unicode.GetBytes(value)); return p;
    }
    public ulong Array(params ulong[] values)
    {
        ulong p = Alloc(); Int(p + 0x1C, values.Length); for (int i = 0; i < values.Length; i++) Ptr(p + 0x20 + (ulong)i * 8, values[i]); return p;
    }
}

sealed class FakeGame
{
    public readonly FakeMemory M = new FakeMemory();
    public readonly WildsAddresses Addresses;
    public readonly ulong Base = 0x140000000;
    public ulong Local, Context, Stage, MonsterHeader, Elements;
    public readonly List<ulong> Monsters = new List<ulong>();
    readonly byte[] key = Enumerable.Range(1, 16).Select(i => (byte)i).ToArray();
    readonly byte[] round = Enumerable.Range(20, 16).Select(i => (byte)i).ToArray();
    // Independent forward S-box from the standard algebraic AES definition.
    static byte Mul(byte a, byte b) { byte r = 0; for (int i = 0; i < 8; i++) { if ((b & 1) != 0) r ^= a; a = (byte)((a << 1) ^ ((a & 0x80) != 0 ? 0x11B : 0)); b >>= 1; } return r; }
    static byte SBox(byte x) { byte y = 1; if (x == 0) y = 0; else for (int i = 0; i < 254; i++) y = Mul(y, x); byte Rotate(byte n, int count) => (byte)((n << count) | (n >> (8 - count))); return (byte)(y ^ Rotate(y, 1) ^ Rotate(y, 2) ^ Rotate(y, 3) ^ Rotate(y, 4) ^ 0x63); }
    public FakeGame()
    {
        Addresses = LayoutFixtures.Create();
        M.Put(A("HealthEncryptionKey"), key); M.Put(A("HealthEncryptionRound"), round);
        Local = M.Alloc(); Context = M.Alloc(); Stage = M.Alloc();
        M.Ptr(F("PlayerManager", "PlayerLocal"), Local);
        O(Local, "PlayerContext", Context); O(Local, "PlayerStage", Stage);
        M.Ptr(Context + 0x38, M.String("測試 Hunter")); M.Int(Stage + 0x88, 1);
        M.Int(F("PauseManager", "PauseState"), 0);
        MonsterHeader = F("EnemyManager", "EnvironmentMonsterList"); Elements = M.Array(); M.Ptr(MonsterHeader, Elements);
    }
    public ulong A(string key) => Addresses.Address(key, Base);
    public ulong F(string root, string key) => M.Field(A(root), Addresses.Offsets(key));
    public void O(ulong root, string key, ulong value) => M.Object(root, Addresses.Offsets(key), value);
    public ulong Encrypted(float value)
    {
        ulong p = M.Alloc(); var block = new byte[16]; var raw = BitConverter.GetBytes(value); int[] offsets = { 0, 13, 10, 7 };
        for (int i = 0; i < 4; i++) block[offsets[i]] = (byte)(SBox((byte)(raw[i] ^ round[i])) ^ key[offsets[i]]);
        M.Put(p + 0x10, block); return p;
    }
    public ulong Monster(int id, float health = 1000, int category = 0, bool enabled = true)
    {
        ulong p = M.Alloc(), basic = M.Alloc(), hp = M.Alloc(), context = M.Alloc();
        O(p, "MonsterMagic", 0x6D0045); O(p, "MonsterContextData", context);
        O(p, "MonsterBasicData", basic); O(p, "MonsterHealth", hp);
        M.Byte(basic + 0x10, enabled ? (byte)1 : (byte)0); M.Int(basic + 0x48, id); M.Int(basic + 0x54, category);
        M.Ptr(hp + 0x10, Encrypted(health)); M.Ptr(hp + 0x18, Encrypted(1000)); M.Short(context + 0x3EC, 112);
        O(p, "MonsterTargetKey", (ulong)(id + 100));
        Monsters.Add(p); M.Int(MonsterHeader + 8, Monsters.Count); M.Ptr(Elements + 0x20 + (ulong)(Monsters.Count - 1) * 8, p);
        return p;
    }
    public void Select(ulong monster)
    {
        var v = new WildsMemoryView(M); ulong context = v.Object(monster, Addresses.Offsets("MonsterContext"));
        M.Ptr(F("CameraManager", "CameraMonsterTarget"), context);
    }
    public void SetPopulation(params ulong[] actors)
    {
        Monsters.Clear(); Monsters.AddRange(actors); M.Int(MonsterHeader + 8, actors.Length);
        for (int i = 0; i < actors.Length; i++) M.Ptr(Elements + 0x20 + (ulong)i * 8, actors[i]);
    }
    public void SetHealth(ulong monster, float health)
    {
        ulong component = new WildsMemoryView(M).Object(monster, Addresses.Offsets("MonsterHealth"));
        M.Ptr(component + 0x10, Encrypted(health));
    }
    public WildsSnapshot Read(bool selected = false, bool prioritizeSelected = true) => new WildsReader(M, Addresses, Base).Read(selected, prioritizeSelected);
}

static class LayoutFixtures
{
    sealed class Reference
    {
        public Dictionary<string, ulong> Addresses { get; set; }
        public Dictionary<string, int[]> Offsets { get; set; }
    }
    static string ReadResource(string name)
    {
        var assembly = typeof(LayoutFixtures).Assembly;
        string resource = assembly.GetManifestResourceNames().Single(n => n.EndsWith(name, StringComparison.Ordinal));
        using (var stream = assembly.GetManifestResourceStream(resource))
        using (var reader = new StreamReader(stream)) return reader.ReadToEnd();
    }
    static readonly Reference reference = JsonConvert.DeserializeObject<Reference>(ReadResource("wilds-layout-reference.json"));
    public static WildsAddresses Create(long shift = 0) => new WildsAddresses("1.42.0.2",
        reference.Addresses.ToDictionary(p => p.Key, p => checked((ulong)checked((long)p.Value + shift))), reference.Offsets);
    public static Dictionary<string, int[]> ExpectedOffsets() => reference.Offsets.ToDictionary(p => p.Key, p => (int[])p.Value.Clone());
    public static JObject UserScan() => JObject.Parse(ReadResource("user-scan-1.42.0.2.json"));
}
