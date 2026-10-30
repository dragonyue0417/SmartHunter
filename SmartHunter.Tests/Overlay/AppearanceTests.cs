using Newtonsoft.Json;
using SmartHunter.Core;
using SmartHunter.Core.Config;
using SmartHunter.Game.Config;
using SmartHunter.Game.Data;
using SmartHunter.Game.Data.ViewModels;
using SmartHunter.Game.Helpers;
using SmartHunter.Ui.Behaviors;
using SmartHunter.Ui.Converters;
using SmartHunter.Ui.Windows;
using SmartHunter.World;
using static TestSupport;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

static class AppearanceTests
{
    public static void Register(Action<string,Action> register)
    {
        if(!Application.Current.Resources.Contains("A_Color_Monster_StatusEffect_Bar")) Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("/SmartHunter;component/Ui/Resources/Default.xaml",UriKind.Relative)});
        Action<string,Action> test=(name,body)=>register(name,()=>{try{body();}finally{foreach(var window in hosts)window.Close();hosts.Clear();}});
        test("appearance defaults and JSON profiles preserve original colors and isolated overrides", Profiles);
        test("appearance editor validates colors, selects game-specific states and resets only its overlay", Editor);
        test("actual monster templates apply independent state bar, marker and text colors without changing defaults", MonsterColors);
        test("all four overlay templates retain their original backgrounds and accept live per-widget colors", WidgetColors);
        test("player status text and timer indicators inherit per-state overrides with their original group colors", PlayerColors);
        test("color picker renders alpha, validates drafts and cancels without saving", Picker);
        test("clipboard contention retries are bounded and a newer copy supersedes an old retry", ClipboardRetry);
        test("clipboard work is deferred outside the hook and repeated busy failures produce one message", ClipboardQueue);
        test("mouse wheel changes each overlay by five percent and upgrades old default steps", Wheel);
        test("Wilds scrollbars stay hidden through population and row changes while every actor remains reachable", Scrollbars);
    }
    static Color C(Brush brush)=>((SolidColorBrush)brush).Color;
    static readonly List<Window> hosts=new List<Window>();
    static void Profiles()
    {
        var main=new MainConfig();
        foreach(var overlay in new[]{main.Overlays.World,main.Overlays.Wilds}) foreach(var widget in new WidgetConfig[]{overlay.TeamWidget,overlay.MonsterWidget,overlay.PlayerWidget,overlay.DebugWidget})
            Assert(widget.Appearance.BackgroundColor=="" && widget.Appearance.TextColor=="" && widget.Appearance.StatusColors.Count==0,"default must not override any skin color");
        main.Overlays.World.MonsterWidget.Appearance.BackgroundColor="#99000000";
        main.Overlays.World.MonsterWidget.Appearance.StatusColors["18"]=new StatusAppearanceConfig{BarColor="#FFFF00FF"};
        var saved=JsonConvert.DeserializeObject<MainConfig>(JsonConvert.SerializeObject(main));
        Assert(saved.Overlays.World.MonsterWidget.Appearance.StatusColors["18"].BarColor=="#FFFF00FF" && saved.Overlays.Wilds.MonsterWidget.Appearance.StatusColors.Count==0 && saved.Overlays.World.TeamWidget.Appearance.BackgroundColor=="","per-overlay / per-game persistence");
        var old=JsonConvert.DeserializeObject<MainConfig>("{\"Overlays\":{\"World\":{\"MonsterWidget\":{\"X\":-1200,\"FontSize\":20}}}}");
        Assert(old.Overlays.World.MonsterWidget.X==-1200 && old.Overlays.World.MonsterWidget.FontSize==20 && old.Overlays.World.MonsterWidget.Appearance.TextColor=="","old coordinates, fonts and defaults survive");
    }
    static void Editor()
    {
        var main=ConfigHelper.Main.Values;var game=GameProfile.Active;var editing=main.SettingsGame;var world=main.Overlays.World;var wilds=main.Overlays.Wilds;var vm=SettingsViewModel.Instance;
        try {main.Overlays.World=GameOverlayConfig.WorldDefaults();main.Overlays.Wilds=new OverlayConfig();GameProfile.Active=GameChoice.Wilds;main.SettingsGame=GameChoice.World;vm.RefreshGameProfile();
            var target=vm.Settings.Single(s=>s.Key=="LOC_UI_COLOR_WIDGET");target.SelectedOption=target.Options.Single(o=>o.Id=="Monster");
            var choice=vm.Settings.Single(s=>s.Key=="LOC_UI_COLOR_STATUS");choice.SelectedOption=choice.Options.Single(o=>o.Id=="18");
            var bar=vm.Settings.Single(s=>s.Key=="LOC_UI_COLOR_STATUS_BAR");bar.Text="#aabbcc";
            Assert(main.Overlays.World.MonsterWidget.Appearance.StatusColors["18"].BarColor=="#FFAABBCC" && main.Overlays.Wilds.MonsterWidget.Appearance.StatusColors.Count==0 && GameProfile.Active==GameChoice.Wilds,"inactive profile edited without touching active reader");
            bar.Text="red";Assert(bar.ValidationMessage!="" && main.Overlays.World.MonsterWidget.Appearance.StatusColors["18"].BarColor=="#FFAABBCC","invalid color cannot replace valid one");
            bar.Reset.Execute(null);Assert(!main.Overlays.World.MonsterWidget.Appearance.StatusColors.ContainsKey("18"),"reset removes an empty state override");
            main.Overlays.World.MonsterWidget.Appearance.BackgroundColor="#FFFFFFFF";main.Overlays.World.TeamWidget.Appearance.TextColor="#FF000000";
            vm.Settings.Single(s=>s.Key=="LOC_UI_COLOR_RESET_WIDGET").Action.Execute(null);
            Assert(main.Overlays.World.MonsterWidget.Appearance.BackgroundColor=="" && main.Overlays.World.TeamWidget.Appearance.TextColor=="#FF000000","widget reset preserves sibling colors");
        }finally{main.Overlays.World=world;main.Overlays.Wilds=wilds;main.SettingsGame=editing;GameProfile.Active=game;vm.RefreshGameProfile();}
    }
    static ContentControl Host(object context,string image,int width=900,int height=550)
    {
        var host=new ContentControl{Content=context,DataContext=context};var window=new Window{Content=host,Width=width,Height=height,Opacity=0,ShowActivated=false,ShowInTaskbar=false};hosts.Add(window);window.Show();TestSupport.Render(host,image,width,height);return host;
    }
    static void MonsterColors()
    {
        var vm=OverlayViewModel.Instance;var main=ConfigHelper.Main.Values;var game=GameProfile.Active;var original=main.Overlays.World;var model=vm.MonsterWidget.Context;
        try {GameProfile.Active=GameChoice.World;main.Overlays.World=GameOverlayConfig.WorldDefaults();ConfigHelper.SwitchGameData(GameChoice.World);var config=main.Overlays.World.MonsterWidget;config.ShowStatusEffects=config.ShowUnchangedStatusEffects=true;
            var monster=new Monster(0x100000,"em001_00",100,70,1,1){IsSelected=true};monster.UpdateAndGetStatusEffect(0x200000,18,280,13,20,5,1);monster.UpdateAndGetStatusEffect(0x210000,23,352,51,20,5,1);model.Monsters.Clear();model.Monsters.Add(monster);model.UpdateFromConfig();
            var host=Host(model,"appearance-monster-default.png");
            var rows=Visuals<StackPanel>(host).Where(p=>p.Name=="Root" && p.DataContext is MonsterStatusEffect).ToArray();
            Assert(rows.Length==2 && rows.All(r=>C(Visuals<ContentControl>(r).Single(c=>c.Name=="Bar").Foreground)==Color.FromRgb(0,204,204)),"original cyan bars with no overrides; rows="+rows.Length+"; colors="+String.Join(",",rows.Select(r=>C(Visuals<ContentControl>(r).Single(c=>c.Name=="Bar").Foreground).ToString()))+"; monsterVisible="+monster.IsVisible);
            config.Appearance.BackgroundColor="#CCFFFFFF";config.Appearance.TextColor="#FF203047";
            config.Appearance.StatusColors["18"]=new StatusAppearanceConfig{BarColor="#FFFF00FF",TextColor="#FF3366CC"};config.Appearance.StatusColors["23"]=new StatusAppearanceConfig{BarColor="#FF33BB33"};model.UpdateFromConfig();
            TestSupport.Render(host,"appearance-monster-custom.png",900,550);
            foreach(var row in rows) {int id=((MonsterStatusEffect)row.DataContext).Index;var expected=id==18?Color.FromRgb(255,0,255):Color.FromRgb(51,187,51);
                Assert(C(Visuals<ContentControl>(row).Single(c=>c.Name=="Bar").Foreground)==expected && C(Visuals<ContentControl>(row).Single(c=>c.Name=="TimesActivatedGraphic").Background)==expected,"bar and marker follow state "+id);
                Assert(C(Visuals<TextBlock>(row).Single(t=>t.Name=="NameBlock").Foreground)==(id==18?Color.FromRgb(51,102,204):Color.FromRgb(32,48,71)),"per-state text overrides widget text");
            }
            config.Appearance=new WidgetAppearanceConfig();model.UpdateFromConfig();Drain();
            Assert(rows.All(r=>C(Visuals<ContentControl>(r).Single(c=>c.Name=="Bar").Foreground)==Color.FromRgb(0,204,204)),"reset reaches existing templates without rebuilding game data");
        }finally{model.Monsters.Clear();main.Overlays.World=original;GameProfile.Active=game;ConfigHelper.SwitchGameData(game);}
    }
    static void WidgetColors()
    {
        var vm=OverlayViewModel.Instance;var configs=new WidgetConfig[]{ConfigHelper.Main.Values.Overlay.TeamWidget,ConfigHelper.Main.Values.Overlay.MonsterWidget,ConfigHelper.Main.Values.Overlay.PlayerWidget,ConfigHelper.Main.Values.Overlay.DebugWidget};
        var contexts=new SmartHunter.Core.Data.WidgetContext[]{vm.TeamWidget.Context,vm.MonsterWidget.Context,vm.PlayerWidget.Context,vm.DebugWidget.Context};
        for(int i=0;i<4;i++){var old=configs[i].Appearance;try{configs[i].Appearance=new WidgetAppearanceConfig();contexts[i].UpdateFromConfig();var host=Host(contexts[i],"appearance-widget-"+i+"-default.png");
            var root=i==3?(FrameworkElement)Visuals<Border>(host).Single(b=>b.Name=="DebugCard"):Visuals<Grid>(host).First(g=>OverlayAppearance.GetPalette(g)!=null);
            Brush background=root is Border border?border.Background:((Grid)root).Background;
            Assert((i==3?C(background)==Color.FromArgb(153,0,0,0):C(background).A==0),"preserve original background "+i);
            configs[i].Appearance.BackgroundColor="#CC112233";configs[i].Appearance.TextColor="#FFEEDDCC";contexts[i].UpdateFromConfig();Drain();
            background=root is Border b2?b2.Background:((Grid)root).Background;Assert(C(background)==Color.FromArgb(204,17,34,51),"live per-overlay background "+i);
        }finally{configs[i].Appearance=old;contexts[i].UpdateFromConfig();}}
    }
    static void PlayerColors()
    {
        var context=OverlayViewModel.Instance.PlayerWidget.Context;var config=ConfigHelper.Main.Values.Overlay.PlayerWidget;var old=config.Appearance;
        try {config.Appearance=new WidgetAppearanceConfig();context.StatusEffects.Clear();int id=Array.FindIndex(ConfigHelper.PlayerData.Values.StatusEffects,s=>s.GroupId=="Buff");Assert(id>=0,"buff exists");context.StatusEffects.Add(new PlayerStatusEffect(id,100,60,true));context.UpdateFromConfig();var host=Host(context,"appearance-player-default.png");
            var name=Visuals<TextBlock>(host).Single(t=>t.Name=="NameBlock");var timer=Visuals<ContentControl>(host).Single(c=>c.Name=="CircleProgress");
            Assert(C(name.Foreground)==(Color)Application.Current.Resources["A_Color_PlayerStatusEffect_Group_Buff"],"default buff text group color");
            config.Appearance.StatusColors[id.ToString()]=new StatusAppearanceConfig{BarColor="#FFFF00FF",TextColor="#FF112233"};context.UpdateFromConfig();Drain();
            Assert(C(name.Foreground)==Color.FromRgb(17,34,51) && C(timer.Foreground)==Color.FromRgb(255,0,255),"individual player status color updates both text and timer");
        }finally{context.StatusEffects.Clear();config.Appearance=old;context.UpdateFromConfig();}
    }
    static void Picker()
    {
        var draft=new ColorDraft("#99000000","#FFFFFFFF");Assert(draft.A==153 && Math.Abs(draft.Opacity-60)<0.01,"alpha starts at sixty percent");draft.Opacity=100;Assert(draft.Hex=="#FF000000","opacity slider writes ARGB");draft.Hex="#notcolor";Assert(!draft.Valid && draft.Error!="","invalid draft cannot be applied");draft.Hex="#123456";Assert(draft.Valid && C(draft.Preview)==Color.FromRgb(18,52,86),"RGB accepted");
        var dialog=new ColorPickerWindow("","#99000000");try{TestSupport.Render((FrameworkElement)dialog.Content,"appearance-color-picker.png",440,520);Assert(dialog.Result==null,"opening / cancelling picker does not save color");}finally{dialog.Close();}
        var vm=SettingsViewModel.Instance;var selected=vm.SelectedSection;var window=new ConsoleWindow();try{vm.SelectedSection=vm.Sections.Single(s=>s.Id=="Appearance");TestSupport.Render((FrameworkElement)window.Content,"appearance-settings.png",1120,900);}finally{vm.SelectedSection=selected;window.Close();}
    }
    static void ClipboardRetry()
    {
        Assert(ClipboardService.TextData("中文😀").GetText(System.Windows.Forms.TextDataFormat.UnicodeText)=="中文😀","native clipboard data explicitly preserves Unicode text");
        int attempts=0;var waits=new List<int>();var writer=new ClipboardWriter(t=>{if(++attempts<3)throw new COMException("busy",unchecked((int)0x800401D0));},ms=>{waits.Add(ms);return Task.FromResult(0);});
        Assert(writer.TryWrite("中文").Result && attempts==3 && waits.SequenceEqual(new[]{50,100}),"temporary lock retries and then succeeds");
        attempts=0;waits.Clear();writer=new ClipboardWriter(t=>{attempts++;throw new ExternalException("busy",unchecked((int)0x800401D0));},ms=>{waits.Add(ms);return Task.FromResult(0);});
        Assert(!writer.TryWrite("test").Result && attempts==6 && waits.Sum()==750,"permanent lock stops after six bounded attempts");
        var gate=new TaskCompletionSource<bool>();var written=new List<string>();bool blocked=true;
        writer=new ClipboardWriter(t=>{if(blocked)throw new COMException("busy",unchecked((int)0x800401D0));written.Add(t);},ms=>gate.Task);
        var first=writer.TryWrite("old");blocked=false;Assert(writer.TryWrite("new").Result,"new copy succeeds");gate.SetResult(true);
        Assert(!first.Result && written.SequenceEqual(new[]{"new"}),"old retry cannot overwrite newer clipboard text");
    }
    static void ClipboardQueue()
    {
        var original=ClipboardService.Writer;int writes=0;var lines=new List<string>();EventHandler<GenericEventArgs<string>> log=(s,e)=>lines.Add(e.Value);Log.LineReceived+=log;
        try{ClipboardService.Writer=new ClipboardWriter(t=>writes++,ms=>Task.FromResult(0));ClipboardService.Copy("deferred");Assert(writes==0,"no clipboard operation occurs inside caller / keyboard callback");Drain();Assert(writes==1,"dispatcher executes queued write");
            ClipboardService.Writer=new ClipboardWriter(t=>{writes++;throw new COMException("busy",unchecked((int)0x800401D0));},ms=>Task.FromResult(0));lines.Clear();ClipboardService.Copy("busy");Drain();Assert(writes==7 && lines.Count(l=>l.Contains(UiText.Instance["LOC_LOG_CLIPBOARD_BUSY"]))==1,"one actionable failure after retries, without repeated COM stack traces");
        }finally{ClipboardService.Writer=original;Log.LineReceived-=log;}
    }
    static void Wheel()
    {
        Assert(new OverlayConfig().ScaleStep==.05f && JsonConvert.DeserializeObject<OverlayConfig>("{\"ScaleStep\":0.1}").ScaleStep==.05f,"old default upgrades");Assert(JsonConvert.DeserializeObject<OverlayConfig>("{\"ScaleStep\":0.2}").ScaleStep==.2f,"custom step retained");
        var vm=OverlayViewModel.Instance;var windows=new SmartHunter.Core.Windows.WidgetWindow[]{new MonsterWidgetWindow(),new TeamWidgetWindow(),new PlayerWidgetWindow(),new DebugWidgetWindow()};
        try{foreach(var window in windows){float old=window.Widget.Scale;try{window.Widget.Scale=1;var callback=typeof(SmartHunter.Core.Windows.WidgetWindow).GetMethod("WidgetWindow_MouseWheel",BindingFlags.Instance|BindingFlags.NonPublic);callback.Invoke(window,new object[]{window,new MouseWheelEventArgs(Mouse.PrimaryDevice,0,120)});Assert(Math.Abs(window.Widget.Scale-1.05f)<.00001,"one notch increases by five percentage points");callback.Invoke(window,new object[]{window,new MouseWheelEventArgs(Mouse.PrimaryDevice,0,-120)});Assert(window.Widget.Scale==1,"opposite notch returns exactly to original scale");}finally{window.Widget.Scale=old;}}}finally{foreach(var window in windows)window.Close();}
    }
    static void Scrollbars()
    {
        var game=GameProfile.Active;var vm=OverlayViewModel.Instance;var cfg=ConfigHelper.Main.Values.Overlays.Wilds.MonsterWidget;var layout=cfg.Layout;bool wrap=cfg.WrapHorizontal,active=vm.IsGameActive,hidden=vm.HideWidgetsRequested;var context=vm.MonsterWidget.Context;
        try{GameProfile.Active=GameChoice.Wilds;ConfigHelper.SwitchGameData(GameChoice.Wilds);vm.IsGameActive=true;vm.HideWidgetsRequested=false;var apply=new SmartHunter.Wilds.WildsSnapshotApplier();var window=new MonsterWidgetWindow{Opacity=0,ShowActivated=false};window.Show();try{var root=(FrameworkElement)window.Content;var scroll=(ScrollViewer)window.FindName("MonsterScroll");
            foreach(int count in new[]{3,20,7,30}) {cfg.WrapHorizontal=true;cfg.Layout=MonsterLayout.Horizontal;context.UpdateFromConfig();context.UpdateLayoutBounds(700,400,1);apply.Apply(SmartHunter.Wilds.WildsSnapshotApplier.Preview(count));TestSupport.Render(root,"appearance-hidden-bars-"+count+".png",850,600);Assert(scroll.ComputedVerticalScrollBarVisibility!=Visibility.Visible && scroll.ComputedHorizontalScrollBarVisibility!=Visibility.Visible && context.Monsters.Count==count,"layout changes never expose bars or drop actors");scroll.ScrollToEnd();Drain();Assert(scroll.VerticalOffset==scroll.ScrollableHeight,"final vertical row reachable");}
            cfg.WrapHorizontal=false;context.UpdateFromConfig();TestSupport.Render(root,"appearance-hidden-horizontal.png",850,600);Assert(scroll.HorizontalScrollBarVisibility==ScrollBarVisibility.Hidden && scroll.ScrollableWidth>0,"single row remains horizontally scrollable with no bar; visibility="+scroll.HorizontalScrollBarVisibility+" width="+scroll.ScrollableWidth+" active="+vm.IsGameActive+" visible="+vm.IsVisible+" row="+context.IsSingleRow+" monsters="+context.Monsters.Count);scroll.ScrollToRightEnd();Drain();Assert(scroll.HorizontalOffset==scroll.ScrollableWidth,"final horizontal monster reachable");
        }finally{window.Close();apply.Clear();}}finally{cfg.Layout=layout;cfg.WrapHorizontal=wrap;vm.IsGameActive=active;vm.HideWidgetsRequested=hidden;GameProfile.Active=game;ConfigHelper.SwitchGameData(game);}
    }
}
