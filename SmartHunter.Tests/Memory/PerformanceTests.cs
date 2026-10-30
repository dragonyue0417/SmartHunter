using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SmartHunter.Game.Config;
using SmartHunter.Game.Data.ViewModels;
using SmartHunter.Game.Helpers;
using SmartHunter.Ui.Windows;
using SmartHunter.Wilds;
using static TestSupport;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

static class ReadPerformanceTests
{
    sealed class Counter : IWildsMemory
    {
        public readonly FakeMemory Memory;public int Calls;public ulong FailArray;
        public Counter(FakeMemory memory){Memory=memory;}
        public bool Read(ulong address,byte[] buffer,int size)
        {
            Calls++;
            if(FailArray!=0 && (size==20 || size==12 || address==FailArray && size>8))return false;
            return Memory.Read(address,buffer,size);
        }
    }
internal static void PointerFreshness()
    {
        var memory=new FakeMemory();ulong address=memory.Alloc();memory.Ptr(address,123456);var counted=new Counter(memory);var view=new WildsMemoryView(counted);
        view.BeginSnapshot();Assert(view.Ptr(address)==123456 && view.Ptr(address)==123456 && counted.Calls==1 && view.PointerCacheHits==1,"same-frame pointers reused");
        memory.Ptr(address,654321);view.EndSnapshot();view.BeginSnapshot();Assert(view.Ptr(address)==654321,"next frame sees changed pointer");
        ulong missing=0x123456780;Assert(view.Ptr(missing)==0,"failed read");memory.Ptr(missing,777);Assert(view.Ptr(missing)==777,"failures cannot poison the frame cache");view.EndSnapshot();memory.Ptr(address,999);Assert(view.Ptr(address)==999,"outside a reader snapshot all reads remain live");
    }
    static FakeGame Population(int count)
    {
        var game=new FakeGame();
        var ids=Enumerable.Range(2,WildsCatalog.Instance.StatusEffects.Length-2).Where(i=>!WildsCatalog.Instance.StatusEffects[i].NameStringId.StartsWith("LOC_STATUS_EFFECT_UNKNOWN",StringComparison.Ordinal)).Take(8).ToArray();
        for(int i=0;i<count;i++)
        {
            ulong actor=game.Monster(16+i%4,900-i),data=game.M.Alloc();game.O(actor,"MonsterParts",data);
            var parts=new List<ulong>();for(int n=0;n<6;n++){ulong part=game.M.Alloc();game.M.Ptr(part+0x10,game.Encrypted(500));game.M.Ptr(part+0x28,game.Encrypted(320-n));parts.Add(part);}game.M.Ptr(data+0x78,game.M.Array(parts.ToArray()));
            var effects=new List<ulong>();foreach(int id in ids){ulong effect=game.M.Alloc(),progress=game.M.Alloc();game.M.Ptr(effect+0x20,progress);game.M.Float(effect+0x48,3);game.M.Float(effect+0x4C,20);game.M.Int(effect+0x58,1);game.M.Int(effect+0xB4,id);game.M.Float(progress+0x14,73);game.M.Float(progress+0x1C,200);effects.Add(effect);}game.O(actor,"MonsterStatusEffects",game.M.Array(effects.ToArray()));
        }
        game.Select(game.Monsters.Last());return game;
    }
    static void Same(WildsSnapshot a,WildsSnapshot b)=>Assert(JToken.DeepEquals(JObject.FromObject(a),JObject.FromObject(b)),"snapshot values differ from scalar baseline");
internal static void Parity()
    {
        var reports=new JArray();
        foreach(int count in new[]{1,5,12})
        {
            var game=Population(count);var baseline=new WildsReader(game.M,game.Addresses,game.Base,false);var optimized=new WildsReader(game.M,game.Addresses,game.Base);
            for(int i=0;i<3;i++){Same(baseline.Read(),optimized.Read());}
            Assert(optimized.LastReadStatistics.Calls<baseline.LastReadStatistics.Calls*.8,"at least 20 percent fewer memory calls for "+count);
            Assert(optimized.LastReadStatistics.PointerCacheHits>0,"pointer reuse was exercised");
            game.SetHealth(game.Monsters[0],432);Same(baseline.Read(),optimized.Read());Assert(optimized.Read().Monsters.Single(m=>m.Address==game.Monsters[0]).Health==432,"new frame updates health immediately");
            reports.Add(new JObject{["Monsters"]=count,["BaselineCalls"]=baseline.LastReadStatistics.Calls,["OptimizedCalls"]=optimized.LastReadStatistics.Calls,["BaselineBytes"]=baseline.LastReadStatistics.BytesRead,["OptimizedBytes"]=optimized.LastReadStatistics.BytesRead,["ReductionPercent"]=Math.Round((1-optimized.LastReadStatistics.Calls/(double)baseline.LastReadStatistics.Calls)*100,2)});
        }
        File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"read-performance.json"),reports.ToString());Console.WriteLine(reports.ToString(Formatting.None));
    }
internal static void Fallback()
    {
        var game=Population(5);var source=new Counter(game.M){FailArray=game.Elements+0x20};var baseline=new WildsReader(source,game.Addresses,game.Base,false);var optimized=new WildsReader(source,game.Addresses,game.Base);
        Same(baseline.Read(),optimized.Read());Assert(optimized.Read().Monsters.Count==5 && optimized.Read().Monsters.All(m=>m.Parts.Count==6 && m.Ailments.Count==8),"bulk failure retains all parts and statuses");
    }
internal static void CryptoFailure()
    {
        var game=new FakeGame();var memory=new WildsMemoryView(game.M);var crypto=new WildsCrypto(memory.Bytes(game.A("HealthEncryptionKey"),16),memory.Bytes(game.A("HealthEncryptionRound"),16));
        Assert(crypto.Read(memory,game.Encrypted(123))==123,"valid buffer");Assert(float.IsNaN(crypto.Read(memory,0x123456780)),"failed read never reuses a previous health block");Assert(crypto.Read(memory,game.Encrypted(456))==456,"reader recovers after failure");
    }
    static WildsNetworkReport Report(){return new WildsNetworkReport{GameVersion="1.42.0.2",Checks={new WildsNetworkCheck{StageId=1},new WildsNetworkCheck{StageId=2}}};}
internal static void Streaming()
    {
        string folder=Folder(),path=Path.Combine(folder,"Network.Wilds.json");var report=Report();WildsDiagnosticsFile.Write(path,report);SameJson(report,path);report.Checks[1].StageId=3;WildsDiagnosticsFile.Write(path,report);SameJson(report,path);Assert(Directory.GetFiles(folder).Length==1,"no .bak or unfinished temporary files");
    }
    static void SameJson(object value,string path)=>Assert(JToken.DeepEquals(JObject.FromObject(value),JObject.Parse(File.ReadAllText(path))),"streaming changes diagnostic contents");
internal static void FileBounds()
    {
        var report=Report();report.Checks[0].RelayState=new string('X',4096);string path=Path.Combine(Folder(),"Network.Wilds.json");WildsDiagnosticsFile.Write(path,report,2500);Assert(report.Checks.Count==1 && report.Checks[0].StageId==2,"oldest check is evicted first");SameJson(report,path);
    }
internal static void NativeSession()
    {
        var memory=new FakeMemory();ulong owner=memory.Alloc(),session=memory.Alloc(),info=memory.Alloc(),definition=memory.Alloc(),type=memory.Alloc(),name=memory.Alloc(),buffer=memory.Alloc();
        memory.Ptr(owner+0x60,session);memory.Ptr(session,info);memory.Ptr(info,definition);memory.Ptr(definition+0x40,info);memory.Ptr(definition+0x38,type);memory.Int(definition+0x14,96);memory.Int(type+0x30,96);memory.Ptr(type+0x20,name);memory.Put(name,System.Text.Encoding.ASCII.GetBytes("via.network.utility.SessionId\0"));
        byte[] secret=System.Text.Encoding.UTF8.GetBytes("test-session-only-never-export-raw");memory.Put(buffer,secret);memory.Ptr(session+0x30,buffer);memory.Int(session+0x38,secret.Length);
        Func<WildsNativeSessionCandidate> capture=()=>{var view=new WildsMemoryView(memory);return WildsNetworkProbe.ReadNativeSessionCandidate(view,new WildsNetworkMetadata(view,()=>false),owner,"test");};
        var first=capture();Assert(first.Status=="BufferFingerprintCandidate" && first.Sha256.Length==64 && !first.ValidatedForSharing,"candidate cannot authorize relay grouping");Assert(!JsonConvert.SerializeObject(first).Contains("never-export-raw"),"only fingerprint exported");
        memory.Byte(buffer,77);Assert(capture().Sha256!=first.Sha256,"candidate refreshed every capture");memory.Int(session+0x38,int.MaxValue);Assert(capture().Sha256==null,"unbounded lengths rejected");
    }
}

static class ScanHistoryTests
{
internal static void ScanHistory()
    {
        string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test-scan-history-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var report = new WildsScanReport { Status = "Resolved", GameVersion = "1.42.0.2", ElapsedMilliseconds = 1200 };
            report.ResolvedRvas["PlayerManager"] = "0x147EDF60";
            File.WriteAllText(path, JsonConvert.SerializeObject(report));
            var summary = WildsScanHistory.Read(path);
            Assert(summary.ResolvedRvas["PlayerManager"] == "0x147EDF60" && summary.ElapsedMilliseconds == 1200, "successful summary retained");
            var failed = new WildsScanReport { Status = "Failed", PreviousSuccessfulScan = summary };
            File.WriteAllText(path, JsonConvert.SerializeObject(failed));
            Assert(WildsScanHistory.Read(path).ResolvedRvas.Count == 1 && failed.ResolvedRvas.Count == 0, "failed scan does not populate live results from history");
            Assert(!JsonConvert.SerializeObject(summary).Contains("PreviousSuccessfulScan"), "history cannot grow recursively");
            File.WriteAllText(path, "invalid"); Assert(WildsScanHistory.Read(path) == null, "damaged history ignored");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
