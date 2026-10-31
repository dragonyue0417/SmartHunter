using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace SmartHunter.Wilds
{
    public sealed class WildsReader
    {
        readonly WildsReadMemory reads;
        readonly WildsReadContext context;
        readonly WildsMonsterReader monsters;
        readonly WildsPartyReader party;
        readonly WildsPlayerEffectReader effects;
        readonly WildsQuestDamage questTotals = new WildsQuestDamage();
        readonly HashSet<int> fieldTargets = new HashSet<int>();
        int fieldId = -1;
        public WildsReadStatistics LastReadStatistics { get; private set; } = new WildsReadStatistics();
        public WildsReader(IWildsMemory memory, WildsAddresses addresses, ulong moduleBase, bool optimizeReads = true, int moduleSize = 0)
        {
            reads = new WildsReadMemory(memory);
            context = new WildsReadContext(new WildsMemoryView(reads), addresses, moduleBase, optimizeReads);
            monsters = new WildsMonsterReader(context);
            party = new WildsPartyReader(context, questTotals);
            effects = new WildsPlayerEffectReader(context, moduleSize);
        }
        public static bool Valid(float value) => WildsReadContext.Number(value);
        public static bool ValidHealth(float current, float maximum) => WildsReadContext.HealthRange(current, maximum);
        public WildsSnapshot Read(bool selectedOnly = false, bool prioritizeSelected = true, bool showInCamp = false, bool collectMantleDiagnostics = false)
        {
            reads.Reset();
            long started = Stopwatch.GetTimestamp();
            if (context.BulkReads)
                context.Memory.BeginSnapshot();
            try
            { return Capture(selectedOnly, prioritizeSelected, showInCamp, collectMantleDiagnostics); }
            finally
            {
                LastReadStatistics = new WildsReadStatistics
                {
                    Calls = reads.Calls,
                    BytesRead = reads.BytesRead,
                    PointerCacheHits = context.BulkReads ? context.Memory.PointerCacheHits : 0,
                    ElapsedMilliseconds = (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency
                };
                context.Memory.EndSnapshot();
                context.Health = null;
            }
        }
        WildsSnapshot Capture(bool selectedOnly, bool prioritizeSelected, bool showInCamp, bool collectMantleDiagnostics)
        {
            var result = new WildsSnapshot();
            var memory = context.Memory;
            ulong local = context.Root("PlayerManager", "PlayerLocal");
            if (local == 0)
                return result;
            ulong playerContext = context.Follow(local, "PlayerContext");
            result.PlayerName = memory.String(memory.Ptr(playerContext + 0x38));
            if (String.IsNullOrEmpty(result.PlayerName))
                return result;
            byte[] key = memory.Bytes(context.Global("HealthEncryptionKey"), 16), round = memory.Bytes(context.Global("HealthEncryptionRound"), 16);
            if (key == null || round == null || !key.Any(x => x != 0) || !round.Any(x => x != 0))
                return result;
            context.Health = new WildsCrypto(key, round);
            ulong stage = context.Follow(local, "PlayerStage");
            result.StageId = stage == 0 ? -1 : memory.Int(stage + 0x88);
            int pauseFlags = memory.Int(context.Slot("PauseManager", "PauseState"));
            result.IsLoading = stage == 0 || result.StageId < 0 || (pauseFlags & 6) != 0;
            result.IsReady = true;
            result.AllowCampDisplay = showInCamp;
            result.InHuntingZone = !result.IsLoading && result.StageId != 12 && result.StageId != 14 && memory.Byte(stage + 0xC5) == 0;
            ulong equipment = context.Follow(local, "PlayerGear");
            result.Weapon = equipment == 0 ? -1 : memory.Byte(equipment + 0xC8);
            if (fieldId != result.StageId)
            { fieldId = result.StageId; fieldTargets.Clear(); }
            ulong quest = context.Root("QuestManager", "QuestCurrentInformation");
            float elapsed = quest == 0 ? Single.NaN : memory.Float(quest + 0xE0), duration = quest == 0 ? Single.NaN : memory.Float(quest + 0xE4);
            result.QuestSuccessState = quest == 0 ? 0 : memory.Int(quest + 0x108);
            result.QuestFailureState = quest == 0 ? 0 : memory.Int(quest + 0x114);
            bool questPresent = quest != 0 && Valid(elapsed) && Valid(duration) && duration > 0 && elapsed >= 0 && elapsed <= duration;
            bool active = questPresent && result.QuestSuccessState == 0 && result.QuestFailureState == 0 && memory.Byte(context.Slot("QuestManager", "QuestIsRetrying")) == 0;
            if (questTotals.BeginFrame(result, questPresent, active))
            { fieldTargets.Clear(); party.ClearHistory(); }
            if (active)
                party.ClearHistory();
            var questTargets = new HashSet<int>();
            if (questPresent)
            {
                result.QuestIdentity = quest;
                result.QuestElapsed = elapsed;
                ulong targetList = memory.Ptr(quest + 0x20);
                int count = targetList == 0 ? 0 : memory.Int(targetList + 0x1C);
                if (count >= 0 && count <= 20)
                    for (int i = 0; i < count; i++)
                        questTargets.Add(memory.Int(targetList + 0x24 + (ulong)i * 8));
            }
            if (result.CanDisplayData)
            {
                var visible = monsters.CaptureHeaders(playerContext);
                foreach (var monster in visible)
                    fieldTargets.Add(monster.TargetKey);
                HashSet<int> damageTargets = active ? questTargets : fieldTargets;
                result.Monsters = visible.Where(x => !selectedOnly || x.Selected).OrderByDescending(x => prioritizeSelected && x.Selected)
                    .ThenByDescending(x => damageTargets.Contains(x.TargetKey)).ThenBy(x => x.Distance).ThenBy(x => x.Address).ToList();
                foreach (var monster in result.Monsters)
                    monsters.CaptureDetails(monster);
                party.Capture(result, local, questPresent && questTotals.UseQuestCounters, damageTargets);
                CompleteQuestFrame(result, questPresent, active);
                effects.Capture(result, local, collectMantleDiagnostics);
            }
            else
            {
                CompleteQuestFrame(result, questPresent, active);
                if (!result.IsLoading && collectMantleDiagnostics) effects.CaptureMantles(result, local);
            }
            return result;
        }
        void CompleteQuestFrame(WildsSnapshot result, bool present, bool active) => questTotals.Update(result, present, active, context.Memory.Int(context.Slot("QuestManager", "QuestMissionId")));
    }
}
