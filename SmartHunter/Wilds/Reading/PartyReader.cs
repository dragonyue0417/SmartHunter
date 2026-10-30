using System;
using System.Collections.Generic;
using System.Linq;
namespace SmartHunter.Wilds
{
    internal sealed class WildsPartyReader
    {
        readonly WildsReadContext game;
        readonly WildsQuestDamage quest;
        sealed class HitCheckpoint { public int Count; public ulong Tail; public float TailDamage; public double Total; }
        readonly Dictionary<ulong, HitCheckpoint> checkpoints = new Dictionary<ulong, HitCheckpoint>();
        public WildsPartyReader(WildsReadContext game, WildsQuestDamage quest) { this.game = game; this.quest = quest; }
        public void ClearHistory() => checkpoints.Clear();
        public void Capture(WildsSnapshot result, ulong local, bool questCounters, HashSet<int> targets)
        {
            var memory = game.Memory;
            var occupied = new HashSet<int>();
            ulong roster = game.Root("PlayerManager", "PlayerParty"), array = roster == 0 ? 0 : memory.Ptr(roster + 16);
            int count = roster == 0 ? 0 : memory.Int(roster + 24);
            ulong questSlots = game.Root("PlayerManager", "PlayerQuestPlayerIndexes"), players = game.Slot("PlayerManager", "PlayerList");
            if (array != 0 && memory.Int(array + 28) == 4 && count >= 0 && count <= 4 && players != 0)
                for (int entry = 0; entry < Math.Max(1, count); entry++)
                {
                    int playerIndex = memory.Int(array + 32 + (ulong)entry * 4);
                    if (playerIndex < 0 || playerIndex > 15)
                        continue;
                    ulong handle = memory.Ptr(players + (ulong)playerIndex * 8);
                    if (handle == 0 || memory.Int(handle + 0x4C) != 6)
                        continue;
                    ulong player = memory.Ptr(handle + 16), state = game.Follow(player, "PlayerContext");
                    string name = memory.String(memory.Ptr(state + 0x38));
                    ulong network = memory.Ptr(state + 0x40);
                    int questIndex = network == 0 ? -1 : memory.Int(network + 0x28);
                    if (name.Length == 0 || questIndex < 0 || questIndex > 3 || questSlots == 0 || memory.Int(questSlots + 28) != 4)
                        continue;
                    int slot = memory.Int(questSlots + 0x30 + (ulong)questIndex * 24);
                    if (slot < 0 || slot > 3 || !occupied.Add(slot))
                        continue;
                    int damage = Damage(player, local, slot, questCounters, targets, out bool available);
                    result.Players.Add(new WildsPlayerSnapshot { Address = player, Name = name, Slot = slot, Damage = damage, DamageAvailable = available });
                }
            if (result.Players.Count == 0)
            {
                occupied.Add(0);
                int damage = Damage(local, local, 0, questCounters, targets, out bool available);
                result.Players.Add(new WildsPlayerSnapshot { Address = local, Name = result.PlayerName, Slot = 0, Damage = damage, DamageAvailable = available });
            }
            if (!game.Has("NpcManager") || !game.HasPath("NpcParty"))
                return;
            ulong npcs = memory.Ptr(game.Slot("NpcManager", "NpcParty"));
            foreach (ulong npc in memory.Array(npcs, 16))
            {
                if (result.Players.Count == 4)
                    break;
                if (memory.Int(npc + 0x44) != 2 || memory.Int(npc + 0x38) == 3)
                    continue;
                ulong identity = memory.Ptr(npc + 16), state = memory.Ptr(npc + 0x28);
                if (identity == 0 || state == 0 || memory.Int(identity + 0x28) < 0 || memory.Byte(identity + 0x24) == 255)
                    continue;
                int slot = Enumerable.Range(0, 4).First(index => !occupied.Contains(index));
                occupied.Add(slot);
                result.Players.Add(new WildsPlayerSnapshot
                {
                    Address = state,
                    Name = "LOC_SUPPORT_HUNTER_" + memory.Int(identity + 0x28),
                    IsNpc = true,
                    Slot = slot,
                    Damage = Hits(game.Follow(state, "NpcDamageHistory"), targets),
                    DamageAvailable = true
                });
            }
        }
        int Damage(ulong player, ulong local, int slot, bool questCounters, HashSet<int> targets, out bool available)
        {
            if (!questCounters)
            { available = true; return Hits(game.Follow(player, "PlayerDamageHistory"), targets); }
            bool self = player == local;
            ulong address = game.Slot("QuestManager", self ? "QuestLocalPlayerDamage" : "QuestRemotePlayerDamage");
            float amount = address == 0 ? Single.NaN : game.Memory.Float(address + (self ? 0UL : (ulong)slot * 120));
            available = WildsReadContext.Number(amount) && amount >= 0 && (double)amount <= Int32.MaxValue;
            return available ? (int)amount : 0;
        }
        static int Counter(double amount) => amount < Int32.MaxValue ? (int)amount : Int32.MaxValue;
        int Hits(ulong history, HashSet<int> targets)
        {
            if (history == 0)
                return 0;
            var memory = game.Memory;
            int count = memory.Int(history + 24);
            ulong storage = memory.Ptr(history + 16);
            if (count < 0 || count > 100000 || storage == 0)
                return 0;
            int start = Math.Max(0, count - 100);
            ulong tail = count == 0 ? 0 : memory.Ptr(storage + 32 + (ulong)(count - 1) * 8);
            float tailDamage = tail == 0 ? 0 : memory.Float(tail + 16);
            HitCheckpoint checkpoint = null;
            if (!quest.UseQuestCounters)
            {
                if (!checkpoints.TryGetValue(history, out checkpoint))
                {
                    if (checkpoints.Count == 64)
                        checkpoints.Remove(checkpoints.Keys.First());
                    checkpoints.Add(history, new HitCheckpoint { Count = count, Tail = tail, TailDamage = tailDamage });
                    return 0;
                }
                if (checkpoint.Count == count && checkpoint.Tail == tail)
                {
                    if (tail != 0 && targets.Contains(memory.Int(tail + 24)) && WildsReadContext.Number(tailDamage) && WildsReadContext.Number(checkpoint.TailDamage))
                        checkpoint.Total += Math.Max(0, tailDamage - checkpoint.TailDamage);
                    checkpoint.TailDamage = tailDamage;
                    return Counter(checkpoint.Total);
                }
                if (count > checkpoint.Count)
                    start = Math.Max(start, checkpoint.Count);
                else if (checkpoint.Tail != 0)
                    for (int slot = count - 1; slot >= start; slot--)
                        if (memory.Ptr(storage + 32 + (ulong)slot * 8) == checkpoint.Tail)
                        { start = slot + 1; break; }
            }
            double total = checkpoint?.Total ?? 0;
            for (int slot = start; slot < count; slot++)
            {
                ulong hit = memory.Ptr(storage + 32 + (ulong)slot * 8);
                float amount = hit == 0 ? Single.NaN : memory.Float(hit + 16);
                if (hit != 0 && targets.Contains(memory.Int(hit + 24)) && WildsReadContext.Number(amount))
                    total += Math.Max(0, amount);
            }
            if (checkpoint != null)
            { checkpoint.Count = count; checkpoint.Tail = tail; checkpoint.TailDamage = tailDamage; checkpoint.Total = total; }
            return Counter(total);
        }
    }
}
