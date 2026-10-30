using System;
using System.Collections.Generic;
using System.Linq;

namespace SmartHunter.Wilds
{
    // Keep a quest's damage source and roster through clear/fail/quit transitions.
    // The game's free-roam hit history is a different counter, not its final result.
    public sealed class WildsQuestDamage
    {
        ulong quest;
        int mission;
        float elapsed;
        bool ended;
        bool returned;
        bool completionSeen, loadingAfterEnd;
        int completionStage = -1;
        int resetVersion;
        readonly Dictionary<string,int> historyBaseline = new Dictionary<string,int>();
        public bool UseQuestCounters => !returned;
        List<WildsPlayerSnapshot> players = new List<WildsPlayerSnapshot>();
        static WildsPlayerSnapshot Copy(WildsPlayerSnapshot p) => new WildsPlayerSnapshot
        { Address = p.Address, Name = p.Name, Slot = p.Slot, Damage = p.Damage, IsNpc = p.IsNpc, DamageAvailable = p.DamageAvailable };
        static bool Same(WildsPlayerSnapshot a, WildsPlayerSnapshot b) => a.Slot == b.Slot && a.Address == b.Address && a.Name == b.Name && a.IsNpc == b.IsNpc;
        void ClearResult() { quest = 0; mission = 0; elapsed = 0; ended = false; players.Clear(); }
        public void Clear() { ClearResult(); returned = completionSeen = loadingAfterEnd = false; completionStage = -1; historyBaseline.Clear(); resetVersion++; }
        public bool BeginFrame(WildsSnapshot snapshot, bool available, bool active)
        {
            bool completed = ended || snapshot.QuestSuccessState != 0 || snapshot.QuestFailureState != 0 ||
                quest != 0 && !available && !snapshot.IsLoading && (snapshot.StageId == 12 || snapshot.StageId == 14);
            if (completed && !completionSeen) { completionSeen = true; completionStage = snapshot.StageId; }
            if (completionSeen && snapshot.IsLoading) loadingAfterEnd = true;
            bool returnTransition = loadingAfterEnd || completionSeen && completionStage != snapshot.StageId || !available;
            // Loading and a mid-quest visit to camp are not a result return.
            bool reset = !returned && snapshot.IsReady && !snapshot.IsLoading && !snapshot.InHuntingZone && !active && completionSeen && returnTransition;
            if (reset)
            {
                ClearResult(); returned = true; resetVersion++; historyBaseline.Clear();
            }
            if (active) { returned = completionSeen = loadingAfterEnd = false; completionStage = -1; historyBaseline.Clear(); }
            snapshot.DamageResetVersion = resetVersion;
            return reset;
        }
        public void Update(WildsSnapshot snapshot, bool available, bool active, int missionId)
        {
            BeginFrame(snapshot, available, active);
            if (returned && !active)
            {
                snapshot.QuestIdentity = 0; snapshot.QuestElapsed = 0; snapshot.IsQuestDamageFrozen = false;
                foreach (var player in snapshot.Players)
                {
                    string key = player.Slot + ":" + player.Address + ":" + player.Name;
                    int baseline;
                    if (!historyBaseline.TryGetValue(key, out baseline))
                        historyBaseline[key] = baseline = player.Damage;
                    if (player.Damage < baseline) historyBaseline[key] = baseline = 0;
                    player.Damage = Math.Max(0, player.Damage - baseline);
                }
                return;
            }
            if (available && (quest == 0 || (active && (quest != snapshot.QuestIdentity || mission != missionId ||
                ended || snapshot.QuestElapsed < elapsed - 5000))))
            { ClearResult(); quest = snapshot.QuestIdentity; mission = missionId; }
            if (active)
            {
                if (snapshot.Players.Count == 0) return;
                foreach (var player in snapshot.Players)
                {
                    var previous = players.FirstOrDefault(p => Same(p, player));
                    if (previous != null) player.Damage = player.DamageAvailable ? Math.Max(previous.Damage, player.Damage) : previous.Damage;
                }
                players = snapshot.Players.Select(Copy).ToList(); elapsed = snapshot.QuestElapsed;
                return;
            }
            if (quest == 0) return;
            if (!ended && available && snapshot.QuestIdentity == quest)
            {
                if (players.Count == 0) players = snapshot.Players.Select(Copy).ToList();
                else foreach (var previous in players.Where(p => !p.IsNpc))
                {
                    var final = snapshot.Players.FirstOrDefault(p => Same(previous, p) && p.DamageAvailable);
                    // The final hit can arrive in the same poll as the clear flag.
                    // A cleared/reset buffer must never replace the last total.
                    if (final != null) previous.Damage = Math.Max(previous.Damage, final.Damage);
                }
                elapsed = Math.Max(elapsed, snapshot.QuestElapsed); ended = true;
            }
            snapshot.QuestIdentity = quest; snapshot.QuestElapsed = elapsed;
            snapshot.IsQuestDamageFrozen = true;
            snapshot.Players = players.Select(Copy).ToList();
        }
    }
}
