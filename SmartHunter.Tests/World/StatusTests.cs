using DataOffsets = SmartHunter.Game.Helpers.MhwHelper.DataOffsets;
using Newtonsoft.Json.Linq;
using SmartHunter.Core;
using SmartHunter.Core.Helpers;
using SmartHunter.Game.Config;
using SmartHunter.Game.Data;
using SmartHunter.Game.Data.ViewModels;
using SmartHunter.Game.Helpers;
using SmartHunter.World;
using static TestSupport;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

// Status reference copied from the user's original source; never used in production.
static class OriginalWorldStatusReference
{
        public static void Update(Process process, Monster monster)
        {
            int maxIndex = ConfigHelper.MonsterData.Values.StatusEffects.Where(s => s.GroupId.Equals("StatusEffect")).Count() - 1;
            var statuses = monster.StatusEffects;
            if (statuses != null && statuses.Where(s => s.GroupId.Equals("StatusEffect")).Any())
            {
                for (int i = 0; i < statuses.Count(); i++)
                {
                    MonsterStatusEffect status = statuses[i];
                    if (status == null)
                    {
                        continue;
                    }
                    float currentBuildup = 0;
                    float maxBuildup = MemoryHelper.Read<float>(process, status.Address + DataOffsets.MonsterStatusEffect.MaxBuildup);
                    if (maxBuildup > 0)
                    {
                        currentBuildup = MemoryHelper.Read<float>(process, status.Address + DataOffsets.MonsterStatusEffect.CurrentBuildup);
                    }
                    float currentDuration = 0;
                    float maxDuration = MemoryHelper.Read<float>(process, status.Address + DataOffsets.MonsterStatusEffect.MaxDuration);
                    if (maxDuration > 0)
                    {
                        currentDuration = MemoryHelper.Read<float>(process, status.Address + DataOffsets.MonsterStatusEffect.CurrentDuration);
                    }
                    int timesActivatedCount = MemoryHelper.Read<int>(process, status.Address + DataOffsets.MonsterStatusEffect.TimesActivatedCount);

                    if (maxBuildup > 0 || maxDuration > 0)
                    {
                        uint index = MemoryHelper.Read<uint>(process, status.Address + 0x198);
                        if (index <= maxIndex)
                        {
                            var statusEffectConfig = ConfigHelper.MonsterData.Values.StatusEffects[index];
                            monster.UpdateAndGetStatusEffect(status.Address, (int)index, maxBuildup > 0 ? maxBuildup : 1, !statusEffectConfig.InvertBuildup ? currentBuildup : maxBuildup - currentBuildup, maxDuration, !statusEffectConfig.InvertDuration ? currentDuration : maxDuration - currentDuration, timesActivatedCount);
                        }
                    }
                }
            }
            else
            {
                ulong baseStatus = MemoryHelper.Read<ulong>(process, monster.Address + 0x78);
                baseStatus = MemoryHelper.Read<ulong>(process, baseStatus + 0x57A8);
                ulong nani = baseStatus;
                while (nani != 0)
                {
                    nani = MemoryHelper.Read<ulong>(process, nani + 0x10);
                    if (nani != 0)
                    {
                        baseStatus = nani;
                    }
                }
                ulong currentStatusPointer = baseStatus + 0x40;
                while (currentStatusPointer != 0x0)
                {
                    var currentMonsterInStatus = MemoryHelper.Read<ulong>(process, currentStatusPointer + 0x188);
                    if (currentMonsterInStatus == monster.Address && !monster.StatusEffects.Where(status => status.Address == currentStatusPointer).Any())
                    {
                        float currentBuildup = 0;
                        float maxBuildup = MemoryHelper.Read<float>(process, currentStatusPointer + DataOffsets.MonsterStatusEffect.MaxBuildup);
                        if (maxBuildup > 0)
                        {
                            currentBuildup = MemoryHelper.Read<float>(process, currentStatusPointer + DataOffsets.MonsterStatusEffect.CurrentBuildup);
                        }
                        float currentDuration = 0;
                        float maxDuration = MemoryHelper.Read<float>(process, currentStatusPointer + DataOffsets.MonsterStatusEffect.MaxDuration);
                        if (maxDuration > 0)
                        {
                            currentDuration = MemoryHelper.Read<float>(process, currentStatusPointer + DataOffsets.MonsterStatusEffect.CurrentDuration);
                        }
                        int timesActivatedCount = MemoryHelper.Read<int>(process, currentStatusPointer + DataOffsets.MonsterStatusEffect.TimesActivatedCount);

                        if (maxBuildup > 0 || maxDuration > 0)
                        {
                            uint index = MemoryHelper.Read<uint>(process, currentStatusPointer + 0x198);
                            if (index <= maxIndex && !((index == 14 || index == 15) && monster.isElder) && index != 0) // skip traps for elders
                            {
                                var statusEffectConfig = ConfigHelper.MonsterData.Values.StatusEffects[index];
                                monster.UpdateAndGetStatusEffect(currentStatusPointer, (int)index, maxBuildup > 0 ? maxBuildup : 1, !statusEffectConfig.InvertBuildup ? currentBuildup : maxBuildup - currentBuildup, maxDuration, !statusEffectConfig.InvertDuration ? currentDuration : maxDuration - currentDuration, timesActivatedCount);
                            }
                        }
                    }
                    currentStatusPointer = MemoryHelper.Read<ulong>(process, currentStatusPointer + 0x18);
                }
            }

            // Stamina

            ulong staminaAddress = monster.Address + DataOffsets.Monster.MonsterStaminaOffset;
            float maxStaminaBuildUp = MemoryHelper.Read<float>(process, staminaAddress + 0x4);
            float currentStaminaBuildUp = 0;
            if (maxStaminaBuildUp > 0)
            {
                currentStaminaBuildUp = MemoryHelper.Read<float>(process, staminaAddress);
            }
            float maxFatigueDuration = MemoryHelper.Read<float>(process, staminaAddress + 0x0C);
            float currentFatigueDuration = 0;
            if (maxFatigueDuration > 0)
            {
                currentFatigueDuration = MemoryHelper.Read<float>(process, staminaAddress + 0x10);
            }
            int fatigueActivatedCount = MemoryHelper.Read<int>(process, staminaAddress + 0x14);
            MonsterStatusEffectConfig statusEffect = null;
            if (currentFatigueDuration >= 0)
            {
                statusEffect = ConfigHelper.MonsterData.Values.StatusEffects.SingleOrDefault(s => s.GroupId.Equals("Fatigue"));
                monster.UpdateAndGetStatusEffect(staminaAddress, Array.IndexOf(ConfigHelper.MonsterData.Values.StatusEffects, statusEffect), 1, 0, maxFatigueDuration, currentStaminaBuildUp == 0 ? maxFatigueDuration - currentFatigueDuration : 0, fatigueActivatedCount);
            }
            if (currentStaminaBuildUp >= 0)
            {
                statusEffect = ConfigHelper.MonsterData.Values.StatusEffects.SingleOrDefault(s => s.GroupId.Equals("Stamina"));
                monster.UpdateAndGetStatusEffect(staminaAddress, Array.IndexOf(ConfigHelper.MonsterData.Values.StatusEffects, statusEffect), maxStaminaBuildUp, currentStaminaBuildUp, 1, 0, 0);
            }
            /*
            if (currentFatigueDuration > 0)
            {
                statusEffect = ConfigHelper.MonsterData.Values.StatusEffects.SingleOrDefault(s => s.GroupId.Equals("Fatigue"));
            }
            else
            {
                statusEffect = ConfigHelper.MonsterData.Values.StatusEffects.SingleOrDefault(s => s.GroupId.Equals("Stamina"));
            }

            if (maxStaminaBuildUp > 0 || currentFatigueDuration > 0)
            {
                monster.UpdateAndGetStatusEffect(staminaAddress, Array.IndexOf(ConfigHelper.MonsterData.Values.StatusEffects, statusEffect), maxStaminaBuildUp > 0 ? maxStaminaBuildUp : 1, !statusEffect.InvertBuildup ? currentStaminaBuildUp : maxStaminaBuildUp - currentStaminaBuildUp, maxFatigueDuration, !statusEffect.InvertDuration ? currentFatigueDuration : maxFatigueDuration - currentFatigueDuration, fatigueActivatedCount);
            }
            */
            // Rage

            ulong rageAddress = monster.Address + DataOffsets.Monster.MonsterRageOffset;
            float maxRageBuildUp = MemoryHelper.Read<float>(process, rageAddress + 0x18);
            float currentRageBuildUp = 0;
            if (maxRageBuildUp > 0)
            {
                currentRageBuildUp = MemoryHelper.Read<float>(process, rageAddress - 0x0C);
            }
            float maxRageDuration = MemoryHelper.Read<float>(process, rageAddress + 0x04);
            float currentRageDuration = 0;
            if (maxRageDuration > 0)
            {
                currentRageDuration = MemoryHelper.Read<float>(process, rageAddress + 0x00);
            }
            int rageActivatedCount = MemoryHelper.Read<int>(process, rageAddress + 0x10);
            var rageStatusEffect = ConfigHelper.MonsterData.Values.StatusEffects.SingleOrDefault(s => s.GroupId.Equals("Rage"));//[33]; // 33 is rage
            if (maxRageBuildUp > 0 || maxRageDuration > 0)
            {
                monster.UpdateAndGetStatusEffect(rageAddress, Array.IndexOf(ConfigHelper.MonsterData.Values.StatusEffects, rageStatusEffect), maxRageBuildUp > 0 ? maxRageBuildUp : 1, !rageStatusEffect.InvertBuildup ? currentRageBuildUp : maxRageBuildUp - currentRageBuildUp, maxRageDuration, !rageStatusEffect.InvertDuration ? currentRageDuration : maxRageDuration - currentRageDuration, rageActivatedCount);
            }
        }
}

static class WorldStatusTests
{
    public static void Register(Action<string,Action> test)
    {
        var active=GameProfile.Active;var main=ConfigHelper.Main.Values;var original=main.Overlays.World;
        string language=main.WorldLocalizationFileName;var memory=WorldConfigHelper.Memory.Values;
        bool auto=memory.AutoResolvePlayerNameCollection;long offset=memory.PlayerNameCollectionOffset;
        try {
            main.Overlays.World=GameOverlayConfig.WorldDefaults();main.WorldLocalizationFileName="zh-TW.World.json";
            GameProfile.Active=GameChoice.World;ConfigHelper.SwitchGameData(GameChoice.World);
            main.Overlays.World.MonsterWidget.UseNetworkServer=false;
            memory.AutoResolvePlayerNameCollection=true;memory.PlayerNameCollectionOffset=0;
            test("World parity: all 36 original status definitions retain their native indices and inversion rules", Catalog);
            test("World parity: original English monster, part and status translations are complete", English);
            test("World parity: original reader reaches elderseal and drooling after more than 1024 foreign records in either direction", LongStatusList);
            test("World parity: native memory supplies all ordinary status IDs including elderseal and claw flinch", NativeStatuses);
            test("World parity: original discovery retries when every general status is dormant", LateStatuses);
            test("World parity: dormant duplicate records cannot hide any original native status", DuplicateStatuses);
            test("World parity: unused negative timers and signed progress follow the uploaded original", OriginalCounters);
            test("World parity: statuses remain readable without editable part metadata", NoParts);
            test("World parity: upstream cached special addresses supply drooling 191/352 and elderseal", SpecialAddresses);
            test("World parity: the original cached-address path discovers blast, claw attack, elderseal and drooling after ID or owner changes", CachedStatusIds);
            test("World parity: World model updates retain the first address exactly as the original source", Relocation);
            test("World parity: foreign owners, ID zero and elder traps remain excluded", Filters);
            test("World parity: player, session and lobby fields use complete 64-bit pointers", Names64);
            test("World parity: the community minus-0x30 layout is selected only with native validation", ShiftedNames);
            test("World parity: missing session IDs do not hide independent lobby fields or invent a host", PartialNames);
            test("World parity: unknown layouts keep explicit settings and permit a manual override", UncertainNames);
            test("World parity: local diagnostics retain raw field results and the legacy room-key comparison", Diagnostics);
            test("World parity: another client's cloud sample replays every provided status into Unified models", CloudReplay);
            test("World parity: elderseal, drooling, rage, stamina and fatigue render with SmartHunter colors", RenderedStatuses);
        } finally {
            OverlayViewModel.Instance.MonsterWidget.Context.Monsters.Clear();OverlayViewModel.Instance.DebugWidget.Context.ResetWorldSession();
            main.Overlays.World=original;main.WorldLocalizationFileName=language;
            memory.AutoResolvePlayerNameCollection=auto;memory.PlayerNameCollectionOffset=offset;
            GameProfile.Active=active;ConfigHelper.SwitchGameData(active);
        }
    }
    static readonly Process Self=Process.GetCurrentProcess();
    static JObject Fixture(string name)
    {
        var assembly=typeof(WorldStatusTests).Assembly;
        using(var stream=assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(n=>n.EndsWith(name))))
        using(var reader=new StreamReader(stream)) return JObject.Parse(reader.ReadToEnd());
    }
    static void Catalog()
    {
        var expected=(JArray)Fixture("WorldOriginal.json")["StatusEffects"];
        var actual=JArray.FromObject(ConfigHelper.MonsterData.Values.StatusEffects);
        Assert(JToken.DeepEquals(actual,expected),"native index definitions differ from original commit 99d3a55");
        Assert((string)actual[18]["NameStringId"]=="LOC_STATUS_EFFECT_ELDERSEAL" && (string)actual[23]["NameStringId"]=="LOC_STATUS_EFFECT_VIOLATED","legacy wire IDs preserved");
    }
    static void English()
    {
        var original=(JObject)Fixture("WorldOriginal.json")["English"];
        var english=LanguageCatalog.ReadBuiltIn("en-US.World.json");
        foreach(var pair in original.Properties()) Assert(english.ContainsKey(TextKeyNames.Canonical(pair.Name)),"missing original English key "+pair.Name);
        foreach(var definition in ConfigHelper.MonsterData.Values.StatusEffects) Assert(english.ContainsKey(definition.NameStringId),"untranslated native status "+definition.NameStringId);
        Assert(LanguageCatalog.ReadBuiltIn("zh-TW.World.json")["LOC_STATUS_EFFECT_VIOLATED"].Contains("流口水"),"drooling label is identifiable");
    }
    sealed class Arena : IDisposable
    {
        readonly List<IntPtr> allocations=new List<IntPtr>();
        public ulong Alloc(int length) {var p=Marshal.AllocHGlobal(length);allocations.Add(p);Marshal.Copy(new byte[length],0,p,length);return unchecked((ulong)p.ToInt64());}
        public void Ptr(ulong address,ulong value)=>Marshal.WriteInt64((IntPtr)address,unchecked((long)value));
        public void Int(ulong address,int value)=>Marshal.WriteInt32((IntPtr)address,value);
        public void Float(ulong address,float value) {byte[] bytes=BitConverter.GetBytes(value);Marshal.Copy(bytes,0,(IntPtr)address,4);}
        public void Text(ulong address,string value) {byte[] bytes=Encoding.UTF8.GetBytes(value+"\0");Marshal.Copy(bytes,0,(IntPtr)address,bytes.Length);}
        public void Dispose() {foreach(var p in allocations) Marshal.FreeHGlobal(p);}
    }
    sealed class MonsterMemory : IDisposable
    {
        public readonly Arena A=new Arena();public readonly Monster Model;
        public readonly ulong Root,Head;ulong previous;
        public MonsterMemory(bool elder=false) {
            ulong entity=A.Alloc(0x1D400);Root=A.Alloc(0x6000);Head=A.Alloc(0x300);
            A.Ptr(entity+0x78,Root);A.Ptr(Root+0x57A8,Head);
            string id=elder?ConfigHelper.MonsterData.Values.Monsters.First(p=>p.Value.isElder).Key:"em001_00";
            Model=new Monster(entity,id,100,80,1,1){IsSelected=true};
        }
        public ulong Add(int id,float maximum=100,float buildup=20) {
            ulong record=(previous==0?Head:A.Alloc(0x300))+0x40;
            if(previous!=0) A.Ptr(previous+0x18,record);
            A.Ptr(record+0x188,Model.Address);A.Int(record+0x198,id);Values(record,maximum,buildup);previous=record;return record;
        }
        public void Values(ulong record,float maximum,float buildup) {
            A.Float(record+0x1C8,maximum);A.Float(record+0x1B8,buildup);A.Float(record+0x19C,maximum>0?10:0);A.Float(record+0x1F8,maximum>0?4:0);A.Int(record+0x200,maximum>0?1:0);
        }
        public void Read(long now=0)=>ReadOriginalPipeline(Model);
        public void Dispose()=>A.Dispose();
    }
    static void ReadOriginalPipeline(Monster monster)
    {
        typeof(MhwHelper).GetMethods(BindingFlags.NonPublic|BindingFlags.Static)
            .Single(m=>m.Name=="UpdateMonsterStatusEffects" && m.GetParameters()[0].ParameterType==typeof(Process))
            .Invoke(null,new object[]{Self,monster});
    }
    static void LongStatusList()
    {
        foreach(bool startAtTail in new[]{false,true}) using(var game=new MonsterMemory(true)) {
            const int foreignCount=4096;
            ulong chain=game.A.Alloc((foreignCount+3)*0x300);
            ulong prior=0;
            for(int i=0;i<foreignCount+2;i++) {
                ulong node=chain+(ulong)i*0x300,record=node+0x40;
                if(prior!=0) {game.A.Ptr(prior+0x18,record);game.A.Ptr(node+0x10,prior-0x40);}
                game.A.Ptr(record+0x188,i<foreignCount?game.Model.Address+1:game.Model.Address);
                game.A.Int(record+0x198,i==foreignCount?18:23);
                game.Values(record,i==foreignCount?280:352,i==foreignCount?13:51);
                prior=record;
            }
            game.A.Ptr(game.Root+0x57A8,startAtTail?prior-0x40:chain);
            var expected=new Monster(game.Model.Address,game.Model.Id,100,80,1,1);
            OriginalWorldStatusReference.Update(Self,expected);
            ReadOriginalPipeline(game.Model);
            foreach(int id in new[]{18,23}) {
                var old=expected.StatusEffects.Single(s=>s.Index==id);
                var actual=game.Model.StatusEffects.SingleOrDefault(s=>s.Index==id);
                Assert(actual!=null && actual.Buildup.Current==old.Buildup.Current && actual.Buildup.Max==old.Buildup.Max,"production reader stops before original state "+id+"; backward start="+startAtTail);
            }
            var probe=JObject.FromObject(WorldStatusDiagnostics.Probe(Self,game.Model));
            Assert((bool)probe["TraversalComplete"] && (int)probe["Traversed"]==foreignCount+2 && probe["Records"].Any(r=>(int)r["Index"]==18) && probe["Records"].Any(r=>(int)r["Index"]==23),"diagnostics cannot truncate the same long list either");
        }
    }
    static void NativeStatuses()
    {
        using(var game=new MonsterMemory()) {
            for(int i=1;i<=32;i++) game.Add(i);
            game.Read();Assert(game.Model.StatusEffects.Where(s=>s.GroupId=="StatusEffect").Select(s=>s.Index).OrderBy(i=>i).SequenceEqual(Enumerable.Range(1,32)),"Unified lost native status IDs");
            Assert(game.Model.StatusEffects.Single(s=>s.Index==18).Buildup.Current==20 && game.Model.StatusEffects.Single(s=>s.Index==23).Duration.Current==6,"elderseal and claw flinch values");
            Assert(game.Model.StatusEffects.Single(s=>s.Index==22).Buildup.Current==80,"original claw buildup inversion preserved");
            ulong stamina=game.Model.Address+MhwHelper.DataOffsets.Monster.MonsterStaminaOffset,rage=game.Model.Address+MhwHelper.DataOffsets.Monster.MonsterRageOffset;
            game.A.Float(stamina,50);game.A.Float(stamina+4,150);game.A.Float(stamina+12,60);game.A.Float(stamina+16,10);game.A.Int(stamina+20,2);
            game.A.Float(rage+24,200);game.A.Float(rage-12,40);game.A.Float(rage+4,90);game.A.Float(rage,30);game.A.Int(rage+16,3);
            var read=typeof(MhwHelper).GetMethods(BindingFlags.NonPublic|BindingFlags.Static).Single(m=>m.Name=="UpdateMonsterStatusEffects" && m.GetParameters()[0].ParameterType==typeof(Process));
            read.Invoke(null,new object[]{Self,game.Model});
            Assert(game.Model.StatusEffects.Count==35 && game.Model.StatusEffects.Single(s=>s.Index==34).Buildup.Current==50 && game.Model.StatusEffects.Single(s=>s.Index==33).TimesActivatedCount==3,"complete original native pipeline includes independent stamina and rage");
            game.A.Float(stamina,0);read.Invoke(null,new object[]{Self,game.Model});
            Assert(game.Model.StatusEffects.Single(s=>s.Index==35).Duration.Current==50 && game.Model.StatusEffects.Single(s=>s.Index==18).Buildup.Current==20,"fatigue updates without overwriting elderseal");
        }
    }
    static void LateStatuses()
    {
        using(var game=new MonsterMemory()) {
            ulong elder=game.Add(18,0,0),drool=game.Add(23,0,0);game.Read();
            Assert(!game.Model.StatusEffects.Any(s=>s.GroupId=="StatusEffect"),"dormant general records do not create overlay rows");
            game.Values(elder,250,60);game.Values(drool,352,51);game.Read();
            Assert(game.Model.StatusEffects.Any(s=>s.Index==18 && s.Buildup.Current==60) && game.Model.StatusEffects.Any(s=>s.Index==23 && s.Buildup.Current==51),"original retries discovery until it obtains general statuses");
        }
    }
    static void SpecialAddresses()
    {
        using(var game=new MonsterMemory()) {
            game.Add(1);game.Read();
            ulong stamina=game.Model.Address+MhwHelper.DataOffsets.Monster.MonsterStaminaOffset;
            ulong rage=game.Model.Address+MhwHelper.DataOffsets.Monster.MonsterRageOffset;
            game.A.Int(stamina+0x198,23);game.Values(stamina,352,192);game.A.Int(stamina+0x200,2);
            game.A.Int(rage+0x198,18);game.Values(rage,500,333);
            game.Model.UpdateAndGetStatusEffect(stamina,35,1,0,20,10,1);
            game.Model.UpdateAndGetStatusEffect(stamina,34,800,300,1,0,0);
            game.Model.UpdateAndGetStatusEffect(rage,33,100,35,1,0,1);game.Read(1);
            game.A.Float(stamina+0x1B8,191);game.Read(2);
            var drooling=game.Model.StatusEffects.Single(s=>s.Index==23);
            Assert(drooling.Buildup.Current==191 && drooling.Buildup.Max==352 && drooling.TimesActivatedCount==2 && drooling.IsVisible,"reproduce the user's original screenshot through the upstream cached-address branch, including changed-only visibility");
            Assert(game.Model.StatusEffects.Single(s=>s.Index==18).Buildup.Current==333,"ordinary cached-source states and independent fatigue remain present");
            var sources=JArray.FromObject(WorldStatusDiagnostics.ProbeCachedSources(Self,game.Model));
            Assert(sources.Count==3 && sources.Any(s=>(int)s["Index"]==23) && sources.Any(s=>(int)s["Index"]==18),"diagnostics preserve every original cached source without duplicate reads");
        }
    }
    static void CachedStatusIds()
    {
        using(var game=new MonsterMemory()) {
            ulong address=game.Add(1);game.Read();game.A.Ptr(address+0x188,0);
            foreach(int index in new[]{4,22,18,23}) {
                game.A.Int(address+0x198,index);game.Values(address,index==23?352:100,index==23?192:30);game.Read(index);
            }
            game.A.Float(address+0x1B8,191);game.A.Int(address+0x200,2);game.Read(1001);
            Assert(game.Model.StatusEffects.Any(s=>s.Index==4) && game.Model.StatusEffects.Single(s=>s.Index==22).Buildup.Current==70 && game.Model.StatusEffects.Any(s=>s.Index==18),"all requested status IDs follow the source's cached-address branch rather than discovery-only owner/index filters");
            var drooling=game.Model.StatusEffects.Single(s=>s.Index==23);
            Assert(drooling.Buildup.Current==191 && drooling.Buildup.Max==352 && drooling.TimesActivatedCount==2 && drooling.IsVisible,"drooling remains available after periodic discovery no longer owns the original cached record");
        }
    }
    static void DuplicateStatuses()
    {
        using(var game=new MonsterMemory()) {
            var addresses=new List<ulong>();
            for(int i=1;i<=32;i++) {addresses.Add(game.Add(i));addresses.Add(game.Add(i,0,0));}
            game.Read();
            Assert(game.Model.StatusEffects.Count(s=>s.GroupId=="StatusEffect")==32,"keep each native address and select usable records for all original IDs");
            Assert(game.Model.StatusEffects.Single(s=>s.Index==18).Buildup.Max==100 && game.Model.StatusEffects.Single(s=>s.Index==23).Duration.Max==10,"dormant duplicates cannot replace elderseal or claw flinch");
            var probe=JObject.FromObject(WorldStatusDiagnostics.Probe(Self,game.Model));
            Assert(((JArray)probe["Records"]).Count==64 && probe["Records"].Count(s=>(int)s["Index"]==18)==2,"raw diagnostics retain both active and dormant records");
            foreach(var address in addresses) {game.A.Ptr(address+0x188,0);if(Marshal.ReadInt32((IntPtr)(address+0x1C8))!=0) game.A.Float(address+0x1B8,25);}
            game.Read(1);Assert(game.Model.StatusEffects.Count(s=>s.GroupId=="StatusEffect")==32 && game.Model.StatusEffects.Single(s=>s.Index==23).Buildup.Current==25,"the upstream cached path keeps reading trusted addresses when the discovery-only owner field changes");
        }
    }
    static void OriginalCounters()
    {
        using(var game=new MonsterMemory()) {
            ulong elder=game.Add(18),claw=game.Add(23),attack=game.Add(22);
            game.A.Float(elder+0x19C,-1);game.A.Float(elder+0x1B8,-5);
            game.A.Float(claw+0x1B8,125);game.A.Float(claw+0x1F8,11);game.A.Float(attack+0x1B8,150);
            game.Read();game.A.Float(elder+0x1B8,-4);game.A.Float(claw+0x1F8,12);game.Read(1);
            Assert(game.Model.StatusEffects.Single(s=>s.Index==18).Buildup.Current==0 && game.Model.StatusEffects.Single(s=>s.Index==18).Duration.Max==-1,"an unused negative duration must not suppress the valid buildup");
            Assert(game.Model.StatusEffects.Single(s=>s.Index==23).Buildup.Current==100 && game.Model.StatusEffects.Single(s=>s.Index==23).Duration.Current==0 && game.Model.StatusEffects.Single(s=>s.Index==22).Buildup.Current==0,"World applies the uploaded original's inversion and Progress.Cap after accepting native records");
        }
    }
    static void NoParts()
    {
        using(var game=new MonsterMemory()) {
            var definitions=ConfigHelper.MonsterData.Values.Monsters[game.Model.Id];var parts=definitions.Parts;
            var context=OverlayViewModel.Instance.MonsterWidget.Context;
            try {
                definitions.Parts=new SmartHunter.Game.Config.MonsterPartConfig[0];
                ulong health=game.A.Alloc(128),name=game.A.Alloc(128);
                game.A.Ptr(game.Model.Address+MhwHelper.DataOffsets.Monster.MonsterHealthComponentOffset,health);game.A.Float(health+0x60,100);game.A.Float(health+0x64,80);
                game.A.Ptr(game.Model.Address+0x2A0,name);game.A.Text(name+12,"em001_00");game.Add(18);game.Add(23);
                OverlayViewModel.Instance.DebugWidget.Context.ResetWorldSession();
                var read=typeof(MhwHelper).GetMethod("UpdateAndGetMonster",BindingFlags.NonPublic|BindingFlags.Static);
                var monster=(Monster)read.Invoke(null,new object[]{Self,game.Model.Address});
                Assert(monster!=null && monster.StatusEffects.Any(s=>s.Index==18) && monster.StatusEffects.Any(s=>s.Index==23),"native status decoding is independent of editable part definitions");
            } finally {definitions.Parts=parts;context.Monsters.Clear();}
        }
    }
    static void Relocation()
    {
        using(var game=new MonsterMemory()) {
            ulong first=game.Add(18);game.Read();
            ulong second=game.A.Alloc(0x300)+0x40;
            game.Model.UpdateAndGetStatusEffect(second,18,200,90,10,4,2);
            var row=game.Model.StatusEffects.Single(s=>s.Index==18);
            Assert(row.Address==first && row.Buildup.Current==90,"World retains its original cached address while updating values by index");
        }
    }
    static void Filters()
    {
        using(var game=new MonsterMemory(true)) {
            game.Add(0);game.Add(14);game.Add(15);ulong other=game.Add(7);game.A.Ptr(other+0x188,game.Model.Address+1);game.Add(18);game.Read();
            Assert(game.Model.StatusEffects.Count(s=>s.GroupId=="StatusEffect")==1 && game.Model.StatusEffects.Any(s=>s.Index==18),"owner and elder trap restrictions retained");
        }
    }
    sealed class NamesMemory : IDisposable
    {
        public readonly Arena A=new Arena();public readonly ulong Table,Slot,Player,Lobby,Weapon;
        public NamesMemory(long shift=0) {
            Table=A.Alloc(0x56000);Slot=A.Alloc(8);Player=A.Alloc(64);Lobby=A.Alloc(128);Weapon=A.Alloc(128);
            A.Ptr(Slot,unchecked((ulong)((long)Table+shift)));A.Text(Player,"Sapphire");A.Text(Weapon,"wp11");A.Int(Lobby+0x54,1);A.Int(Lobby+0x38,1);
            A.Text(Table+MhwHelper.DataOffsets.PlayerNameCollection.FirstPlayerName+66,"Sapphire");
            A.Text(Table+MhwHelper.DataOffsets.PlayerNameCollection.SessionID,"AB12CD34EF56");A.Text(Table+MhwHelper.DataOffsets.PlayerNameCollection.SessionHostPlayerName,"GatheringHost");
            A.Text(Table+MhwHelper.DataOffsets.PlayerNameCollection.LobbyID,"LM56NO78PQ90");A.Text(Table+MhwHelper.DataOffsets.PlayerNameCollection.LobbyHostPlayerName,"QuestHost");
        }
        public ulong Read() {ulong names=MhwHelper.ResolvePlayerNameCollection(Self,Slot,Player);MhwHelper.UpdateCurrentGame(Self,names,Player,Weapon,Lobby);OverlayViewModel.Instance.DebugWidget.Context.RefreshWorld();return names;}
        public void Dispose()=>A.Dispose();
    }
    static void CheckNames(NamesMemory game)
    {
        Assert(game.Table>UInt32.MaxValue,"fixture must reproduce a high native pointer");Assert(game.Read()==game.Table,"native base mismatch");
        var current=OverlayViewModel.Instance.DebugWidget.Context.CurrentGame;
        Assert(current.CurrentPlayerName=="Sapphire" && current.SessionID=="AB12CD34EF56" && current.SessionHostPlayerName=="GatheringHost" && current.LobbyID=="LM56NO78PQ90" && current.LobbyHostPlayerName=="QuestHost","all native fields read independently");
        Assert(!current.LobbyHostWasFallback && current.IsPlayerOnline(),"online source is not a local fallback");
    }
    static void Names64() {using(var game=new NamesMemory()) {CheckNames(game);Assert(WorldPlayerNames.LastResolution.Offset==0,"corrected 0x532D5 layout must not be shifted again");}}
    static void ShiftedNames() {using(var game=new NamesMemory(0x30)) {CheckNames(game);Assert(WorldPlayerNames.LastResolution.Offset==-0x30 && WorldPlayerNames.LastResolution.Reason=="PlayerName","minus-0x30 requires independent player anchor");}}
    static void PartialNames()
    {
        using(var game=new NamesMemory()) {
            game.A.Text(game.Table+MhwHelper.DataOffsets.PlayerNameCollection.SessionID,"");game.Read();var context=OverlayViewModel.Instance.DebugWidget.Context;var current=context.CurrentGame;
            Assert(current.SessionID=="" && current.SessionHostPlayerName=="GatheringHost" && current.LobbyID=="LM56NO78PQ90" && current.LobbyHostPlayerName=="QuestHost" && !current.LobbyHostWasFallback,"one missing ID must not suppress the other three fields");
            game.A.Text(game.Table+MhwHelper.DataOffsets.PlayerNameCollection.LobbyID,"");
            for(int i=0;i<4;i++) game.A.Text(game.Table+MhwHelper.DataOffsets.PlayerNameCollection.LobbyHostPlayerName+(ulong)(i*0x2F),"");
            game.Read();Assert(current.LobbyHostWasFallback && current.LobbyHostPlayerName=="Sapphire" && context.MemberLine==UiText.Instance["LOC_UI_DEBUG_ROLE_UNKNOWN"] && context.WorldDetails.Any(s=>s.Contains(UiText.Instance["LOC_UI_WORLD_HOST_UNREAD"])) ,"legacy offline fallback is never presented as a verified host");
        }
    }
    static void UncertainNames()
    {
        using(var game=new NamesMemory(0x30)) {
            var memory=WorldConfigHelper.Memory.Values;game.A.Text(game.Player,"");game.A.Text(game.Table+MhwHelper.DataOffsets.PlayerNameCollection.SessionID,"");
            try {
                Assert(MhwHelper.ResolvePlayerNameCollection(Self,game.Slot)==game.Table+0x30 && WorldPlayerNames.LastResolution.Reason=="NoAnchor","do not guess a layout without evidence");
                memory.AutoResolvePlayerNameCollection=false;memory.PlayerNameCollectionOffset=-48;
                Assert(MhwHelper.ResolvePlayerNameCollection(Self,game.Slot)==game.Table,"explicit manual offset is supported");
            }finally {memory.AutoResolvePlayerNameCollection=true;memory.PlayerNameCollectionOffset=0;}
        }
    }
    static void Diagnostics()
    {
        using(var game=new NamesMemory(0x30)) {
            game.Read();var report=JObject.FromObject(WorldReadDiagnostics.Capture(Self,game.Slot,game.Table,game.Player,game.Lobby));
            Assert((bool)report["PointerWasAbove32Bits"] && (long)report["PlayerNamesResolution"]["Offset"]==-48 && (bool)report["Fields"]["SessionId"]["ReadSucceeded"] && (string)report["Fields"]["SessionId"]["Text"]=="AB12CD34EF56","diagnostics retain real pointer and raw field evidence");
            using(var sha=System.Security.Cryptography.SHA1.Create()) Assert((string)report["LegacySync"]["ComputedRoomKey"]==Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes("AB12CD34EF56LM56NO78PQ90"))),"diagnostic room key follows the unchanged World legacy protocol");
            Assert(FileContainer.GetFullPathFileName("Read.World.json").Contains(Path.Combine("Diagnostics","World")),"local diagnostic is classified separately");
            var context=OverlayViewModel.Instance.MonsterWidget.Context;
            using(var monster=new MonsterMemory(true)) {
                try {
                    monster.Add(23,352,191);monster.Read();context.Monsters.Add(monster.Model);
                    WorldReadDiagnostics.WriteSnapshot(WorldReadDiagnostics.Capture(Self,game.Slot,game.Table,game.Player,game.Lobby),true);
                    context.Monsters.Clear();
                    WorldReadDiagnostics.WriteSnapshot(WorldReadDiagnostics.Capture(Self,game.Slot,game.Table,game.Player,game.Lobby),false);
                    var saved=JObject.Parse(File.ReadAllText(FileContainer.GetFullPathFileName("Read.World.Monsters.json")));
                    var latest=JObject.Parse(File.ReadAllText(FileContainer.GetFullPathFileName("Read.World.json")));
                    Assert(saved["Monsters"].Count()==1 && saved["Monsters"][0]["Statuses"].Any(s=>(int)s["Index"]==23) && latest["Monsters"].Count()==0,"leaving a quest cannot overwrite the last captured monster diagnostics with an empty list");
                } finally {context.Monsters.Clear();}
            }
        }
    }
    static void CloudReplay()
    {
        var source=(JObject)Fixture("WorldCloudSample.json")["Monsters"];
        var apply=typeof(MhwHelper).GetMethods(BindingFlags.NonPublic|BindingFlags.Static).Single(m=>m.Name=="UpdateMonsterStatusEffects" && m.GetParameters()[0].ParameterType==typeof(Dictionary<string,int[]>));
        foreach(var pair in source.Properties()) {
            var model=new Monster(0x123456,pair.Name,100,90,1,1);var statuses=pair.Value["statuses"].ToObject<Dictionary<string,int[]>>();apply.Invoke(null,new object[]{statuses,model});
            Assert(model.StatusEffects.Count==statuses.Count && model.StatusEffects.Any(s=>s.Index==23) && model.StatusEffects.Any(s=>s.Index==35),"Unified must retain all statuses supplied by another client's sample");
        }
    }
    static void RenderedStatuses()
    {
        if(!Application.Current.Resources.Contains("A_Color_Monster_StatusEffect_Bar")) Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("/SmartHunter;component/Ui/Resources/Default.xaml",UriKind.Relative)});
        var vm=OverlayViewModel.Instance;var config=ConfigHelper.Main.Values.Overlay.MonsterWidget;config.ShowUnchangedStatusEffects=true;config.ShowStatusEffects=true;
        bool active=vm.IsGameActive,preview=vm.IsPreview;vm.IsGameActive=true;vm.IsPreview=true;
        try {
        using(var memory=new MonsterMemory(true)) {
            memory.Add(18);memory.Add(23);memory.Read();var model=memory.Model;
            foreach(int index in new[]{33,34,35}) model.UpdateAndGetStatusEffect(model.Address,index,100,40,20,10,1);
            vm.MonsterWidget.Context.Monsters.Clear();vm.MonsterWidget.Context.Monsters.Add(model);vm.MonsterWidget.Context.UpdateFromConfig();
            var window=new SmartHunter.Ui.Windows.MonsterWidgetWindow();var host=(FrameworkElement)window.Content;
            try {
            window.Opacity=0;window.Show();
            TestSupport.Render(host,"world-status-parity.png",800,550);
            Assert(Visuals<StackPanel>(host).Where(p=>p.Name=="Root" && p.DataContext is Monster).All(p=>p.ActualWidth==300),"World health and detail columns retain the legacy 300 width");
            var texts=Visuals<TextBlock>(host).Where(t=>t.Visibility==Visibility.Visible).Select(t=>t.Text).ToArray();
            foreach(var key in new[]{"LOC_STATUS_EFFECT_ELDERSEAL","LOC_STATUS_EFFECT_VIOLATED","LOC_STATUS_EFFECT_RAGE","LOC_STATUS_EFFECT_STAMINA","LOC_STATUS_EFFECT_FATIGUE"}) Assert(texts.Contains(LocalizationHelper.GetString(key)),"missing rendered World status: "+key);
            Assert((Color)Application.Current.Resources["A_Color_Monster_StatusEffect_Bar"]==Color.FromRgb(0,204,204),"SmartHunter cyan status palette unchanged");
            } finally {window.Close();}
            vm.MonsterWidget.Context.Monsters.Clear();
        }
        } finally {vm.IsGameActive=active;vm.IsPreview=preview;}
    }
}
