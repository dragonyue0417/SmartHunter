using System;

namespace SmartHunter.Wilds
{
    // External enum values are not EM identifiers. Keep this conversion at the reader boundary.
    public static class WildsMonsterTypes
    {
        static readonly string[] identifiers =
        {
            "em001_00", // 0: RATHIAN
            "em002_00", // 1: RATHALOS
            "em002_50", // 2: GUARDIAN_RATHALOS
            "em005_00", // 3: GRAVIOS
            "em008_00", // 4: YIAN_KUT_KU
            "em009_00", // 5: GYPCEROS
            "em021_00", // 6: CONGALALA
            "em022_00", // 7: BLANGONGA
            "em046_00", // 8: LAGIACRUS
            "em070_00", // 9: NERSCYLLA
            "em071_00", // 10: GORE_MAGALA
            "em077_00", // 11: SEREGIOS
            "em078_00", // 12: GOGMAZIOS
            "em082_00", // 13: MIZUTSUNE
            "em100_51", // 14: GUARDIAN_FULGUR_ANJANATH
            "em113_51", // 15: GUARDIAN_EBONY_ODOGARON
            "em150_00", // 16: DOSHAGUMA
            "em150_50", // 17: GUARDIAN_DOSHAGUMA
            "em151_00", // 18: BALAHARA
            "em152_00", // 19: CHATACABRA
            "em153_00", // 20: QUEMATRICE
            "em154_00", // 21: LALA_BARINA
            "em155_00", // 22: ROMPOPOLO
            "em156_00", // 23: REY_DAU
            "em157_00", // 24: UTH_DUNA
            "em158_00", // 25: NU_UDRA
            "em159_00", // 26: AJARAKAN
            "em160_00", // 27: ARKVELD
            "em160_50", // 28: GUARDIAN_ARKVELD
            "em161_00", // 29: HIRABAMI
            "em162_00", // 30: JIN_DAHAAD
            "em163_00", // 31: XU_WU
            "em164_50", // 32: ZOH_SHIA
            "em165_00", // 33: HIGH_PURRFORMANCE_BARREL_PUNCHER
            "em166_00", // 34: OMEGA_PLANETES
        };
        public static int Count => identifiers.Length;
        public static string Id(int index) => index >= 0 && index < identifiers.Length ? identifiers[index] : "unknown_" + index;
        public static int Index(string id) => Array.IndexOf(identifiers, id);
    }
}
