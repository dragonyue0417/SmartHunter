using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
namespace SmartHunter.Wilds
{
    // Native field positions are game-format facts. All entry points are
    // resolved by the scanner before a reader receives this layout.
    public static class WildsMemoryLayout
    {
        public const string LayoutVersion = "1.42.0.2";
        static readonly Dictionary<string, int[]> fields = CreateFields();
        static Dictionary<string, int[]> CreateFields()
        {
            var result = new Dictionary<string, int[]>(StringComparer.Ordinal);
            void Define(string name, params int[] offsets) => result.Add(name, offsets);
            Define("EnvironmentMonsterList", 0x2D0);
            Define("MonsterMagic", 0x10, 0x28, 0x14);
            Define("MonsterContext", 0x30);
            Define("MonsterContextData", 0x30, 0x40);
            Define("MonsterBasicData", 0x30, 0x40, 0x108);
            Define("MonsterHealth", 0x30, 0x38, 0x10);
            Define("MonsterRage", 0x30, 0x40, 0x138, 0x18);
            Define("MonsterStamina", 0x30, 0x40, 0x138, 0x20);
            Define("MonsterStatusEffects", 0x30, 0x40, 0x138, 0xC0);
            Define("MonsterParts", 0x30, 0x40, 0x128);
            Define("CameraMonsterTarget", 0xB0, 0x230, 0x20, 0x30);
            Define("MonsterTargetKey", 0x30, 0x40, 0x100, 0x18);
            Define("PlayerLocal", 0x50, 0x20, 0x10);
            Define("PlayerList", 0x50, 0x20);
            Define("PlayerStage", 0x30, 0x38, 0x18);
            Define("PlayerContext", 0x30, 0x48);
            Define("PlayerGear", 0x30, 0x40, 0x10);
            Define("PlayerParty", 0x60);
            Define("PlayerDamageHistory", 0x18, 0x680, 0xA8, 0x20, 0x38);
            Define("PlayerConsumableEffects", 0x30, 0x40, 0x50, 0x38);
            Define("PlayerHornEffects", 0x30, 0x40, 0x50, 0x40, 0x38);
            Define("PlayerDebuffs", 0x30, 0x40, 0x50, 0x60);
            Define("PlayerPalicoEffects", 0x30, 0x40, 0x50, 0x40, 0x30);
            Define("PlayerSkillEffects", 0x30, 0x40, 0x50, 0x40, 0x40);
            Define("PlayerQuestPlayerIndexes", 0x78, 0x10);
            Define("QuestIsRetrying", 0x178, 0xD5);
            Define("QuestCurrentInformation", 0x178, 0x38, 0x18);
            Define("QuestLocalPlayerDamage", 0x178, 0x48, 0x50, 0x30);
            Define("QuestRemotePlayerDamage", 0x178, 0x48, 0x60, 0x30);
            Define("PauseState", 0x50, 0x10, 0x20);
            Define("SaveIndex", 0xD8);
            Define("SaveData", 0x80, 0x10, 0x20);
            Define("PlayerSpecializedTools", 0x18, 0x3A8, 0xB0, 0x38);
            Define("QuestArenaSelectedLoadout", 0x178, 0x30, 0x30, 0x3C);
            Define("QuestMissionId", 0x178, 0x30, 0x18, 0x20, 0x10);
            Define("PlayerIsInArena", 0x50, 0x20, 0x18, 0x20, 0xF1);
            Define("QuestArenaData", 0xF0, 0x48, 0x48);
            Define("SaveSpecializedTool", 0x90, 0x10, 0x10);
            Define("NpcParty", 0xE0, 0x28);
            Define("NpcDamageHistory", 0x28, 0x680, 0xA8, 0x20, 0x38);
            return result;
        }
        public static Dictionary<string, int[]> CopyOffsets() => fields.ToDictionary(field => field.Key, field => (int[])field.Value.Clone(), StringComparer.Ordinal);
        public static bool Supports(string version) => version == "1.42.0.2" || version == "1.42.0.3";
        static ulong Displace(ulong root, long offset) => checked((ulong)checked((long)root + offset));
        static Dictionary<string, ulong> Require(IReadOnlyDictionary<string, ulong> scanned, params string[] names)
        {
            var verified = new Dictionary<string, ulong>(StringComparer.Ordinal);
            foreach (string name in names)
            {
                if (!scanned.TryGetValue(name, out ulong value))
                    throw new InvalidDataException("Required scan result is missing: " + name);
                verified.Add(name, value);
            }
            return verified;
        }
        public static WildsAddresses Resolve(IReadOnlyDictionary<string, ulong> scannedRvas, string version)
        {
            var roots = Require(scannedRvas, "PlayerManager", "EnemyManager", "SaveManager", "QuestManager", "CameraManager", "PauseManager", "HealthEncryptionRound", "NpcManager");
            roots.Add("HealthEncryptionKey", Displace(roots["HealthEncryptionRound"], 16));
            return new WildsAddresses(version, roots, fields);
        }
    }
}
