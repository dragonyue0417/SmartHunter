using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Newtonsoft.Json.Linq;
using SmartHunter.Core;
using SmartHunter.Core.Config;
using SmartHunter.Game.Config;
using SmartHunter.Game.Helpers;
using SmartHunter.Ui.Converters;
using SmartHunter.World;

namespace SmartHunter.Game.Data.ViewModels
{
    public sealed partial class SettingsViewModel
    {
        string colorWidget="Monster",colorState="";
        bool refreshingColors;
        Setting stateChoice;
        Dictionary<string,string> stateGroups=new Dictionary<string,string>();
        WidgetConfig ColorWidget => colorWidget=="Team"?EditingOverlay.TeamWidget:colorWidget=="Player"?EditingOverlay.PlayerWidget:colorWidget=="Debug"?EditingOverlay.DebugWidget:(WidgetConfig)EditingOverlay.MonsterWidget;
        WidgetAppearanceConfig ColorsConfig {get {return ColorWidget.Appearance??(ColorWidget.Appearance=new WidgetAppearanceConfig());}}
        bool HasColorStates => colorWidget=="Monster" || colorWidget=="Player";
        StatusAppearanceConfig StateColors
        {
            get {StatusAppearanceConfig value;return ColorsConfig.StatusColors!=null && ColorsConfig.StatusColors.TryGetValue(colorState,out value)?value:null;}
        }
        void SetStateColor(bool text,string value)
        {
            if(String.IsNullOrEmpty(colorState)) return;
            if(ColorsConfig.StatusColors==null) ColorsConfig.StatusColors=new Dictionary<string,StatusAppearanceConfig>();
            var state=StateColors??new StatusAppearanceConfig();
            if(text) state.TextColor=value;else state.BarColor=value;
            if(String.IsNullOrEmpty(state.TextColor)&&String.IsNullOrEmpty(state.BarColor)) ColorsConfig.StatusColors.Remove(colorState);
            else ColorsConfig.StatusColors[colorState]=state;
            ConfigHelper.SaveSettings();
        }
        string SkinColor(string key,string fallback)
        {
            var value=Application.Current?.TryFindResource(key);
            return value is Color color?OverlayColors.Canonical(color):fallback;
        }
        string DefaultStateBar()
        {
            string group;stateGroups.TryGetValue(colorState,out group);
            return colorWidget=="Player"?SkinColor("A_Color_PlayerStatusEffect_Group_"+group,"#FFFFFFFF"):SkinColor("A_Color_Monster_StatusEffect_Bar","#FF00CCCC");
        }
        string DefaultStateText() => OverlayColors.TryParse(ColorsConfig.TextColor,out var color)?OverlayColors.Canonical(color):colorWidget=="Player"?DefaultStateBar():"#FFFFFFFF";
        void InitializeAppearance()
        {
            Settings.Add(new Setting("Appearance","LOC_UI_COLOR_WIDGET",()=>colorWidget,v=>{if(refreshingColors)return;colorWidget=v;ReloadAppearanceStates();},
                new SettingOption("Monster","LOC_WIDGET_MONSTER"),new SettingOption("Team","LOC_WIDGET_TEAM"),new SettingOption("Player","LOC_WIDGET_PLAYER"),new SettingOption("Debug","LOC_WIDGET_DEBUG")));
            Settings.Add(Setting.Color("Appearance","LOC_UI_COLOR_BACKGROUND",()=>ColorsConfig.BackgroundColor,v=>{ColorsConfig.BackgroundColor=v;ConfigHelper.SaveSettings();},()=>colorWidget=="Debug"?SkinColor("A_Color_Debug_Background","#99000000"):SkinColor("A_Color_Widget_Background","#00000000")));
            Settings.Add(Setting.Color("Appearance","LOC_UI_COLOR_TEXT",()=>ColorsConfig.TextColor,v=>{ColorsConfig.TextColor=v;ConfigHelper.SaveSettings();},()=>"#FFFFFFFF"));
            stateChoice=new Setting("Appearance","LOC_UI_COLOR_STATUS",()=>colorState,v=>{if(refreshingColors)return;colorState=v;RefreshAppearanceControls();},new SettingOption[0]);Settings.Add(stateChoice);
            Settings.Add(Setting.Color("Appearance","LOC_UI_COLOR_STATUS_BAR",()=>StateColors?.BarColor??"",v=>SetStateColor(false,v),DefaultStateBar));
            Settings.Add(Setting.Color("Appearance","LOC_UI_COLOR_STATUS_TEXT",()=>StateColors?.TextColor??"",v=>SetStateColor(true,v),DefaultStateText));
            Settings.Add(new Setting("Appearance","LOC_UI_COLOR_RESET_WIDGET",new Command(_=>{ColorWidget.Appearance=new WidgetAppearanceConfig();ConfigHelper.SaveSettings();RefreshAppearanceControls();}),null,()=>""));
            ReloadAppearanceStates();
        }
        void RefreshAppearanceControls()
        {
            foreach(var setting in Settings.Where(s=>s.Section=="Appearance")) setting.Refresh();
            FilteredSettings?.Refresh();NotifyPropertyChanged(nameof(NoResults));
        }
        void ReloadAppearanceStates()
        {
            if(stateChoice==null)return;
            var game=EditingGame;
            var strings=LanguageCatalog.ReadBuiltIn("en-US."+game+".json");
            foreach(var pair in LanguageCatalog.ReadBuiltIn(EditingLanguage)) strings[pair.Key]=pair.Value;
            string language=FileContainer.GetFullPathFileName(EditingLanguage);
            try {if(File.Exists(language)) foreach(var pair in JObject.Parse(File.ReadAllText(language))["Strings"].ToObject<Dictionary<string,string>>()) strings[pair.Key]=pair.Value;}
            catch(Exception error) when(error is IOException || error is Newtonsoft.Json.JsonException || error is NullReferenceException) {Log.WriteException(error);}
            JArray definitions;
            if(colorWidget=="Player")
            {
                if(game==GameChoice.World) definitions=JArray.FromObject(new WorldPlayerDataConfig().StatusEffects);
                else {
                    var effects=SmartHunter.Wilds.WildsCatalog.Instance.PlayerStatusEffects.Select(e=>new StatusEffectConfig(e.GroupId,e.NameStringId,0,null)).ToList();
                    foreach(var id in new[]{"GHILLIE","ROCKSTEADY","MENDING","EVASION","CORRUPTED"}) {effects.Add(new StatusEffectConfig("Equipment","LOC_EQUIPMENT_MANTLE_"+id,0,null));effects.Add(new StatusEffectConfig("Equipment","LOC_EQUIPMENT_RECHARGE_MANTLE_"+id,0,null));}
                    definitions=JArray.FromObject(effects);
                }
            }
            else definitions=JArray.FromObject(game==GameChoice.World?new WorldMonsterDataConfig().StatusEffects:SmartHunter.Wilds.WildsCatalog.Instance.StatusEffects);
            string path=FileContainer.GetFullPathFileName((colorWidget=="Player"?"PlayerData.":"MonsterData.")+game+".json");
            try {if(File.Exists(path) && JObject.Parse(File.ReadAllText(path))["StatusEffects"] is JArray saved) definitions=saved;}
            catch(Exception error) when(error is IOException || error is Newtonsoft.Json.JsonException) {Log.WriteException(error);}
            refreshingColors=true;
            try {
                stateChoice.Options.Clear();stateGroups.Clear();
                for(int i=0;i<definitions.Count;i++) {
                    if(colorWidget=="Monster" && i==0 && (string)definitions[i]["NameStringId"]=="LOC_STATUS_0")continue;
                    string id=i.ToString(CultureInfo.InvariantCulture),key=(string)definitions[i]["NameStringId"],name;
                    if(!strings.TryGetValue(key??"",out name)) name=key??id;
                    string label=name+" · #"+id;
                    stateChoice.Options.Add(new SettingOption(id,()=>label));stateGroups[id]=(string)definitions[i]["GroupId"];
                }
                if(!stateChoice.Options.Any(o=>o.Id==colorState)) colorState=stateChoice.Options.FirstOrDefault()?.Id??"";
            }finally{refreshingColors=false;}
            RefreshAppearanceControls();
        }
    }
}
