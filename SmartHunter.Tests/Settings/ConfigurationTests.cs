using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SmartHunter.Core;
using SmartHunter.Core.Config;
using SmartHunter.Game;
using SmartHunter.Game.Config;
using SmartHunter.Game.Data;
using SmartHunter.Game.Data.ViewModels;
using SmartHunter.Game.Data.WidgetContexts;
using SmartHunter.Game.Helpers;
using SmartHunter.Ui.Controls;
using SmartHunter.Ui.Windows;
using SmartHunter.Wilds;
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
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

static class GameProfileTests
{
internal static void Selection()
    {
        Assert(GameProfile.Select(GameChoice.Auto,GameChoice.Wilds,true,false)==GameChoice.World,"World detected");
        Assert(GameProfile.Select(GameChoice.Auto,GameChoice.World,false,true)==GameChoice.Wilds,"Wilds detected");
        Assert(GameProfile.Select(GameChoice.Auto,GameChoice.World,true,true)==GameChoice.World,"both keep active");
        Assert(GameProfile.Select(GameChoice.Wilds,GameChoice.World,true,false)==GameChoice.Wilds,"manual selection takes priority");
    }
internal static void Profiles()
    {
        var defaults=new MainConfig();
        Assert(defaults.Overlays.World.MonsterWidget.CardWidth==300 && defaults.Overlays.Wilds.MonsterWidget.CardWidth==220,"World restores legacy width independently of compact Wilds");
        var migrated=new MainConfig();
        JsonConvert.PopulateObject("{\"Overlays\":{\"World\":{\"MonsterWidget\":{\"CardWidth\":220,\"X\":517.25,\"Y\":247.5,\"Scale\":1.4,\"FontSize\":20}},\"Wilds\":{\"MonsterWidget\":{\"CardWidth\":180}}}}",migrated);
        var oldWorld=migrated.Overlays.World.MonsterWidget;
        Assert(oldWorld.CardWidth==300 && oldWorld.X==517.25 && oldWorld.Y==247.5 && oldWorld.Scale==1.4f && oldWorld.FontSize==20 && migrated.Overlays.Wilds.MonsterWidget.CardWidth==180,"old shared width upgrades without changing positions, scale, fonts or Wilds");
        oldWorld.CardWidth=220;
        Assert(JsonConvert.DeserializeObject<MainConfig>(JsonConvert.SerializeObject(migrated)).Overlays.World.MonsterWidget.CardWidth==220,"a later explicit choice of 220 survives reload");
        Assert(JsonConvert.DeserializeObject<MainConfig>("{\"Overlays\":{\"World\":{\"MonsterWidget\":{\"CardWidth\":260}}}}").Overlays.World.MonsterWidget.CardWidth==260,"custom previous widths survive upgrade");
        var original=GameProfile.Active; var main=ConfigHelper.Main.Values;
        var world=main.Overlays.World; var wilds=main.Overlays.Wilds;
        try
        {
            main.Overlays.World=new OverlayConfig(); main.Overlays.Wilds=new OverlayConfig();
            main.Overlays.World.MonsterWidget.X=41; main.Overlays.World.MonsterWidget.Y=42; main.Overlays.World.MonsterWidget.Scale=1.5f;
            main.Overlays.Wilds.MonsterWidget.X=801; main.Overlays.Wilds.MonsterWidget.Y=202; main.Overlays.Wilds.MonsterWidget.Scale=0.8f;
            GameProfile.Active=GameChoice.World; ConfigHelper.SwitchGameData(GameChoice.World);
            var widget=OverlayViewModel.Instance.MonsterWidget;
            Assert(widget.X==41 && widget.Y==42 && widget.Scale==1.5f,"World coordinates load together without clobbering Y or scale");
            widget.X=61;
            Assert(main.Overlays.World.MonsterWidget.X==61 && main.Overlays.Wilds.MonsterWidget.X==801,"moving World cannot alter Wilds");
            Assert(ConfigHelper.Localization.FileName.EndsWith(".World.json"),"World has a separate translation file");
            GameProfile.Active=GameChoice.Wilds; ConfigHelper.SwitchGameData(GameChoice.Wilds);
            Assert(widget.X==801 && widget.Y==202 && widget.Scale==0.8f && ConfigHelper.Localization.FileName.EndsWith(".Wilds.json"),"same widget loads Wilds layout and language");
            var document=JObject.Parse(JsonConvert.SerializeObject(main));
            Assert(document["Overlay"]==null && (int)document["Overlays"]["World"]["MonsterWidget"]["X"]==61 && (int)document["Overlays"]["Wilds"]["MonsterWidget"]["X"]==801,"JSON persists both named profiles");
            var data=JObject.FromObject(new MonsterDataConfig());
            Assert(data["Monsters"] is JObject && data["Monsters"].First.First["NameStringId"]!=null && data["Monsters"].First.First["BaseSize"]!=null,"editable monster schema uses SmartHunter dictionaries and names");
        }
        finally { main.Overlays.World=world; main.Overlays.Wilds=wilds; GameProfile.Active=original; ConfigHelper.SwitchGameData(original); }
    }
internal static void Import()
    {
        string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"import-"+Guid.NewGuid().ToString("N").Substring(0,8));
        string source=Path.Combine(root,"original"),target=Path.Combine(root,"unified"); Directory.CreateDirectory(source); Directory.CreateDirectory(target);
        string config=Path.Combine(source,"Config.json");
        var overlay=new OverlayConfig(); overlay.MonsterWidget.X=153; overlay.TeamWidget.Y=667; overlay.PlayerWidget.Scale=1.7f;
        string text=JsonConvert.SerializeObject(new { Overlay=overlay, LocalizationFileName="zh-TW.json" });
        string language="{\"Strings\":{\"LOC_PART_HEAD\":\"自訂頭部\"},\"Author\":\"User\"}";
        File.WriteAllText(config,text);File.WriteAllText(Path.Combine(source,"zh-TW.json"),language);
        File.WriteAllText(Path.Combine(target,"zh-TW.World.json"),"existing custom language"); File.WriteAllText(Path.Combine(target,"Config.SmartHunter.json"),"{}");
        var imported=SettingsImporter.Import(new[]{config},target,GameChoice.World,"zh-TW.World.json"); var again=SettingsImporter.Import(new[]{config},target,GameChoice.World,"zh-TW.World.json");
        Assert(imported.Overlay.MonsterWidget.X==153 && imported.Overlay.TeamWidget.Y==667 && imported.Overlay.PlayerWidget.Scale==1.7f,"position and scale transferred");
        Assert(File.ReadAllText(config)==text && File.ReadAllText(Path.Combine(source,"zh-TW.json"))==language,"original files remain byte-equivalent");
        var converted=JObject.Parse(File.ReadAllText(Path.Combine(target,imported.LanguageFileName)));
        Assert((string)converted["Author"]=="User" && (string)converted["Strings"]["LOC_PART_HEAD"]=="自訂頭部" && imported.LanguageFileName==again.LanguageFileName && !Directory.Exists(Path.Combine(target,"Backups")),"translation converted directly without backup folders");
        Assert(File.ReadAllText(Path.Combine(target,"zh-TW.World.json"))=="existing custom language" && File.Exists(GameSettingsStore.PathFor(target,GameChoice.World)),"existing language untouched and configuration backed up");
    }
    sealed class DelayedHandler : System.Net.Http.HttpMessageHandler
    {
        public readonly System.Threading.Tasks.TaskCompletionSource<System.Net.Http.HttpResponseMessage> Response = new System.Threading.Tasks.TaskCompletionSource<System.Net.Http.HttpResponseMessage>();
        public int Calls;
        protected override System.Threading.Tasks.Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request, System.Threading.CancellationToken token)
        { System.Threading.Interlocked.Increment(ref Calls); return Response.Task; }
    }
internal static void LateCallbacks()
    {
        var main=ConfigHelper.Main.Values;string url=main.Relay.ServerUrl;main.Relay.ServerUrl="http://unit.invalid";
        try
        {
            var handler=new DelayedHandler();
            using(var client=new System.Net.Http.HttpClient(handler))
            {
                var server=new SmartHunter.Core.Helpers.ServerManager(client);int callbacks=0;
                var task=System.Threading.Tasks.Task.Run(()=>server.RequestCommandAsync(SmartHunter.Core.Helpers.ServerManager.Command.PULL,"key",null,false,0,null,(value,time)=>callbacks++));
                Assert(System.Threading.SpinWait.SpinUntil(()=>handler.Calls==1,2000),"request starts");
                server.Invalidate();
                handler.Response.SetResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content=new System.Net.Http.StringContent("{\"status\":\"ok\"}") });
                Assert(task.Wait(3000) && callbacks==0,"stale response cannot update the next game's overlay");
            }
        }
        finally { main.Relay.ServerUrl=url; }
    }
internal static void RenderProfiles()
    {
        var main=ConfigHelper.Main.Values; var active=GameProfile.Active;var choice=main.Game;bool sample=main.WorldOptions.Debug.UseSampleData,wildsSample=main.WildsOptions.Debug.UseSampleData;
        var settings=SettingsViewModel.Instance;var section=settings.SelectedSection;
        var window=new SmartHunter.Ui.Windows.ConsoleWindow();
        try
        {
            main.WorldOptions.Debug.UseSampleData=main.WildsOptions.Debug.UseSampleData=true;
            foreach(var game in new[] { GameChoice.World,GameChoice.Wilds })
            {
                main.Game=game;
                using(var updater=new UnifiedMemoryUpdater(g=>false,null,false))
                {
                    settings.SelectedSection=settings.Sections.First(x=>x.Id=="General");
                    Assert(settings.EditingGame == game && settings.ActiveGameProfile == UiText.Instance[game==GameChoice.World ? "LOC_UI_PROFILE_WORLD" : "LOC_UI_PROFILE_WILDS"],"localized active profile is visible");
                    TestSupport.Render((System.Windows.FrameworkElement)window.Content,"unified-"+game+"-settings.png",1120,780);
                    var host=new System.Windows.Controls.ContentControl { Content=OverlayViewModel.Instance.MonsterWidget.Context, DataContext=OverlayViewModel.Instance.MonsterWidget.Context, Width=1200 };
                    TestSupport.Render(host,"unified-"+game+"-overlay.png",1200,360);
                    Assert(OverlayViewModel.Instance.MonsterWidget.Context.Monsters.Count==(game==GameChoice.World?3:6),"profile-specific preview uses correct population");
                }
            }
        }
        finally {window.Close();main.Game=choice;main.WorldOptions.Debug.UseSampleData=sample;main.WildsOptions.Debug.UseSampleData=wildsSample;GameProfile.Active=active;ConfigHelper.SwitchGameData(active);settings.SelectedSection=section;}
    }
    sealed class Reader : IDisposable
    {
        readonly Action stop; public Reader(Action stop) { this.stop=stop; } public void Dispose()=>stop();
    }
internal static void ReaderLifecycle()
    {
        var main=ConfigHelper.Main.Values; var choice=main.Game; var active=GameProfile.Active; bool sample=main.Debug.UseSampleData;
        bool world=true,wilds=false; int current=0,starts=0,stops=0;
        try
        {
            main.Game=GameChoice.Auto; main.Debug.UseSampleData=false;
            var overlay=OverlayViewModel.Instance;
            using(var updater=new UnifiedMemoryUpdater(g=>g==GameChoice.World?world:wilds,(g,p)=> { Assert(current==0,"previous reader must stop first"); current++;starts++;return new Reader(()=>{current--;stops++;}); },false))
            {
                Assert(GameProfile.Active==GameChoice.World && starts==1,"World starts");
                updater.Poll();Assert(starts==1,"steady state does not recreate reader");
                world=false;wilds=true;updater.Poll();
                Assert(GameProfile.Active==GameChoice.Wilds && starts==2 && stops==1 && ReferenceEquals(overlay,OverlayViewModel.Instance),"Wilds reuses exact overlay instance after World stops");
                wilds=false;updater.Poll();Assert(current==0 && stops==2,"no game stops reader");
            }
        }
        finally { main.Game=choice; main.Debug.UseSampleData=sample; GameProfile.Active=active; ConfigHelper.SwitchGameData(active); }
    }
}

static class InputMigrationTests
{
internal static void HookFailure()
    {
        using(var hook=new KeyboardInput())
        {
            int calls=0; string logged="";
            EventHandler<GenericEventArgs<string>> broken=(s,e)=>{throw new InvalidOperationException("log UI failed");};
            EventHandler<GenericEventArgs<string>> capture=(s,e)=>logged+=e.Value;
            Log.LineReceived+=broken; Log.LineReceived+=capture;
            hook.InputReceived+=(s,e)=>{calls++;if(calls==1) throw new InvalidOperationException("shortcut callback test"); e.Handled=true;};
            IntPtr data=Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WindowsApi.KBDLLHOOKSTRUCT)));
            try {
                var callback=typeof(KeyboardInput).GetMethod("KeyboardHook",BindingFlags.Instance|BindingFlags.NonPublic);
                Marshal.StructureToPtr(new WindowsApi.KBDLLHOOKSTRUCT{VkCode=0x75},data,false);
                callback.Invoke(hook,new object[]{0,new IntPtr(0x100),data});
                Assert(logged.Contains("shortcut callback test") && logged.Contains("HookFailure"),"full exception recorded even with broken log observer");
                var result=(IntPtr)callback.Invoke(hook,new object[]{0,new IntPtr(0x100),data});
                Assert(calls==2 && result==new IntPtr(1),"hook remains usable and consumes next action");
            } finally {Marshal.FreeHGlobal(data);Log.LineReceived-=broken;Log.LineReceived-=capture;}
        }
    }
internal static void Servers()
    {
        const string shared="{\"Relay\":{\"Enabled\":true,\"ServerUrl\":\"http://old.invalid/index.php\",\"ClientId\":\"local\",\"Role\":\"Guest\"}}";
        var migrated=JsonConvert.DeserializeObject<MainConfig>(shared);
        Assert(migrated.Servers.World.Enabled && migrated.Servers.Wilds.ServerUrl==migrated.Servers.World.ServerUrl && !Object.ReferenceEquals(migrated.Servers.World,migrated.Servers.Wilds),"old common settings copied, not shared by reference");
        migrated.Servers.World.ServerUrl="http://world.invalid";
        Assert(migrated.Servers.Wilds.ServerUrl=="http://old.invalid/index.php","server editing independent");
        foreach(string text in new[]{"{\"Servers\":{\"World\":{\"ServerUrl\":\"http://new.invalid\"}},\"Relay\":{\"ServerUrl\":\"http://old.invalid\"}}", "{\"Relay\":{\"ServerUrl\":\"http://old.invalid\"},\"Servers\":{\"World\":{\"ServerUrl\":\"http://new.invalid\"}}}"}) {
            var result=JsonConvert.DeserializeObject<MainConfig>(text);
            Assert(result.Servers.World.ServerUrl=="http://new.invalid" && result.Servers.Wilds.ServerUrl==RelayConfig.DefaultWildsServerUrl,"new profiles win in either JSON property order");
        }
        var saved=JObject.Parse(JsonConvert.SerializeObject(migrated));Assert(saved["Relay"]==null && saved["Servers"]["World"]!=null,"save only named server profiles");
        JsonConvert.PopulateObject(JsonConvert.SerializeObject(migrated),migrated);Assert(migrated.Servers.World.ServerUrl=="http://world.invalid","file watcher PopulateObject preserves migration semantics");
        var readerConfig=migrated.Servers.Wilds;
        JsonConvert.PopulateObject("{\"Servers\":{\"Wilds\":{\"ServerUrl\":\"http://reloaded.invalid\"}}}",migrated);
        Assert(Object.ReferenceEquals(readerConfig,migrated.Servers.Wilds) && readerConfig.ServerUrl=="http://reloaded.invalid","live reader retains the updated profile after external JSON reload");
    }
internal static void ServerEditor()
    {
        var main=ConfigHelper.Main.Values;var servers=main.Servers;var game=GameProfile.Active;var editing=main.SettingsGame;var vm=SettingsViewModel.Instance;
        try {main.Servers=new GameServerProfiles();GameProfile.Active=GameChoice.Wilds;main.SettingsGame=GameChoice.World;vm.RefreshGameProfile();
            vm.Settings.Single(s=>s.Key=="LOC_UI_RELAY_URL").Text="http://world.invalid/index.php";
            vm.Settings.Single(s=>s.Key=="LOC_UI_RELAY_ENABLED").Value=true;
            Assert(main.Servers.World.Enabled && main.Servers.World.ServerUrl.Contains("world.invalid") && !main.Relay.Enabled && main.Relay.ServerUrl==RelayConfig.DefaultWildsServerUrl && GameProfile.Active==GameChoice.Wilds,"inactive editor does not change active server");
        }finally {main.Servers=servers;main.SettingsGame=editing;GameProfile.Active=game;vm.RefreshGameProfile();}
    }
internal static void Import()
    {
        var root=Folder();var source=Path.Combine(root,"old");var target=Path.Combine(root,"new");Directory.CreateDirectory(source);Directory.CreateDirectory(target);
        var main=new MainConfig();main.Servers.Wilds.ServerUrl="http://wilds.invalid";main.Servers.World.Role=RelayRole.Guest;
        GameSettingsStore.Write(target,main);
        var config=Path.Combine(source,"Config.json");File.WriteAllText(config,"{\"ServerUrl\":\"http://world.invalid/index.php\",\"LocalizationFileName\":\"zh-TW.json\",\"Overlay\":{\"MonsterWidget\":{\"X\":123,\"UseNetworkServer\":true}}}");
        File.WriteAllText(Path.Combine(source,"zh-TW.json"),"{\"Author\":\"User\",\"Strings\":{\"CUSTOM\":\"保留\"}}");
        var result=SettingsImporter.Import(new[]{config},target,GameChoice.World,"en-US.World.json");
        var saved=GameSettingsStore.Read(target);
        Assert(saved.Servers.World.Enabled && saved.Servers.World.ServerUrl=="http://world.invalid/index.php" && saved.Servers.World.Role==RelayRole.Guest && saved.Servers.Wilds.ServerUrl=="http://wilds.invalid","legacy URL and toggle migrate only to chosen game, preserving absent fields");
        Assert(saved.Overlays.World.MonsterWidget.X==123 && LanguageCatalog.SameFile(target,saved.WorldLocalizationFileName,result.LanguageFileName) && File.Exists(Path.Combine(target,result.LanguageFileName)),"conversion persisted directly into classified current files");
        Assert(!Directory.EnumerateDirectories(target,"Imports",SearchOption.AllDirectories).Any(),"no import directories created");
        Directory.CreateDirectory(Path.Combine(target,"Imports/old"));File.WriteAllText(Path.Combine(target,"Imports/old/Original.json"),"keep");
        StorageLayout.Prepare(target);
        Assert(!Directory.Exists(Path.Combine(target,"Imports")) && !Directory.Exists(Path.Combine(target,"Backups")),"obsolete import archives are removed after conversion");
    }
}

static class RetiredSettingsTests
{
internal static void BackupRemoved()
    {
        var config = JsonConvert.DeserializeObject<MainConfig>("{\"UserDataPath\":\"C:\\\\old\",\"BackupWhenProcessExits\":true,\"ShutdownWhenProcessExits\":true}");
        string json = JsonConvert.SerializeObject(config);
        Assert(config.ShutdownWhenProcessExits && !json.Contains("UserData") && !json.Contains("BackupWhenProcessExits"), "old backups ignored, supported settings retained");
        Assert(!SettingsViewModel.Instance.Settings.Any(s => s.Key.Contains("BACKUP") || s.Key.Contains("USERADATA")), "no save-folder or backup settings");
        foreach (string language in new[] { "en-US.json", "zh-TW.json", "zh-CN.json" })
        {
            var strings = LanguageCatalog.ReadBuiltIn(language);
            Assert(!strings.ContainsKey("LOC_UI_LOCAL") && !strings.ContainsKey("LOC_SETTING_BACKUP_WHEN_PROCESS_EXIT"), "removed language labels");
            Assert(strings.ContainsKey("LOC_UI_BACKGROUND_CHOOSE") && strings.ContainsKey("LOC_UI_NETWORK_DIAGNOSTICS"), "new settings translated");
        }
    }
}

static class ProfilePersistenceTests
{
internal static void SeparateFiles()
    {
        string root=Folder();var values=new MainConfig();values.WorldOptions.InterfaceFontSize=20;values.WildsOptions.InterfaceFontSize=15;values.WorldOptions.BackgroundImagePath="world.jpg";values.WildsOptions.BackgroundImagePath="wilds.jpg";values.WorldOptions.Keybinds[InputControl.HideWidgets]=Key.F11;values.WildsOptions.Debug.ShowServerLogs=true;
        values.Servers.World.ServerUrl="http://world.invalid";values.Servers.Wilds.ServerUrl="http://wilds.invalid";values.Overlays.World.MonsterWidget.X=-500;values.Overlays.Wilds.MonsterWidget.X=800;GameSettingsStore.Write(root,values);var loaded=GameSettingsStore.Read(root);
        Assert(loaded.WorldOptions.InterfaceFontSize==20 && loaded.WildsOptions.InterfaceFontSize==15 && loaded.WorldOptions.BackgroundImagePath=="world.jpg" && loaded.WildsOptions.BackgroundImagePath=="wilds.jpg" && loaded.WorldOptions.Keybinds[InputControl.HideWidgets]==Key.F11 && loaded.WildsOptions.Debug.ShowServerLogs,"all independent options round trip");
        foreach(var game in new[]{GameChoice.World,GameChoice.Wilds}) {var json=JObject.Parse(File.ReadAllText(GameSettingsStore.PathFor(root,game)));Assert(json["Overlay"]!=null && json["Server"]!=null && json["Overlays"]==null && json["Servers"]==null && json["WorldLocalizationFileName"]==null && (string)json["LocalizationFileName"]=="zh-TW.json","one game per file, no sibling profile or duplicate file aliases");}
        Assert(!Directory.Exists(Path.Combine(root,"Config/Shared")) && !Directory.Exists(Path.Combine(root,"Backups")),"no shared or backup directory");
    }
internal static void Migrate()
    {
        string root=Folder();Directory.CreateDirectory(Path.Combine(root,"Config/Shared"));Directory.CreateDirectory(Path.Combine(root,"Backups/old"));File.WriteAllText(Path.Combine(root,"Backups/old/obsolete.json"),"{}");
        File.WriteAllText(Path.Combine(root,"Config/Shared/Settings.json"),"{\"Game\":\"World\",\"InterfaceFontSize\":19,\"WorldLocalizationFileName\":\"en-US.World.json\",\"Servers\":{\"World\":{\"Enabled\":true,\"ServerUrl\":\"http://legacy.invalid\"}},\"Overlays\":{\"World\":{\"MonsterWidget\":{\"X\":321}},\"Wilds\":{\"MonsterWidget\":{\"X\":654}}}}");
        var newer=new MainConfig();newer.WildsOptions.InterfaceFontSize=15;newer.Overlays.Wilds.MonsterWidget.X=987;GameSettingsStore.Write(root,newer,GameChoice.Wilds);string before=File.ReadAllText(GameSettingsStore.PathFor(root,GameChoice.Wilds));
        StorageLayout.Prepare(root);var loaded=GameSettingsStore.Read(root);Assert(loaded.WorldOptions.InterfaceFontSize==19 && loaded.WildsOptions.InterfaceFontSize==15 && loaded.Overlays.World.MonsterWidget.X==321 && loaded.Overlays.Wilds.MonsterWidget.X==987 && loaded.Servers.World.Enabled && loaded.GetLanguage(GameChoice.World)=="en-US.World.json","old values split and newer profile wins");
        Assert(File.ReadAllText(GameSettingsStore.PathFor(root,GameChoice.Wilds))==before && !Directory.Exists(Path.Combine(root,"Backups")) && !Directory.Exists(Path.Combine(root,"Config/Shared")) && StorageLayout.Prepare(root)==0,"migration is idempotent and retires only obsolete folders");
        string importedRoot=Folder(),name="zh-TW.ImportedWorld-test.json";
        File.WriteAllText(Path.Combine(importedRoot,"Config.SmartHunter.json"),"{\"WorldLocalizationFileName\":\""+name+"\"}");File.WriteAllText(Path.Combine(importedRoot,name),"{\"Strings\":{\"CUSTOM\":\"保留\"}}");StorageLayout.Prepare(importedRoot);
        var imported=GameSettingsStore.Read(importedRoot);Assert(LanguageCatalog.SameFile(importedRoot,imported.WorldLocalizationFileName,Path.Combine("Languages/World",name)) && File.ReadAllText(StorageLayout.Resolve(importedRoot,imported.WorldLocalizationFileName)).Contains("保留"),"old imported language retains its exact file identity");
    }
internal static void DirectImport()
    {
        string root=Folder(),source=Path.Combine(root,"old.json"),target=Path.Combine(root,"app");File.WriteAllText(source,"{\"Overlay\":{\"MonsterWidget\":{\"X\":135}},\"ServerUrl\":\"http://old.invalid/index.php\"}");string before=File.ReadAllText(source);var values=new MainConfig();values.WildsOptions.InterfaceFontSize=20;GameSettingsStore.Write(target,values);string sibling=File.ReadAllText(GameSettingsStore.PathFor(target,GameChoice.Wilds));
        SettingsImporter.Import(new[]{source},target,GameChoice.World,"zh-TW.World.json");SettingsImporter.Import(new[]{source},target,GameChoice.World,"zh-TW.World.json");
        Assert(File.ReadAllText(source)==before && File.ReadAllText(GameSettingsStore.PathFor(target,GameChoice.Wilds))==sibling && GameSettingsStore.Read(target).Overlays.World.MonsterWidget.X==135 && !Directory.Exists(Path.Combine(target,"Backups")) && !Directory.Exists(Path.Combine(target,"Imports")),"direct repeat import preserves sources, sibling and flat directory count");
    }
internal static void StableWrites()
    {
        string root=Folder();var values=new MainConfig();GameSettingsStore.Write(root,values);string path=GameSettingsStore.PathFor(root,GameChoice.World);var time=File.GetLastWriteTimeUtc(path);var relay=values.Servers.World;var overlay=values.Overlays.World;
        GameSettingsStore.Write(root,values);GameSettingsStore.Read(root,values);Assert(File.GetLastWriteTimeUtc(path)==time && ReferenceEquals(relay,values.Servers.World) && ReferenceEquals(overlay,values.Overlays.World) && !Directory.GetFiles(root,"*.tmp",SearchOption.AllDirectories).Any(),"unchanged writes are skipped; reload preserves live configuration objects");
    }
internal static void LiveReload()
    {
        var main=ConfigHelper.Main;var original=main.Values;var active=GameProfile.Active;
        try {main.Values=new MainConfig();GameProfile.Active=GameChoice.Wilds;main.Save(false);string path=GameSettingsStore.PathFor(FileContainer.GetFullPath(),GameChoice.World);var document=JObject.Parse(File.ReadAllText(path));document["Game"]="World";document["InterfaceFontSize"]=19;document["Overlay"]["MonsterWidget"]["X"]=444;document["Server"]["ServerUrl"]="http://changed.invalid";File.WriteAllText(path,document.ToString());
            var clock=Stopwatch.StartNew();while(main.Values.WorldOptions.InterfaceFontSize!=19 && clock.ElapsedMilliseconds<3000){Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ContextIdle);Thread.Sleep(20);}
            Assert(main.Values.WorldOptions.InterfaceFontSize==19 && main.Values.WildsOptions.InterfaceFontSize==16 && main.Values.Overlays.World.MonsterWidget.X==444 && main.Values.Servers.World.ServerUrl=="http://changed.invalid" && GameProfile.Active==GameChoice.Wilds && main.Values.Game==GameChoice.Auto,"real file watcher reloads inactive profile only");
        }finally{main.Values=original;GameProfile.Active=active;main.Save(false);ConfigHelper.SwitchGameData(active);}
    }
internal static void InvalidReload()
    {
        var values=new MainConfig();values.WorldOptions.InterfaceFontSize=18;var broken=JObject.Parse("{\"InterfaceFontSize\":20,\"Overlay\":{\"MonsterWidget\":null}}");bool rejected=false;
        try{GameSettingsStore.Apply(values,GameChoice.World,broken);}catch(InvalidDataException){rejected=true;}
        Assert(rejected && values.WorldOptions.InterfaceFontSize==18 && values.Overlays.World.MonsterWidget!=null,"validation precedes live mutation");
        string root=Folder(),path=GameSettingsStore.PathFor(root,GameChoice.World);Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,"invalid JSON");GameSettingsStore.Read(root,values);
        Assert(values.WorldOptions.InterfaceFontSize==18 && File.ReadAllText(path)=="invalid JSON","a malformed existing profile remains untouched");
    }
internal static void IndependentPreview()
    {
        var main=ConfigHelper.Main;var original=main.Values;var active=GameProfile.Active;bool preview=false;int starts=0;
        try{main.Values=new MainConfig();GameProfile.Active=GameChoice.Wilds;main.Values.Game=GameChoice.World;main.Values.WorldOptions.Debug.UseSampleData=true;
            using(var updater=new UnifiedMemoryUpdater(g=>false,(g,p)=>{preview=p;starts++;return new DummyReader();},false))
                Assert(starts==1 && preview && GameProfile.Active==GameChoice.World && !main.Values.WildsOptions.Debug.UseSampleData,"no one-poll lag or inherited preview flag from the previous game");
        }finally{main.Values=original;GameProfile.Active=active;ConfigHelper.SwitchGameData(active);}
    }
    sealed class DummyReader:IDisposable {public void Dispose(){}}
}

static class AtomicFileTests
{
    internal static void LockedFile()
    {
        string root = Folder(), path = Path.Combine(root, "Settings.json");
        File.WriteAllText(path, "original");
        Task writing;
        using (var held = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            writing = Task.Run(() => AtomicFile.Write(path, "updated"));
            Assert(SpinWait.SpinUntil(() => Directory.GetFiles(root, "*.tmp").Length == 1 || writing.IsCompleted, 1000), "write reaches the replacement while the target is locked");
            Thread.Sleep(70);
            Assert(!writing.IsCompleted && File.ReadAllText(path) == "original", "locked target retains its original data while retrying");
        }
        Assert(writing.Wait(2000) && File.ReadAllText(path) == "updated", "write completes after the temporary lock is released");

        using (var held = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var elapsed = Stopwatch.StartNew();
            IOException failure = null;
            try { AtomicFile.Write(path, "must not replace"); }
            catch (IOException error) { failure = error; }
            Assert(failure != null && failure.Message.Contains(path) && elapsed.ElapsedMilliseconds < 2000, "persistent lock reports the file path within a fixed limit");
            Assert(File.ReadAllText(path) == "updated", "failed write preserves the complete previous file");
        }
        Assert(Directory.GetFiles(root).Length == 1 && !Directory.GetDirectories(root).Any(), "neither write leaves a backup or temporary file");
    }
}

static class SettingsImportTests
{
internal static void Storage()
    {
        var root=Folder(); var source=new Dictionary<string,string> {
            {"Config.SmartHunter.json","{\"InterfaceFontSize\":18}"},{"Memory.World.json","{\"Pattern\":\"AA ?? BB\"}"},
            {"MonsterData.Wilds.json","{}"},{"zh-TW.World.json","{\"Strings\":{\"x\":\"字\"}}"},
            {"zh-TW.ImportedWorld-test.json","{}"},{"Scan.Wilds.json","{}"},{"Log.SmartHunter.txt","original log"},{"Default.xaml","<ResourceDictionary/>"}};
        foreach(var file in source) File.WriteAllText(Path.Combine(root,file.Key),file.Value);
        File.WriteAllText(Path.Combine(root,"PlayerData.World.json"),"invalid JSON");
        File.WriteAllText(Path.Combine(root,"archive.zip"),"untouched");
        var current=StorageLayout.Resolve(root,"Config.SmartHunter.json");Directory.CreateDirectory(Path.GetDirectoryName(current));File.WriteAllText(current,"{\"newer\":true}");
        Assert(StorageLayout.Prepare(root)==source.Count,"all known valid files migrated");
        foreach(var file in source) Assert(!File.Exists(Path.Combine(root,file.Key)),"old alias retired after conversion: "+file.Key);
        Assert(!Directory.Exists(Path.Combine(root,"Backups")),"no backup folder");
        Assert(File.ReadAllText(current).Contains("newer") && File.Exists(Path.Combine(root,"Languages/World/zh-TW.json")),"existing target wins and language classified");
        Assert(File.ReadAllText(Path.Combine(root,"archive.zip"))=="untouched" && File.ReadAllText(Path.Combine(root,"PlayerData.World.json"))=="invalid JSON","unrelated and corrupt files preserved");
        Assert(StorageLayout.Prepare(root)==0,"second upgrade is a no-op");
    }
internal static void Watcher()
    {
        var container=new FileContainer("Config.SmartHunter.json");
        var field=typeof(FileContainer).GetField("m_FileWatcher",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
        var watcher=(FileSystemWatcher)field.GetValue(container);
        try { Assert(watcher.Filter=="Settings.json" && watcher.Path==Path.GetFullPath(Path.Combine(Root,"Config",GameProfile.Active.ToString())),"watch physical path instead of old alias: "+watcher.Path+" / "+watcher.Filter); }
        finally { watcher.Dispose(); }
    }
internal static void StartupUpgrade()
    {
        var target=Folder();
        foreach(string name in new[]{"SmartHunter.exe","SmartHunter.exe.config","Newtonsoft.Json.dll","Microsoft.Xaml.Behaviors.dll","OxyPlot.dll","OxyPlot.Wpf.dll","Themes/Default.xaml"}) {
            string path=Path.Combine(target,name);Directory.CreateDirectory(Path.GetDirectoryName(path));File.Copy(Path.Combine(Root,name),path);
        }
        const string original="{\"Game\":\"World\",\"InterfaceFontSize\":18,\"Overlays\":{\"World\":{\"MonsterWidget\":{\"X\":517.25,\"Y\":247.5,\"FontSize\":20}}}}";
        File.WriteAllText(Path.Combine(target,"Config.SmartHunter.json"),original);
        File.WriteAllText(Path.Combine(target,"zh-TW.World.json"),"{\"Strings\":{\"LOC_UI_GAME_WORLD\":\"世界／冰原（自訂）\"},\"Author\":\"UpgradeTest\"}");
        using(var process=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.Combine(target,"SmartHunter.exe"),"--no-updates"){WorkingDirectory=target,WindowStyle=System.Diagnostics.ProcessWindowStyle.Hidden})) {
            try {
                process.WaitForInputIdle(10000);
                Assert(SpinWait.SpinUntil(()=>{process.Refresh();return process.HasExited || process.MainWindowHandle!=IntPtr.Zero;},10000) && !process.HasExited,"app creates its main window");
                Assert(process.CloseMainWindow() && process.WaitForExit(10000) && process.ExitCode==0,"normal app shutdown");
            } finally {if(!process.HasExited) {process.Kill();process.WaitForExit(3000);}}
        }
        var settings=JObject.Parse(File.ReadAllText(Path.Combine(target,"Config/World/Settings.json")));
        var language=JObject.Parse(File.ReadAllText(Path.Combine(target,"Languages/World/zh-TW.json")));
        Assert((string)settings["Game"]=="World" && (int)settings["InterfaceFontSize"]==18 && (double)settings["Overlay"]["MonsterWidget"]["X"]==517.25,"legacy game, font and coordinates loaded before defaults");
        Assert((string)language["Strings"]["LOC_UI_GAME_WORLD"]=="世界／冰原（自訂）" && (string)language["Author"]=="UpgradeTest","custom translation survives startup upgrade");
        Assert(!Directory.Exists(Path.Combine(target,"Backups")) && !File.Exists(Path.Combine(target,"Config.SmartHunter.json")),"legacy source retired without backup directory");
    }
internal static void MultiImport()
    {
        var root=Folder();var source=Path.Combine(root,"source");var target=Path.Combine(root,"target");Directory.CreateDirectory(source);Directory.CreateDirectory(target);
        var files=new List<string>();
        var config=Path.Combine(source,"Config.json");File.WriteAllText(config,"{\"Overlay\":{\"MonsterWidget\":{\"X\":333,\"Y\":222,\"Scale\":1.4}},\"LocalizationFileName\":\"zh-TW.json\"}");files.Add(config);
        foreach(var code in new[] {"zh-TW","zh-CN","en-US"}) { var path=Path.Combine(source,code+".json");File.WriteAllText(path,"{\"Strings\":{\"CUSTOM\":\"自訂\"},\"Author\":\"User\"}");files.Add(path); }
        var destination=Path.Combine(target,"Languages/World/zh-TW.json");Directory.CreateDirectory(Path.GetDirectoryName(destination));File.WriteAllText(destination,"{\"Strings\":{\"EXISTING\":\"保留\"}}");
        var before=files.ToDictionary(p=>p,File.ReadAllText);
        var result=SettingsImporter.Import(files,target,GameChoice.World,"en-US.World.json");
        Assert(result.ConfigFiles==1 && result.LanguageFiles==3 && result.Overlay.MonsterWidget.X==333 && Math.Abs(result.Overlay.MonsterWidget.Scale-1.4)<0.01,"overlay and three translations converted");
        Assert(Path.GetFullPath(Path.Combine(target,result.LanguageFileName))==Path.GetFullPath(destination),"config-selected language wins");
        var saved=JObject.Parse(File.ReadAllText(destination));
        Assert((string)saved["Author"]=="User" && (string)saved["Strings"]["LOC_CUSTOM"]=="自訂" && (string)saved["Strings"]["LOC_EXISTING"]=="保留","custom keys and metadata retained");
        Assert(!Directory.Exists(Path.Combine(target,"Languages/Wilds")) && !Directory.Exists(Path.Combine(target,"Backups")),"other profile untouched; no backup copies");
        Assert(before.All(p=>File.ReadAllText(p.Key)==p.Value),"original files byte-equivalent");
        var again=SettingsImporter.Import(files,target,GameChoice.World,"en-US.World.json");Assert(result.LanguageFileName==again.LanguageFileName && !Directory.Exists(Path.Combine(target,"Backups")),"repeat import is direct and does not grow archives");
    }
internal static void InvalidImport()
    {
        var root=Folder();var good=Path.Combine(root,"zh-TW.json");var bad=Path.Combine(root,"Memory.json");var target=Path.Combine(root,"target");
        File.WriteAllText(good,"{\"Strings\":{\"CUSTOM\":\"ok\"}}");File.WriteAllText(bad,"{\"notSettings\":true}");
        bool rejected=false;try {SettingsImporter.Import(new[]{good,bad},target,GameChoice.Wilds,"zh-TW.Wilds.json");} catch(InvalidDataException) {rejected=true;}
        Assert(rejected && !Directory.Exists(target),"unsupported batch cannot leave a partial language import");
    }
    sealed class Reader : IDisposable { public int Stops; public void Dispose() {Stops++;} }
internal static void Editor()
    {
        var main=ConfigHelper.Main.Values;var active=GameProfile.Active;var game=main.Game;var editing=main.SettingsGame;bool sample=main.Debug.UseSampleData;
        var world=main.Overlays.World;var wilds=main.Overlays.Wilds;string language=main.WorldLocalizationFileName;
        var vm=SettingsViewModel.Instance;var reader=new Reader();int starts=0;
        try {
            GameProfile.Active=GameChoice.Wilds;main.Game=GameChoice.Wilds;main.Debug.UseSampleData=false;main.SettingsGame=GameChoice.Auto;
            main.Overlays.World=new OverlayConfig();main.Overlays.Wilds=new OverlayConfig();
            using(var updater=new UnifiedMemoryUpdater(g=>g==GameChoice.Wilds,(g,p)=>{starts++;return reader;},false)) {
                var loaded=ConfigHelper.Localization.FileName;
                vm.SelectedEditingProfile=vm.ProfileOptions.Single(o=>o.Id=="World");
                var size=vm.Settings.Single(s=>s.Key=="LOC_UI_MONSTER_FONT_SIZE");size.SelectedOption=size.Options.Single(o=>o.Id=="22");
                vm.SelectedLanguage=vm.Languages.Single(l=>l.FileName=="en-US.World.json");updater.Poll();
                Assert(GameProfile.Active==GameChoice.Wilds && starts==1 && reader.Stops==0 && main.Game==GameChoice.Wilds,"editor does not switch or restart reader");
                Assert(main.Overlays.World.MonsterWidget.FontSize==22 && main.Overlays.Wilds.MonsterWidget.FontSize==16 && main.WorldLocalizationFileName=="en-US.World.json" && ConfigHelper.Localization.FileName==loaded,"inactive settings persist without changing active UI");
            }
        } finally {main.Overlays.World=world;main.Overlays.Wilds=wilds;main.WorldLocalizationFileName=language;main.Game=game;main.SettingsGame=editing;main.Debug.UseSampleData=sample;GameProfile.Active=active;ConfigHelper.SwitchGameData(active);}
    }
internal static void Locales()
    {
        var root=Folder();Directory.CreateDirectory(Path.Combine(root,"Languages/World"));
        File.WriteAllText(Path.Combine(root,"Languages/World/zh-TW.json"),"{\"Strings\":{}}");
        var options=LanguageCatalog.Discover(root,"Languages/World/zh-TW.json",GameChoice.World);
        Assert(options.Count==3 && options.Select(l=>l.FileName).Distinct().Count()==3,"physical and virtual locale alias appear once");
        var active=GameProfile.Active;try {GameProfile.Active=GameChoice.Wilds;
            Assert(LanguageCatalog.ReadBuiltIn("Languages/World/zh-TW.json")["LOC_UI_STATE_WAITINGFORPROCESS"].Contains("世界"),"explicit World path beats active Wilds");
            Assert(LanguageCatalog.ReadBuiltIn("Languages/Wilds/zh-TW.json")["LOC_UI_STATE_WAITINGFORPROCESS"].Contains("荒野"),"explicit Wilds path selects Wilds");
        } finally {GameProfile.Active=active;}
    }
internal static void Wording()
    {
        var vm=SettingsViewModel.Instance;
        foreach(string profile in new[]{"World","Wilds"}) foreach(string code in new[]{"zh-TW","zh-CN","en-US"}) {
            var strings=LanguageCatalog.ReadBuiltIn(code+"."+profile+".json");
            foreach(var setting in vm.Settings) Assert(strings.ContainsKey(setting.Key) && strings.ContainsKey(setting.Key+"_DESC"),profile+code+" setting: "+setting.Key);
            foreach(var section in vm.Sections) Assert(strings.ContainsKey(TextKeyNames.Canonical("LOC_UI_"+section.Id)) && strings.ContainsKey(TextKeyNames.Canonical("LOC_UI_"+section.Id+"_DESC")),"section "+section.Id);
            foreach(WeaponType weapon in Enum.GetValues(typeof(WeaponType))) Assert(strings.ContainsKey(TextKeyNames.Canonical("LOC_UI_WEAPON_"+weapon)),"weapon "+weapon);
            foreach(var key in new[]{"LOC_UI_EDIT_FOLLOW","LOC_UI_PROFILE_LANGUAGE","LOC_UI_STATE_WORKING","LOC_UI_STATE_WAITINGFORANYGAME","LOC_UI_UPDATE_URL_INVALID"}) Assert(strings.ContainsKey(key),"state/control "+key);
            if(code!="en-US") Assert(!strings["LOC_UI_GAME_WORLD"].Contains("World") && !strings["LOC_UI_DEBUG_WORLD_NOTE"].Contains("World"),"Chinese game names are consistent");
        }
    }
internal static void WordingUpgrade()
    {
        var active=GameProfile.Active;try { GameProfile.Active=GameChoice.Wilds;
            var text="{\"Author\":\"User\",\"Strings\":{\"LOC_UI_GAME_WORLD\":\"World／冰原\",\"LOC_UI_STATE_WORKING\":\"我自己寫的狀態\"}}";
            var value=JObject.Parse(LanguageCatalog.UpgradeBuiltInFile("zh-TW.World.json",text));
            Assert((string)value["Strings"]["LOC_UI_GAME_WORLD"]=="世界/冰原" && (string)value["Strings"]["LOC_UI_STATE_WORKING"]=="我自己寫的狀態" && (string)value["Author"]=="User","replace exact former defaults only");
            Assert(((string)value["Strings"]["LOC_UI_STATE_WAITINGFORPROCESS"]).Contains("世界"),"upgrade baseline belongs to World even while Wilds runs");
            foreach(string profile in new[]{"World","Wilds"}) foreach(string code in new[]{"zh-TW","zh-CN","en-US"})
            {
                var old=new JObject{["Author"]="User",["Strings"]=new JObject{
                    ["LOC_UI_UPDATE_MASTER_TEST"]="retired",["LOC_LOG_UPDATE_MASTER_TEST"]="retired",["LOC_UI_UPDATE_TESTDOWNLOADING"]="retired",
                    ["LOC_UI_UPDATE_REPOSITORY"]="retired",["LOC_UI_UPDATE_BRANCH_DESC"]="retired",["LOC_UI_UPDATE_URL_DESC"]="custom update wording"}};
                string language=code+"."+profile+".json";
                var cleaned=JObject.Parse(LanguageCatalog.UpgradeBuiltInFile(language,old.ToString()));
                Assert(!((JObject)cleaned["Strings"]).Properties().Any(p=>p.Name.Contains("UPDATE_MASTER") || p.Name.Contains("UPDATE_TESTDOWNLOADING") || p.Name.Contains("UPDATE_REPOSITORY") || p.Name.Contains("UPDATE_BRANCH")),"retired download controls are removed from existing "+language);
                Assert((string)cleaned["Author"]=="User" && (string)cleaned["Strings"]["LOC_UI_UPDATE_URL_DESC"]=="custom update wording" && LanguageCatalog.UpgradeBuiltInFile(language,cleaned.ToString())==null,"cleanup retains custom translations and is idempotent: "+language);
            }
        }finally {GameProfile.Active=active;}
    }
}
