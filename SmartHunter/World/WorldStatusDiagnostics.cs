using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using SmartHunter.Core.Helpers;
using SmartHunter.Game.Data;

namespace SmartHunter.Game.Helpers
{
    // Read-only diagnostics. Production uses the original MhwHelper method;
    // this class does not discover, cache, filter or update overlay models.
    internal static class WorldStatusDiagnostics
    {
        internal static object ProbeCachedSources(Process process,Monster monster) => monster.StatusEffects.Select(s=>s.Address).Where(address=>address!=0).Distinct().Select(address=>new {
            Address="0x"+address.ToString("X"),ModelIndices=monster.StatusEffects.Where(s=>s.Address==address).Select(s=>s.Index).ToArray(),
            Index=MemoryHelper.Read<uint>(process,address+0x198),
            MaxBuildup=MemoryHelper.Read<float>(process,address+MhwHelper.DataOffsets.MonsterStatusEffect.MaxBuildup),
            Buildup=MemoryHelper.Read<float>(process,address+MhwHelper.DataOffsets.MonsterStatusEffect.CurrentBuildup),
            MaxDuration=MemoryHelper.Read<float>(process,address+MhwHelper.DataOffsets.MonsterStatusEffect.MaxDuration),
            Duration=MemoryHelper.Read<float>(process,address+MhwHelper.DataOffsets.MonsterStatusEffect.CurrentDuration),
            Count=MemoryHelper.Read<int>(process,address+MhwHelper.DataOffsets.MonsterStatusEffect.TimesActivatedCount)
        }).ToArray();
        internal static object Probe(Process process, Monster monster)
        {
            ulong root=MemoryHelper.Read<ulong>(process,monster.Address+0x78);
            ulong start=root==0?0:MemoryHelper.Read<ulong>(process,root+0x57A8),head=start;
            var seen=new HashSet<ulong>();bool headFound=false;
            while(head!=0 && seen.Add(head))
            {
                ulong previous=MemoryHelper.Read<ulong>(process,head+0x10);
                if(previous==0) {headFound=true;break;} head=previous;
            }
            int headSteps=seen.Count;
            ulong record=headFound?head+0x40:0;
            var records=new List<object>();seen.Clear();
            while(record!=0 && seen.Add(record))
            {
                if(MemoryHelper.Read<ulong>(process,record+0x188)==monster.Address)
                    records.Add(new {Address="0x"+record.ToString("X"),Index=MemoryHelper.Read<uint>(process,record+0x198),
                        MaxBuildup=MemoryHelper.Read<float>(process,record+MhwHelper.DataOffsets.MonsterStatusEffect.MaxBuildup),
                        Buildup=MemoryHelper.Read<float>(process,record+MhwHelper.DataOffsets.MonsterStatusEffect.CurrentBuildup),
                        MaxDuration=MemoryHelper.Read<float>(process,record+MhwHelper.DataOffsets.MonsterStatusEffect.MaxDuration),
                        Duration=MemoryHelper.Read<float>(process,record+MhwHelper.DataOffsets.MonsterStatusEffect.CurrentDuration),
                        Count=MemoryHelper.Read<int>(process,record+MhwHelper.DataOffsets.MonsterStatusEffect.TimesActivatedCount)});
                record=MemoryHelper.Read<ulong>(process,record+0x18);
            }
            return new {Root="0x"+root.ToString("X"),Start="0x"+start.ToString("X"),Head="0x"+head.ToString("X"),HeadFound=headFound,
                HeadSteps=headSteps,Traversed=seen.Count,TraversalComplete=headFound && record==0,StopReason=head==0?"NoHead":!headFound?"HeadCycle":record==0?"End":"Cycle",Records=records};
        }
    }
}
