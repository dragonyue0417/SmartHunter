using Newtonsoft.Json.Linq;
using SmartHunter.Core;
using SmartHunter.Core.Updates;
using SmartHunter.Game.Config;
using SmartHunter.Game.Data.ViewModels;
using SmartHunter.Game.Helpers;
using SmartHunter.Ui.Windows;
using SmartHunter.World;
using static TestSupport;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

static class UpdateTests
{
    static readonly Version Current=typeof(UpdateFiles).Assembly.GetName().Version;
    static Version Next(int increment = 1) => new Version(Current.Major, Current.Minor, Current.Build, Current.Revision + increment);
    const string Repository="dragonyue0417/SmartHunter";
    static string Source(string branch="unified")=>"https://raw.githubusercontent.com/"+Repository+"/"+branch+"/release/";
    static void Reject(Action action,string reason)
    {
        try{action();}catch(Exception error) when(error is InvalidDataException || error is IOException || error is UnauthorizedAccessException || error is Newtonsoft.Json.JsonException || error is HttpRequestException || error is OperationCanceledException){return;}
        throw new Exception("Accepted invalid update: "+reason);
    }
    const string Revision="aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    static string Payload()
    {
        string folder=Folder();var hashes=new JObject();var sizes=new JObject();
        foreach(string name in UpdatePackage.PayloadFiles)
        {
            string path=Path.Combine(folder,name);Directory.CreateDirectory(Path.GetDirectoryName(path));File.Copy(Path.Combine(Root,name),path);
            hashes[name]=UpdateFiles.Hash(path);sizes[name]=new FileInfo(path).Length;
        }
        File.WriteAllText(Path.Combine(folder,UpdatePackage.ManifestName),new JObject{["Product"]="SmartHunter-Unified",["Version"]=Current.ToString(),["Files"]=hashes,["Sizes"]=sizes}.ToString());return folder;
    }
    static UpdatePackage Package(string folder)
    {
        var result=UpdatePackage.Parse(File.ReadAllText(Path.Combine(folder,UpdatePackage.ManifestName)));
        result.Repository=Repository;result.Branch="master";result.Revision=Revision;return result;
    }
    static string Reference(string branch="master",string revision=Revision)=>new JObject{["ref"]="refs/heads/"+branch,["object"]=new JObject{["type"]="commit",["sha"]=revision}}.ToString();
    static HttpResponseMessage Raw(string folder,HttpRequestMessage request)
    {
        Assert(request.Method==HttpMethod.Get && request.RequestUri.Host=="raw.githubusercontent.com","only official raw file GET requests");
        string prefix="/"+Repository+"/"+Revision+"/release/";Assert(request.RequestUri.AbsolutePath.StartsWith(prefix,StringComparison.Ordinal),"all files use the pinned commit and release directory");
        string name=request.RequestUri.AbsolutePath.Substring(prefix.Length);Assert(UpdatePackage.PayloadFiles.Contains(name)||name==UpdatePackage.ManifestName,"only declared files requested");
        return new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(File.ReadAllBytes(Path.Combine(folder,name)))};
    }
internal static void Catalog()
    {
        string payload=Payload();var package=Package(payload);
        Assert(package.Version==Current && package.Files.Count==7 && package.Sizes.Count==7 && package.Size==UpdatePackage.PayloadFiles.Sum(n=>new FileInfo(Path.Combine(payload,n)).Length),"complete uncompressed manifest");
        Assert(package.FileUri("Themes/Default.xaml").AbsoluteUri=="https://raw.githubusercontent.com/"+Repository+"/"+Revision+"/release/Themes/Default.xaml","fixed raw download origin and commit");
        foreach(string bad in new[]{"","https://github.com/user/repo","user/../repo","user/repo?x=1","user/repo/extra"})Assert(!UpdatePackage.ValidRepository(bad),"invalid repository: "+bad);
        foreach(string good in new[]{"master","main","unified","release/test-1.2"})Assert(UpdatePackage.ValidBranch(good),"valid branch: "+good);
        foreach(string bad in new[]{"","/main","main/","a..b","a//b","a.lock","a/../b","main?x=1","a@{1}","a\\b"})Assert(!UpdatePackage.ValidBranch(bad),"invalid branch: "+bad);
        foreach(string url in new[]{Source(),Source().TrimEnd('/'),"https://github.com/"+Repository+"/tree/unified/release"})
            Assert(UpdateSource.Parse(url).Url==Source(),"GitHub URL forms normalize: "+url);
        Assert(UpdateSource.Parse(Source("test/unified")).Branch=="test/unified","slash branch preserved in one URL");
        foreach(string invalid in new[]{"", "dragonyue0417/SmartHunter", "http://raw.githubusercontent.com/"+Repository+"/unified/release/", "https://github.com/"+Repository+"/tree/unified", "https://raw.githubusercontent.com/"+Repository+"/x/../unified/release/",Source()+"?x=1",Source()+"#fragment","https://user@raw.githubusercontent.com/"+Repository+"/unified/release/",Source().Replace("release","%72elease")})
            Reject(()=>UpdateSource.Parse(invalid),"invalid update URL: "+invalid);
        var original=JObject.Parse(package.ManifestJson);
        foreach(Action<JObject> change in new Action<JObject>[] {
            x=>x["Product"]="Other",x=>x["Version"]="1.2.3",x=>x["Files"]["SmartHunter.exe"]="sha256:bad",x=>x["Files"]["../evil.exe"]=new string('a',64),
            x=>((JObject)x["Files"]).Remove("OxyPlot.dll"),x=>((JObject)x["Files"]).Add("smarthunter.exe",new string('a',64)),
            x=>x["Sizes"]["SmartHunter.exe"]=0,x=>x["Sizes"]["SmartHunter.exe"]=-1,x=>x["Sizes"]["SmartHunter.exe"]=33554433,
            x=>x["Sizes"]["SmartHunter.exe"]="12",x=>x.Remove("Sizes"),x=>x["Sizes"]["other.exe"]=1,
            x=>{foreach(var p in ((JObject)x["Sizes"]).Properties())p.Value=33554432;}})
        {var value=(JObject)original.DeepClone();change(value);Reject(()=>UpdatePackage.Parse(value.ToString()),"manifest declaration");}
        Reject(()=>UpdatePackage.Parse("{\"Product\":\"SmartHunter-Unified\",\"Product\":\"Other\"}"),"duplicate JSON keys");
        package.Revision="master/../other";Reject(()=>package.FileUri("SmartHunter.exe"),"invalid revision");
    }
internal static void ManifestFiles()
    {
        string payload=Payload();Assert(UpdateFiles.Validate(payload)==Current,"complete payload accepted");
        File.AppendAllText(Path.Combine(payload,"Themes/Default.xaml"),"tampered");Reject(()=>UpdateFiles.Validate(payload),"modified file");
        string missing=Payload();File.Delete(Path.Combine(missing,"OxyPlot.dll"));Reject(()=>UpdateFiles.Validate(missing),"missing DLL");
        string bare=Folder();File.WriteAllText(Path.Combine(bare,"SmartHunter.exe"),"unverified executable");string target=Payload(),original=UpdateFiles.Hash(Path.Combine(target,"SmartHunter.exe"));
        Reject(()=>UpdateFiles.Install(bare,target),"a standalone EXE without a manifest cannot be installed");Assert(UpdateFiles.Hash(Path.Combine(target,"SmartHunter.exe"))==original,"rejected downloads leave the current EXE intact");
        string other=Payload(),path=Path.Combine(other,UpdatePackage.ManifestName);var manifest=JObject.Parse(File.ReadAllText(path));manifest["Version"]=Next().ToString();File.WriteAllText(path,manifest.ToString());Reject(()=>UpdateFiles.Validate(other),"EXE version");
        string legacy=Payload();path=Path.Combine(legacy,UpdatePackage.ManifestName);manifest=JObject.Parse(File.ReadAllText(path));manifest.Remove("Sizes");File.WriteAllText(path,manifest.ToString());
        Assert(UpdateFiles.Validate(legacy)==Current,"earlier local manifests remain installable by the standalone legacy bridge");Reject(()=>UpdatePackage.Parse(manifest.ToString()),"network updates require size bounds");
    }
internal static void Install()
    {
        string stage=Payload(),target=Folder();File.WriteAllText(Path.Combine(target,"SmartHunter.exe"),"original program");
        foreach(string name in new[]{"Config/World/Settings.json","Config/Wilds/Settings.json","Languages/World/custom.json","Themes/Custom.xaml","personal.txt"})
        {string path=Path.Combine(target,name);Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,"keep "+name);}
        UpdateFiles.Install(stage,target);Assert(UpdateFiles.Validate(target)==Current,"complete in-place install");
        Assert(!File.Exists(Path.Combine(target,"README.md")) && !Directory.Exists(Path.Combine(target,"docs")),"runtime installation contains no repository documentation");
        Assert(File.ReadAllText(Path.Combine(target,"Config/World/Settings.json"))=="keep Config/World/Settings.json" && File.ReadAllText(Path.Combine(target,"Themes/Custom.xaml"))=="keep Themes/Custom.xaml","user content survives installation");
        Assert(!Directory.Exists(Path.Combine(target,"Backups")) && !Directory.Exists(Path.Combine(target,"Imports")),"no user-visible archive folders");
        string rollback=Payload();foreach(string name in UpdatePackage.PayloadFiles)File.AppendAllText(Path.Combine(rollback,name),"old");
        var before=UpdatePackage.PayloadFiles.ToDictionary(x=>x,x=>UpdateFiles.Hash(Path.Combine(rollback,x)));
        using(var locked=new FileStream(Path.Combine(rollback,"OxyPlot.Wpf.dll"),FileMode.Open,FileAccess.Read,FileShare.Read))Reject(()=>UpdateFiles.Install(stage,rollback),"locked destination");
        foreach(var pair in before)Assert(UpdateFiles.Hash(Path.Combine(rollback,pair.Key))==pair.Value,"rollback restores "+pair.Key);
        Reject(()=>UpdateFiles.Install(stage,stage),"stage cannot be installation root");
    }
    sealed class Handler:HttpMessageHandler
    {
        public Func<HttpRequestMessage,HttpResponseMessage> Respond;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation)
        {cancellation.ThrowIfCancellationRequested();return Task.FromResult(Respond(request));}
    }
internal static void Http()
    {
        string payload=Payload();int references=0,files=0,manifests=0;
        using(var client=new GitHubUpdateClient(new Handler{Respond=r=>
        {
            Assert(r.Headers.UserAgent.ToString().Contains("SmartHunter-Unified/"),"application User-Agent");
            if(r.RequestUri.Host=="api.github.com")
            {references++;Assert(r.RequestUri.AbsolutePath=="/repos/"+Repository+"/git/ref/heads/master","branch lookup replaces the Releases API");return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(Reference())};}
            if(r.RequestUri.AbsolutePath.EndsWith(UpdatePackage.ManifestName))manifests++;else files++;
            return Raw(payload,r);
        }}))
        {
            var package=client.Check(Source("master"),new Version(0,0,0,0),CancellationToken.None).GetAwaiter().GetResult();
            Assert(package.Version==Current && package.Revision==Revision,"newer version and commit resolved");
            string stage=client.Download(package,CancellationToken.None).GetAwaiter().GetResult();Assert(UpdateFiles.Validate(stage)==Current && references==1 && manifests==1 && files==7,"complete raw file download verifies all hashes and the EXE version");
            string destination=Folder();File.WriteAllText(Path.Combine(destination,"SmartHunter.exe"),"old program");Directory.CreateDirectory(Path.Combine(destination,"Config/World"));File.WriteAllText(Path.Combine(destination,"Config/World/Settings.json"),"keep");
            UpdateFiles.Install(stage,destination);Assert(UpdateFiles.Validate(destination)==Current && File.ReadAllText(Path.Combine(destination,"Config/World/Settings.json"))=="keep","actual downloaded files install in place with settings intact");
            Assert(client.Check(Source("master"),Current,CancellationToken.None).GetAwaiter().GetResult()==null && client.Check(Source("master"),Next(),CancellationToken.None).GetAwaiter().GetResult()==null && files==7,"same or older versions never download payload files");
        }
    }
internal static void HttpFailures()
    {
        string folder=Payload();var package=Package(folder);
        foreach(HttpStatusCode status in new[]{HttpStatusCode.ServiceUnavailable,HttpStatusCode.Forbidden,HttpStatusCode.NotFound,HttpStatusCode.Found})
            using(var client=new GitHubUpdateClient(new Handler{Respond=r=>new HttpResponseMessage(status)}))Reject(()=>client.Check(Source("master"),Current,CancellationToken.None).GetAwaiter().GetResult(),"metadata HTTP status: "+status);
        foreach(string reference in new[]{Reference("other"),Reference("master","bad"),Reference().Replace("commit","tree"),new string('x',65537)})
            using(var client=new GitHubUpdateClient(new Handler{Respond=r=>new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(reference)}}))Reject(()=>client.Check(Source("master"),Current,CancellationToken.None).GetAwaiter().GetResult(),"invalid or oversized metadata");
        bool missing=false;
        using(var client=new GitHubUpdateClient(new Handler{Respond=r=>r.RequestUri.Host=="api.github.com"?new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(Reference())}:new HttpResponseMessage(HttpStatusCode.NotFound)}))
        try{client.Check(Source("master"),Current,CancellationToken.None).GetAwaiter().GetResult();}catch(UpdateSourceMissingException){missing=true;}
        Assert(missing,"an unpublished release folder is reported as missing rather than up to date");
        foreach(string mode in new[]{"truncated","too large","tampered","redirect"})
        using(var client=new GitHubUpdateClient(new Handler{Respond=r=>
        {
            if(mode=="redirect"){var moved=new HttpResponseMessage(HttpStatusCode.Found);moved.Headers.Location=new Uri("https://evil.invalid/file");return moved;}
            var response=Raw(folder,r);var bytes=response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            if(mode=="truncated")Array.Resize(ref bytes,bytes.Length-1);else if(mode=="too large")Array.Resize(ref bytes,bytes.Length+1);else bytes[0]^=1;
            response.Content=new ByteArrayContent(bytes);response.Content.Headers.ContentLength=package.Sizes["SmartHunter.exe"];return response;
        }}))Reject(()=>client.Download(package,CancellationToken.None).GetAwaiter().GetResult(),"raw file "+mode);
        using(var token=new CancellationTokenSource())using(var client=new GitHubUpdateClient(new Handler{Respond=r=>throw new Exception("No request after cancellation")}))
        {token.Cancel();Reject(()=>client.Check(Source("master"),Current,token.Token).GetAwaiter().GetResult(),"cancelled metadata request");Reject(()=>client.Download(package,token.Token).GetAwaiter().GetResult(),"cancelled payload request");}
    }
internal static void ReuseFiles()
    {
        string payload=Payload(),installed=Payload();var package=Package(payload);int requests=0;
        using(var client=new GitHubUpdateClient(new Handler{Respond=r=>{requests++;return Raw(payload,r);}}))
        {
            string stage=client.Download(package,CancellationToken.None,installed).GetAwaiter().GetResult();Assert(requests==0 && UpdateFiles.Validate(stage)==Current,"identical installed bytes are reused only after hashing");
            File.AppendAllText(Path.Combine(installed,"Themes/Default.xaml"),"local change");stage=client.Download(package,CancellationToken.None,installed).GetAwaiter().GetResult();
            Assert(requests==1 && UpdateFiles.Validate(stage)==Current && File.ReadAllText(Path.Combine(installed,"Themes/Default.xaml")).EndsWith("local change"),"only changed files download; the running installation stays untouched until apply");
        }
    }
internal static void Pinning()
    {
        string payload=Payload();int refs=0;
        using(var client=new GitHubUpdateClient(new Handler{Respond=r=>
        {
            if(r.RequestUri.Host=="api.github.com")
            {refs++;Assert(r.RequestUri.AbsolutePath.EndsWith("/heads/test/unified"),"slash-separated branch lookup is encoded safely");return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(Reference("test/unified",refs==1?Revision:new string('b',40)))};}
            return Raw(payload,r);
        }}))
        {
            var package=client.Check(Source("test/unified"),new Version(0,0,0,0),CancellationToken.None).GetAwaiter().GetResult();
            Assert(UpdateFiles.Validate(client.Download(package,CancellationToken.None).GetAwaiter().GetResult())==Current && refs==1,"a subsequent branch push cannot change the download snapshot");
        }
    }
internal static void Defaults()
    {
        string root=Folder();var values=new MainConfig();var doc=GameSettingsStore.Document(values,GameChoice.World);doc.Remove("ReleaseDefaultsVersion");
        foreach(var property in ((JObject)doc["Debug"]).Properties())property.Value=true;
        doc["Overlay"]["DebugWidget"]["IsVisible"]=true;doc["DamageSharing"]=new JObject{["Enabled"]=true};doc["Keybinds"]["CopyTeamDamage"]="F5";doc["CustomFutureSetting"]=27;
        string path=GameSettingsStore.PathFor(root,GameChoice.World);Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,doc.ToString());DateTime modified=DateTime.UtcNow.AddDays(-1);File.SetLastWriteTimeUtc(path,modified);
        var migrated=GameSettingsStore.Read(root);var saved=JObject.Parse(File.ReadAllText(path));
        Assert(((JObject)saved["Debug"]).Properties().All(p=>p.Value.Type==JTokenType.Boolean && !(bool)p.Value) && !(bool)saved["Overlay"]["DebugWidget"]["IsVisible"],"all diagnostics off on release migration");
        Assert(saved["DamageSharing"]==null && saved["Keybinds"]["CopyTeamDamage"]==null && (int)saved["CustomFutureSetting"]==27 && File.GetLastWriteTimeUtc(path)==modified,"retired settings removed without losing unknown fields or profile selection time");
        migrated.WorldOptions.Debug.CollectNetworkDiagnostics=true;GameSettingsStore.Write(root,migrated,GameChoice.World);
        Assert(GameSettingsStore.Read(root).WorldOptions.Debug.CollectNetworkDiagnostics,"explicit diagnostic re-enable survives subsequent startup");
        foreach(GameChoice game in new[]{GameChoice.World,GameChoice.Wilds})
        {
            var fresh=GameSettingsStore.Document(new MainConfig(),game);Assert(((JObject)fresh["Debug"]).Properties().All(p=>!(bool)p.Value) && !(bool)fresh["Overlay"]["DebugWidget"]["IsVisible"] && (bool)fresh["Updates"]["Enabled"],"new install release defaults: "+game);
            foreach(string code in new[]{"en-US","zh-TW","zh-CN"})
            {
                var strings=LanguageCatalog.ReadBuiltIn(code+"."+game+".json");Assert(!strings.Keys.Any(k=>k.StartsWith("LOC_UI_CHAT_") || k.StartsWith("LOC_UI_DAMAGE_") || k=="LOC_UI_SHARING"),"no retired controls in bundled "+game+code);
                foreach(string key in new[]{"LOC_UI_WEB_SHARE_START_COPY","LOC_UI_UPDATE_ENABLED","LOC_UI_UPDATE_RESTART","LOC_UI_UPDATE_URL_INVALID"})Assert(strings.ContainsKey(key),"translated release control "+key+" / "+game+code);
            }
        }
    }
internal static void SourceSettings()
    {
        var original=ConfigHelper.Main.Values;var game=GameProfile.Active;
        try
        {
            var values=new MainConfig();ConfigHelper.Main.Values=values;GameProfile.Active=GameChoice.Wilds;
            Assert(values.WorldOptions.Updates.Url==UpdateSource.DefaultUrl && values.WildsOptions.Updates.Url==Source(),"both new profiles default to the unified URL");
            var view=new SettingsViewModel();var url=view.Settings.Single(s=>s.Key=="LOC_UI_UPDATE_URL");
            Assert(url.IsWideText && !url.IsToggle && !view.Settings.Any(s=>s.Key=="LOC_UI_UPDATE_BRANCH" || s.Key=="LOC_UI_UPDATE_REPOSITORY"),"one full-width URL replaces repository and branch controls");
            values.WildsOptions.Updates.PendingDirectory="old-stage";values.WildsOptions.Updates.PendingSource="old-source";
            url.Text="https://github.com/dragonyue0417/SmartHunter/tree/master/release";
            Assert(values.WildsOptions.Updates.Url==Source("master") && values.WorldOptions.Updates.Url==Source() && values.WildsOptions.Updates.PendingDirectory=="" && values.WildsOptions.Updates.PendingSource=="","source edits clear stale downloads and leave the other game intact");
            url.Text="https://evil.invalid/file";Assert(values.WildsOptions.Updates.Url==Source("master") && url.ValidationMessage.Length>0,"invalid URL does not change the source");
            string root=Folder();GameSettingsStore.Write(root,values);var saved=GameSettingsStore.Read(root);
            Assert(saved.WildsOptions.Updates.Url==Source("master") && saved.WorldOptions.Updates.Url==Source(),"URLs persist independently for both profiles");
            var document=GameSettingsStore.Document(values,GameChoice.World);document["Updates"]=new JObject{["Enabled"]=false,["Repository"]=Repository,["Branch"]="master",["PendingDirectory"]="old-stage",["PendingSource"]="old-source"};
            document["CustomFutureSetting"]=42;
            string path=GameSettingsStore.PathFor(root,GameChoice.World);File.WriteAllText(path,document.ToString());var date=DateTime.UtcNow.AddDays(-2);File.SetLastWriteTimeUtc(path,date);
            saved=GameSettingsStore.Read(root);var upgraded=JObject.Parse(File.ReadAllText(path));
            Assert(saved.WorldOptions.Updates.Url==Source() && !saved.WorldOptions.Updates.Enabled && saved.WorldOptions.Updates.PendingDirectory=="" && upgraded["Updates"]["Repository"]==null && upgraded["Updates"]["Branch"]==null && (int)upgraded["CustomFutureSetting"]==42 && File.GetLastWriteTimeUtc(path)==date,"old stock master source migrates to unified once, removes retired fields and preserves settings and timestamp");
            document["Updates"]["Repository"]="other/project";document["Updates"]["Branch"]="custom/branch";GameSettingsStore.UpgradeDocument(document);
            Assert((string)document["Updates"]["Url"]=="https://raw.githubusercontent.com/other/project/custom/branch/release/","custom legacy source is preserved");
        }
        finally{ConfigHelper.Main.Values=original;GameProfile.Active=game;}
    }
internal static void Legacy()
    {
        string root=Folder();
        File.WriteAllText(Path.Combine(root,"Config.json"),"{\"Overlay\":{\"MonsterWidget\":{\"X\":517.25,\"Y\":247.5,\"Scale\":1.25,\"UseNetworkServer\":true}},\"ServerUrl\":\"http://world.invalid/index.php\",\"LocalizationFileName\":\"zh-TW.json\",\"AutomaticallyCheckAndDownloadUpdates\":false,\"InterfaceFontSize\":18,\"Keybinds\":{\"HideWidgets\":\"F2\",\"CopyTeamDamage\":\"F5\"}}");
        File.WriteAllText(Path.Combine(root,"zh-TW.json"),"{\"Author\":\"Test owner\",\"Strings\":{\"LOC_UI_GAME_WORLD\":\"自訂世界\"}}");
        File.WriteAllText(Path.Combine(root,"Config.Wilds.json"),"{\"Overlay\":{\"MonsterWidget\":{\"X\":22,\"CardWidth\":240}},\"Server\":{\"ServerUrl\":\"http://wilds.invalid\",\"Enabled\":true},\"LocalizationFileName\":\"zh-TW.Wilds.json\"}");
        File.WriteAllText(Path.Combine(root,"zh-TW.Wilds.json"),"{\"Author\":\"Test owner\",\"Strings\":{\"LOC_UI_GAME_WILDS\":\"自訂荒野\"}}");
        foreach(string name in new[]{"Memory","MonsterData","PlayerData"})File.WriteAllText(Path.Combine(root,name+".json"),"{}");
        StorageLayout.Prepare(root);var values=GameSettingsStore.Read(root);
        Assert(values.Overlays.World.MonsterWidget.X==517.25 && values.Overlays.World.MonsterWidget.CardWidth==300 && values.Overlays.World.MonsterWidget.Scale==1.25 && values.Overlays.Wilds.MonsterWidget.X==22 && values.Overlays.Wilds.MonsterWidget.CardWidth==240,"separate original overlay positions, width and scale");
        Assert(values.Servers.World.Enabled && values.Servers.World.ServerUrl=="http://world.invalid/index.php" && values.Servers.Wilds.Enabled && values.Servers.Wilds.ServerUrl=="http://wilds.invalid","both server settings converted");
        Assert(!values.WorldOptions.Updates.Enabled && values.WildsOptions.Updates.Enabled && values.WorldOptions.InterfaceFontSize==18 && values.WorldOptions.Keybinds.Count==2,"legacy update preference and supported keys retained");
        Assert((string)JObject.Parse(File.ReadAllText(Path.Combine(root,"Languages/World/zh-TW.json")))["Strings"]["LOC_UI_GAME_WORLD"]=="自訂世界" && (string)JObject.Parse(File.ReadAllText(Path.Combine(root,"Languages/Wilds/zh-TW.json")))["Strings"]["LOC_UI_GAME_WILDS"]=="自訂荒野","custom translations converted separately");
        foreach(string name in new[]{"Memory","MonsterData","PlayerData"})Assert(File.Exists(Path.Combine(root,"Config/World/"+name+".json")) && !File.Exists(Path.Combine(root,name+".json")),"original data moved after conversion: "+name);
        Assert(!File.Exists(Path.Combine(root,"Config.json")) && !File.Exists(Path.Combine(root,"Config.Wilds.json")) && StorageLayout.Prepare(root)==0 && !Directory.Exists(Path.Combine(root,"Imports")),"migration retires old aliases once without Imports");
    }
internal static void Preflight()
    {
        string root=Folder(),source=Path.Combine(root,"Config.json");const string original="{\"Overlay\":{\"MonsterWidget\":{\"X\":42}}}";File.WriteAllText(source,original);File.WriteAllText(Path.Combine(root,"Memory.json"),"{invalid");
        Reject(()=>StorageLayout.Prepare(root),"invalid original data");Assert(File.ReadAllText(source)==original && !Directory.Exists(Path.Combine(root,"Config")),"preflight retains originals with no partial target");
        string current=Folder();GameSettingsStore.Write(current,new MainConfig());string target=GameSettingsStore.PathFor(current,GameChoice.World),before=File.ReadAllText(target);File.WriteAllText(Path.Combine(current,"Config.json"),original);
        StorageLayout.Prepare(current);Assert(File.ReadAllText(target)==before && !File.Exists(Path.Combine(current,"Config.json")) && File.ReadAllText(Path.Combine(StorageLayout.LastArchiveDirectory,"Config.json"))==original,"current classified settings win and the old configuration remains in the archive");
    }
internal static void Ui()
    {
        var old=ConfigHelper.Main.Values;var game=GameProfile.Active;
        try
        {
            ConfigHelper.Main.Values=new MainConfig();GameProfile.Active=GameChoice.Wilds;
            var view=new SettingsViewModel();Assert(view.SelectedSection.Id=="Logs" && view.IsLogs && !view.Sections.Any(s=>s.Id=="Sharing"),"startup Logs and removed damage feature");
            if(!Application.Current.Resources.Contains("A_Color_Monster_StatusEffect_Bar"))Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("/SmartHunter;component/Ui/Resources/Default.xaml",UriKind.Relative)});
            foreach(string language in new[]{"en-US.Wilds.json","zh-TW.Wilds.json","zh-CN.Wilds.json"})
            {
                ConfigHelper.Main.Values.WildsLocalizationFileName=language;ConfigHelper.Localization.TryChangeFileName(language);view.RefreshGameProfile();
                var window=new ConsoleWindow{DataContext=view,Width=1120,Height=780};
                TestSupport.Render((FrameworkElement)window.Content,"updates-logs-"+language+".png",1100,740);
                Assert(((Button)window.FindName("ShareWebButton")).ActualHeight>0 && ((Button)window.FindName("UpdateButton")).ActualHeight>0,"prominent release actions visible "+language);
                ConfigHelper.Main.Values.WildsOptions.InterfaceFontSize=20;view.RefreshGameProfile();TestSupport.Render((FrameworkElement)window.Content,"updates-logs-large-"+language+".png",830,580);
                Assert(view.CanShareWeb,"Wilds watch available");GameProfile.Active=GameChoice.World;Assert(!view.CanShareWeb,"World watch disabled");GameProfile.Active=GameChoice.Wilds;
                view.SelectedSection=view.Sections.Single(s=>s.Id=="General");
                ConfigHelper.Main.Values.WildsOptions.InterfaceFontSize=14;view.RefreshGameProfile();
                TestSupport.Render((FrameworkElement)window.Content,"updates-general-"+language+".png",1100,740);
                view.SelectedSection=view.Sections.Single(s=>s.Id=="Logs");
                window.Close();
            }
        }
        finally{ConfigHelper.Main.Values=old;GameProfile.Active=game;ConfigHelper.Localization.TryChangeFileName(old.LocalizationFileName);}
    }
}
