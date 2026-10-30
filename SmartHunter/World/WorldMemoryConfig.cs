using SmartHunter.Core;

namespace SmartHunter.Game.Config
{
    public class WorldMemoryConfig
    {
        public string ProcessName = "MonsterHunterWorld";

        public int ThreadsPerScan = 1;
        public int InitialScanDelaySeconds = 10;
        public int ScanRetryIntervalSeconds = 5;
        public int ScanMaxRetryIntervalSeconds = 30;
        public bool AutoResolvePlayerNameCollection = true;
        // Byte offset, in decimal in JSON. -48 is the community -0x30 layout.
        // Auto mode validates both layouts against native player/session fields.
        public long PlayerNameCollectionOffset = 0;

        public BytePatternConfig CurrentPlayerNamePattern = new BytePatternConfig(
            "CurrentPlayerNamePattern",
            "48 8B 0D ?? ?? ?? ?? 48 8D 55 ?? 45 33 C9 44 8B C0 E8",
            "1402B4DF9"
            );

        public BytePatternConfig CurrentWeaponPattern = new BytePatternConfig(
            "CurrentWeaponPattern",
            "48 8B 0D ?? ?? ?? ?? 4C 8D 45 ?? 48 8D 97 ?? ?? ?? ?? E8 ?? ?? ?? ?? 48 8B D8 48 85 C0 ",
            "1411A5DD6"
            );

        public BytePatternConfig PlayerDamagePattern = new BytePatternConfig(
            "PlayerDamagePattern",
            "48 8B 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 48 8B D8 48 85 C0 75 04 33 C9",
            "141A5EF27"
            );

        public BytePatternConfig PlayerNamePattern = new BytePatternConfig(
            "PlayerNamePattern",
            "48 8B 0D ?? ?? ?? ?? 48 8D 54 24 38 C6 44 24 20 00 E8 ?? ?? ?? ?? 48 8B 5C 24 70 48 8B 7C 24 60 48 83 C4 68 C3",
            "140625CD7"
            );

        public BytePatternConfig MonsterPattern = new BytePatternConfig(
            "MonsterPattern",
            "48 8B 0D ?? ?? ?? ?? B2 01 E8 ?? ?? ?? ?? C6 83 ?? ?? ?? ?? ?? 48 8B 0D",
            "14024AB49"
            );

        public BytePatternConfig PlayerBuffPattern = new BytePatternConfig(
            "PlayerBuffPattern",
            "48 8B 05 ?? ?? ?? ?? 41 8B 94 00 ?? ?? ?? ?? 89 57 ??",
            "1422455C1"
            );

        public BytePatternConfig SelectedMonsterPattern = new BytePatternConfig(
            "SelectedMonsterPattern",
            "48 8B 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 48 8B 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 83 A0 ?? ?? ?? ?? ?? C6 43 ?? ??",
            "141A98292"
            );

        public BytePatternConfig LobbyStatusPattern = new BytePatternConfig(
            "LobbyStatusPattern",
            "48 8B 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 48 8B 4E ?? F3 0F 10 86 ?? ?? ?? ?? F3 0F 58 86 ?? ?? ?? ?? F3 0F 11 86 ?? ?? ?? ?? E8 ?? ?? ?? ?? 48 8B 4E ??",
            "1402CA714"
            );

        public BytePatternConfig DamageOnScreenPattern = new BytePatternConfig(
            "DamageOnScreenPattern",
            "48 8B 0D ?? ?? ?? ?? 45 33 C0 48 8B D0 E8 ?? ?? ?? ?? 48 8B F0 48 39 83 ?? ?? ?? ??",
            "14049F00B"
            );
    }
}
