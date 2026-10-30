using System;
using System.Collections.Generic;
using System.Linq;
using SmartHunter.Core.Data;
using SmartHunter.Game.Helpers;

namespace SmartHunter.Core
{
    public sealed class SettingOption : Bindable
    {
        readonly string m_Key;
        readonly Func<string> label;
        public string Id { get; }
        public string Name => label?.Invoke() ?? LocalizationHelper.GetString(m_Key);
        public SettingOption(string id, string key) { Id = id; m_Key = key; }
        public SettingOption(string id, Func<string> name) {Id=id;label=name;}
        public void Refresh() => NotifyPropertyChanged(nameof(Name));
    }

    public sealed class Setting : Bindable
    {
        readonly Func<bool> m_Get;
        readonly Action<bool> m_Set;
        readonly Func<string> m_GetChoice;
        readonly Action<string> m_SetChoice;
        readonly Func<string> m_Detail;
        readonly Func<string> m_GetText;
        readonly Action<string> m_SetText;
        readonly string m_Key;
        string validationMessage = "";
        public string ValidationMessage { get => validationMessage; set => SetProperty(ref validationMessage, value); }
        public string Section { get; }
        public bool IsChoice => m_GetChoice != null;
        public bool IsAction => Action != null;
        public bool IsColor {get; private set;}
        public Func<string> ColorDefault {get;private set;}
        public System.Windows.Media.Brush ColorPreview => SmartHunter.Ui.Converters.OverlayColors.Brush(
            SmartHunter.Ui.Converters.OverlayColors.TryParse(Text,out var color)?color:
            SmartHunter.Ui.Converters.OverlayColors.TryParse(ColorDefault?.Invoke(),out color)?color:System.Windows.Media.Colors.Transparent);
        public static Setting Color(string section,string key,Func<string> get,Action<string> set,Func<string> fallback)
        {
            Setting setting=null;
            setting=new Setting(section,key,get,value=> {
                System.Windows.Media.Color parsed=System.Windows.Media.Colors.Transparent;
                if(!String.IsNullOrWhiteSpace(value) && !SmartHunter.Ui.Converters.OverlayColors.TryParse(value,out parsed)) {setting.ValidationMessage=LocalizationHelper.GetString("LOC_UI_COLOR_INVALID");return;}
                setting.ValidationMessage="";set(String.IsNullOrWhiteSpace(value)?"":SmartHunter.Ui.Converters.OverlayColors.Canonical(parsed));
            },true) {IsColor=true,ColorDefault=fallback};
            setting.Reset=new Command(_=>{setting.Text="";setting.Refresh();});return setting;
        }
        public bool IsWideText {get;set;}
        public bool IsText => m_GetText != null && !IsColor && !IsWideText;
        public bool IsToggle => !IsChoice && !IsAction && !IsText && !IsColor && !IsWideText;
        public string Text
        {
            get => m_GetText?.Invoke() ?? "";
            set { if (m_GetText == null || m_GetText() == value) return; m_SetText(value); NotifyPropertyChanged(nameof(Text)); NotifyPropertyChanged(nameof(ColorPreview)); }
        }
        public string ActionText => LocalizationHelper.GetString(m_Key == "LOC_UI_WEB_SHARE_SESSION_LINK" ? "LOC_UI_WEB_SHARE_COPY" : m_Key == "LOC_UI_IMPORT_WORLD" ? "LOC_UI_IMPORT_WORLD_CHOOSE" : m_Key=="LOC_UI_COLOR_RESET_WIDGET" ? "LOC_UI_COLOR_RESET_WIDGET_ACTION" : "LOC_UI_BACKGROUND_CHOOSE");
        public bool CanReset => Reset != null;
        public Command Action { get; }
        public Command Reset { get; private set; }
        public string Detail => m_Detail?.Invoke() ?? "";
        public IList<SettingOption> Options { get; } = new SettingOption[0];
        public SettingOption SelectedOption
        {
            get => IsChoice ? Options.FirstOrDefault(o => o.Id == m_GetChoice()) ?? Options.FirstOrDefault() : null;
            set
            {
                if (!IsChoice || value == null || !Options.Contains(value) || m_GetChoice() == value.Id) return;
                m_SetChoice(value.Id); NotifyPropertyChanged(nameof(SelectedOption));
            }
        }
        public bool RequiresRestart { get; }
        public string Key => m_Key;
        public string Name => LocalizationHelper.GetString(m_Key);
        public string Description => LocalizationHelper.GetString(m_Key + "_DESC");
        public bool Value
        {
            get => m_Get != null && m_Get();
            set
            {
                if (m_Get == null || m_Get() == value) return;
                m_Set(value);
                NotifyPropertyChanged(nameof(Value));
            }
        }
        public Setting(string section, string key, Func<bool> get, Action<bool> set, bool restart = false)
        { Section = section; m_Key = key; m_Get = get; m_Set = set; RequiresRestart = restart; }
        public Setting(string section, string key, Func<string> get, Action<string> set, params SettingOption[] options)
        { Section = section; m_Key = key; m_GetChoice = get; m_SetChoice = set; Options = new System.Collections.ObjectModel.ObservableCollection<SettingOption>(options); }
        public Setting(string section, string key, Command action, Command reset, Func<string> detail)
        { Section = section; m_Key = key; Action = action; Reset = reset; m_Detail = detail; }
        public Setting(string section, string key, Func<string> get, Action<string> set, bool text)
        { Section = section; m_Key = key; m_GetText = get; m_SetText = set; }
        public void Refresh()
        {
            ValidationMessage = "";
            NotifyPropertyChanged(nameof(ActionText));
            NotifyPropertyChanged(nameof(Name)); NotifyPropertyChanged(nameof(Description));
            NotifyPropertyChanged(nameof(Value));
            NotifyPropertyChanged(nameof(Detail));
            NotifyPropertyChanged(nameof(Text)); NotifyPropertyChanged(nameof(ColorPreview));
            foreach (var option in Options) option.Refresh();
            NotifyPropertyChanged(nameof(SelectedOption));
        }
    }
}
