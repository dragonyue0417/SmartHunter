using System.Collections.Generic;
namespace SmartHunter.Wilds
{
    public sealed class WildsSnapshot
    {
        public bool IsReady, InHuntingZone;
        public bool IsLoading, AllowCampDisplay;
        public bool CanDisplayData => IsReady && !IsLoading && (InHuntingZone || AllowCampDisplay);
        public string DisplayState => IsLoading ? "Loading" : !IsReady ? "Retrying" :
            IsQuestDamageFrozen ? "QuestEnded" : InHuntingZone ? "Working" : CanDisplayData ? "Camp" : "SafeZone";
        public string PlayerName = "";
        public int Weapon, StageId;
        public ulong QuestIdentity;
        public float QuestElapsed;
        public int QuestSuccessState, QuestFailureState;
        public bool IsQuestDamageFrozen;
        public int DamageResetVersion;
        public List<WildsMonsterSnapshot> Monsters = new List<WildsMonsterSnapshot>();
        public List<WildsPlayerSnapshot> Players = new List<WildsPlayerSnapshot>();
        public List<WildsEffectSnapshot> Effects = new List<WildsEffectSnapshot>();
    }
    public sealed class WildsMonsterSnapshot
    {
        public ulong Address, Context;
        public int Id, TargetKey, Size, Role, Legendary;
        public float Health, MaxHealth, Distance;
        public bool Selected;
        public List<WildsPartSnapshot> Parts = new List<WildsPartSnapshot>();
        public List<WildsAilmentSnapshot> Ailments = new List<WildsAilmentSnapshot>();
    }
    public sealed class WildsPartSnapshot
    {
        public ulong Address;
        public string NameId;
        public float Health, MaxHealth;
        public int Count, Slot;
        public bool Severable;
    }
    public sealed class WildsAilmentSnapshot
    {
        public ulong Address; public int Id; public bool Active;
        public float Remaining, MaxDuration, Buildup, MaxBuildup;
    }
    public sealed class WildsPlayerSnapshot
    {
        public ulong Address; public string Name; public int Slot, Damage; public bool IsNpc, DamageAvailable;
    }
    public sealed class WildsEffectSnapshot
    {
        public int Index; public float Remaining, Max; public bool Infer;
    }
}
