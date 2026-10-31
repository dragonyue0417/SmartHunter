using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.IO;
using System.Windows.Media;
using SmartHunter.Core;
using SmartHunter.Core.Data;
using SmartHunter.Game.Config;
using SmartHunter.Game.Helpers;

namespace SmartHunter.Game.Data.ViewModels
{
    public sealed class SettingsSection : Bindable
    {
        public string Id { get; }
        public string Glyph { get; }
        public string Name => UiText.Instance["LOC_UI_" + Id];
        public string Description => UiText.Instance["LOC_UI_" + Id + "_DESC"];
        public SettingsSection(string id, string glyph) { Id = id; Glyph = glyph; }
        public void Refresh() { NotifyPropertyChanged(nameof(Name)); NotifyPropertyChanged(nameof(Description)); }
    }

    public sealed partial class SettingsViewModel : Bindable
    {
        public static SettingsViewModel Instance { get; } = new SettingsViewModel();
        public IList<Setting> Settings { get; } = new List<Setting>();
        public ICollectionView FilteredSettings { get; }
        public IList<SettingsSection> Sections { get; } = new List<SettingsSection>
        {
            new SettingsSection("General", "\uE713"), new SettingsSection("Team", "\uE716"),
            new SettingsSection("Monsters", "\uE721"), new SettingsSection("Network", "\uE774"), new SettingsSection("Advanced", "\uE7BE"),
            new SettingsSection("Appearance", "\uE790"),
            new SettingsSection("Logs", "\uE9D9")
        };
        public IList<LanguageOption> Languages { get; private set; }
        public string Version => "v" + typeof(SettingsViewModel).Assembly.GetName().Version;
        public int InterfaceFontSize => Math.Max(14, Math.Min(20, EditingOptions.InterfaceFontSize));
        public int InterfaceBodyFontSize => InterfaceFontSize - 1;
        public int InterfaceSmallFontSize => Math.Max(13, InterfaceFontSize - 2);
        public int InterfaceTitleFontSize => InterfaceFontSize + 11;
        void RefreshFontSizes()
        {
            NotifyPropertyChanged(nameof(InterfaceFontSize)); NotifyPropertyChanged(nameof(InterfaceBodyFontSize));
            NotifyPropertyChanged(nameof(InterfaceSmallFontSize)); NotifyPropertyChanged(nameof(InterfaceTitleFontSize));
        }
        ImageSource backgroundImage;
        string loadedBackgroundPath;
        public ImageSource BackgroundImage
        {
            get
            {
                string path = EditingOptions.BackgroundImagePath ?? "";
                if (backgroundImage == null || path != loadedBackgroundPath)
                {
                    loadedBackgroundPath = path;
                    try { backgroundImage = WindowBackground.Load(path); }
                    catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is NotSupportedException || e is ArgumentException || e is FormatException || e is System.Runtime.InteropServices.COMException)
                    { Log.WriteLine("Custom background unavailable; using built-in image: " + e.Message); backgroundImage = WindowBackground.Load(""); }
                }
                return backgroundImage;
            }
        }
        public void SetBackground(string path)
        {
            // Decode before saving: invalid selections never replace a usable setting.
            var image = WindowBackground.Load(path);
            EditingOptions.BackgroundImagePath = path ?? "";
            loadedBackgroundPath = path ?? ""; backgroundImage = image;
            ConfigHelper.SaveSettings();
            NotifyPropertyChanged(nameof(BackgroundImage));
            foreach (var setting in Settings.Where(s => s.IsAction)) setting.Refresh();
        }
        SettingsSection m_SelectedSection;
        public SettingsSection SelectedSection
        {
            get => m_SelectedSection;
            set { if (value != null && SetProperty(ref m_SelectedSection, value)) { NotifyPropertyChanged(nameof(IsLogs)); FilteredSettings?.Refresh(); NotifyPropertyChanged(nameof(NoResults)); } }
        }
        public bool IsLogs => SelectedSection?.Id == "Logs";
        string m_Search = "";
        public string Search
        {
            get => m_Search;
            set { if (SetProperty(ref m_Search, value ?? "")) { FilteredSettings.Refresh(); NotifyPropertyChanged(nameof(NoResults)); } }
        }
        public bool NoResults => FilteredSettings != null && !FilteredSettings.Cast<object>().Any();
        public LanguageOption SelectedLanguage
        {
            get => Languages.FirstOrDefault(l => LanguageCatalog.SameFile(FileContainer.GetFullPath(), l.FileName, EditingLanguage));
            set
            {
                if (value == null || LanguageCatalog.SameFile(FileContainer.GetFullPath(), value.FileName, EditingLanguage)) return;
                ConfigHelper.Main.Values.SetLanguage(EditingGame,value.FileName);
                if(!IsEditingOtherGame) ConfigHelper.Localization.TryChangeFileName(value.FileName);
                ConfigHelper.SaveSettings();
                NotifyPropertyChanged(nameof(SelectedLanguage));
            }
        }
        bool m_RestartNeeded;
        public bool RestartNeeded { get => m_RestartNeeded; private set => SetProperty(ref m_RestartNeeded, value); }
        public Command Restart { get; }

        public SmartHunter.World.GameChoice EditingGame => ConfigHelper.Main.Values.SettingsGame == SmartHunter.World.GameChoice.Auto ? SmartHunter.World.GameProfile.Active : ConfigHelper.Main.Values.SettingsGame;
        GameOptions EditingOptions => ConfigHelper.Main.Values.GetOptions(EditingGame);
        OverlayConfig EditingOverlay => ConfigHelper.Main.Values.GetOverlay(EditingGame);
        string EditingLanguage => ConfigHelper.Main.Values.GetLanguage(EditingGame);
        public IList<SettingOption> ProfileOptions { get; } = new[] { new SettingOption("Auto","LOC_UI_EDIT_FOLLOW"), new SettingOption("World","LOC_UI_GAME_WORLD"), new SettingOption("Wilds","LOC_UI_GAME_WILDS") };
        public SettingOption SelectedEditingProfile
        {
            get => ProfileOptions.First(o=>o.Id==ConfigHelper.Main.Values.SettingsGame.ToString());
            set { if(value==null || value.Id==ConfigHelper.Main.Values.SettingsGame.ToString()) return; ConfigHelper.Main.Values.SettingsGame=(SmartHunter.World.GameChoice)Enum.Parse(typeof(SmartHunter.World.GameChoice),value.Id); ConfigHelper.SaveSettings(); RefreshGameProfile(); }
        }
        public bool IsEditingOtherGame => EditingGame != SmartHunter.World.GameProfile.Active;
        public string EditingNotice => UiText.Instance[IsEditingOtherGame ? "LOC_UI_EDIT_INACTIVE" : "LOC_UI_EDIT_ACTIVE"];
        public string ActiveGameProfile => UiText.Instance[EditingGame == SmartHunter.World.GameChoice.World ? "LOC_UI_PROFILE_WORLD" : "LOC_UI_PROFILE_WILDS"];
        public void RefreshGameProfile()
        {
            RefreshReleaseActions();
            ReloadAppearanceStates();
            RefreshFontSizes(); NotifyPropertyChanged(nameof(BackgroundImage));
            Languages.Clear();
            foreach (var option in LanguageCatalog.Discover(FileContainer.GetFullPath(), EditingLanguage, EditingGame)) Languages.Add(option);
            foreach (var setting in Settings) setting.Refresh();
            NotifyPropertyChanged(nameof(SelectedLanguage)); NotifyPropertyChanged(nameof(ActiveGameProfile));
            NotifyPropertyChanged(nameof(SelectedEditingProfile)); NotifyPropertyChanged(nameof(EditingNotice)); NotifyPropertyChanged(nameof(IsEditingOtherGame));
            FilteredSettings?.Refresh();
        }
        public SettingsViewModel()
        {
            InitializeReleaseActions();
            Languages = new ObservableCollection<LanguageOption>(LanguageCatalog.Discover(FileContainer.GetFullPath(), EditingLanguage, EditingGame));
            Restart = new Command(_ =>
            {
                string executable = typeof(SettingsViewModel).Assembly.Location;
                Process.Start(new ProcessStartInfo(executable) { WorkingDirectory = FileContainer.GetFullPath() });
                Application.Current.Shutdown();
            });
            Settings.Add(new Setting("General", "LOC_UI_GAME", () => ConfigHelper.Main.Values.Game.ToString(), v =>
            {
                ConfigHelper.Main.Values.Game = (SmartHunter.World.GameChoice)Enum.Parse(typeof(SmartHunter.World.GameChoice), v);
                ConfigHelper.SaveSettings();
            }, new SettingOption("Auto", "LOC_UI_GAME_AUTO"), new SettingOption("World", "LOC_UI_GAME_WORLD"), new SettingOption("Wilds", "LOC_UI_GAME_WILDS")));
            Settings.Add(new Setting("General", "LOC_UI_IMPORT_WORLD", new Command(_ =>
            {
                var dialog = new Microsoft.Win32.OpenFileDialog { Filter="SmartHunter JSON|*.json", CheckFileExists=true, RestoreDirectory=true, Multiselect=true, Title=UiText.Instance["LOC_UI_IMPORT_DIALOG"] };
                if (dialog.ShowDialog()!=true) return;
                try
                {
                    var result = ImportFiles(dialog.FileNames);
                    MessageBox.Show(String.Format(UiText.Instance["LOC_UI_IMPORT_DONE"], result.ConfigFiles, result.LanguageFiles), "SmartHunter");
                }
                catch (Exception error) { MessageBox.Show(error.Message, "SmartHunter"); }
            }), null, () => ""));
            Add("General", "LOC_SETTING_START_MHW_WHEN_SMARTHUNTER_START", () => EditingOptions.StartGameWithSmartHunter, v => EditingOptions.StartGameWithSmartHunter = v, false);
            Add("General", "LOC_SETTING_SHUTDOWN_WHEN_PROCESS_EXIT", () => EditingOptions.ShutdownWhenProcessExits, v => EditingOptions.ShutdownWhenProcessExits = v, false);
            Add("General", "LOC_SETTING_HIDE_WHEN_GAME_WINDOW_IS_INACTIVE", () => EditingOverlay.HideWhenGameWindowIsInactive, v => EditingOverlay.HideWhenGameWindowIsInactive = v, false);
            Settings.Add(new Setting("General", "LOC_UI_INTERFACE_FONT_SIZE", () => InterfaceFontSize.ToString(), v =>
            {
                EditingOptions.InterfaceFontSize = Int32.Parse(v);
                ConfigHelper.SaveSettings(); RefreshFontSizes();
            }, new[] { 14, 15, 16, 18, 20 }.Select(i => new SettingOption(i.ToString(), "LOC_UI_FONT_SIZE_" + i)).ToArray()));
            Settings.Add(new Setting("General", "LOC_UI_BACKGROUND_IMAGE", new Command(_ =>
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = UiText.Instance["LOC_UI_BACKGROUND_IMAGE"],
                    Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp;*.tif;*.tiff;*.gif", CheckFileExists = true, Multiselect = false, RestoreDirectory = true
                };
                if (dialog.ShowDialog() != true) return;
                try { SetBackground(dialog.FileName); }
                catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is NotSupportedException || error is ArgumentException || error is FormatException || error is System.Runtime.InteropServices.COMException)
                { MessageBox.Show(UiText.Instance["LOC_UI_BACKGROUND_ERROR"], "SmartHunter", MessageBoxButton.OK, MessageBoxImage.Warning); Log.WriteException(error); }
            }), new Command(_ => SetBackground("")), () => String.IsNullOrWhiteSpace(EditingOptions.BackgroundImagePath) ? UiText.Instance["LOC_UI_BACKGROUND_DEFAULT"] : Path.GetFileName(EditingOptions.BackgroundImagePath)));
            Add("Team", "LOC_SETTING_TEAM_WIDGET", () => EditingOverlay.TeamWidget.IsVisible, v => EditingOverlay.TeamWidget.IsVisible = v, false);
            AddOverlayFont("Team", "LOC_UI_TEAM_FONT_SIZE", () => EditingOverlay.TeamWidget, () => OverlayViewModel.Instance.TeamWidget.Context.UpdateFromConfig());
            Add("Team", "LOC_SETTING_DONT_SHOW_IF_ALONE", () => EditingOverlay.TeamWidget.DontShowIfAlone, v => EditingOverlay.TeamWidget.DontShowIfAlone = v, false);
            Add("Team", "LOC_SETTING_TEAM_WIDGET_SHOW_BARS", () => EditingOverlay.TeamWidget.ShowBars, v => EditingOverlay.TeamWidget.ShowBars = v, false);
            Add("Team", "LOC_SETTING_TEAM_WIDGET_SHOW_NUMBERS", () => EditingOverlay.TeamWidget.ShowNumbers, v => EditingOverlay.TeamWidget.ShowNumbers = v, false);
            Add("Team", "LOC_SETTING_TEAM_WIDGET_SHOW_PERCENTS", () => EditingOverlay.TeamWidget.ShowPercents, v => EditingOverlay.TeamWidget.ShowPercents = v, false);
            Add("Team", "LOC_SETTING_TEAM_WIDGET_SHOW_CHART", () => EditingOverlay.TeamWidget.ShowChart, v => EditingOverlay.TeamWidget.ShowChart = v, true);
            Add("Monsters", "LOC_SETTING_MONSTER_WIDGET", () => EditingOverlay.MonsterWidget.IsVisible, v => EditingOverlay.MonsterWidget.IsVisible = v, false);
            AddOverlayFont("Monsters", "LOC_UI_MONSTER_FONT_SIZE", () => EditingOverlay.MonsterWidget, () => OverlayViewModel.Instance.MonsterWidget.Context.UpdateFromConfig());
            Setting windowWidth = null;
            windowWidth = new Setting("Monsters", "LOC_UI_MONSTER_WINDOW_WIDTH", () => EditingOverlay.MonsterWidget.WindowWidth.ToString(), value =>
            {
                if (!Int32.TryParse(value,out int width) || width != 0 && (width < 400 || width > 7680))
                { windowWidth.ValidationMessage=UiText.Instance["LOC_UI_MONSTER_WINDOW_WIDTH_INVALID"]; return; }
                windowWidth.ValidationMessage=""; EditingOverlay.MonsterWidget.WindowWidth=width;
                ConfigHelper.SaveSettings(); OverlayViewModel.Instance.MonsterWidget.Context.UpdateFromConfig();
            },true);
            Settings.Add(windowWidth);
            Add("Monsters", "LOC_UI_SHOW_IN_CAMP", () => EditingOverlay.MonsterWidget.ShowInCamp, v => EditingOverlay.MonsterWidget.ShowInCamp = v, false);
            Settings.Add(new Setting("Monsters", "LOC_UI_MONSTER_CARD_WIDTH", () => EditingOverlay.MonsterWidget.CardWidth.ToString(), v =>
            {
                EditingOverlay.MonsterWidget.CardWidth = Int32.Parse(v);
                ConfigHelper.SaveSettings();
            }, new[] { 180, 200, 220, 240, 260, 280, 300 }.Select(i => new SettingOption(i.ToString(), "LOC_UI_CARD_WIDTH_" + i)).ToArray()));
            Settings.Add(new Setting("Monsters", "LOC_UI_MONSTER_LAYOUT", () => EditingOverlay.MonsterWidget.Layout.ToString(), v =>
            {
                EditingOverlay.MonsterWidget.Layout = (MonsterLayout)Enum.Parse(typeof(MonsterLayout), v);
                ConfigHelper.SaveSettings();
            }, new SettingOption("Vertical", "LOC_UI_LAYOUT_VERTICAL"), new SettingOption("Horizontal", "LOC_UI_LAYOUT_HORIZONTAL"), new SettingOption("Grid", "LOC_UI_LAYOUT_GRID")));
            Add("Monsters", "LOC_UI_CENTER_MONSTERS", () => EditingOverlay.MonsterWidget.CenterMonsters, v => EditingOverlay.MonsterWidget.CenterMonsters = v, false);
            Add("Monsters", "LOC_UI_MONSTER_AUTO_WRAP", () => EditingOverlay.MonsterWidget.WrapHorizontal, v => EditingOverlay.MonsterWidget.WrapHorizontal = v, false);
            Add("Monsters", "LOC_UI_MONSTER_SELECTED_DETAILS", () => EditingOverlay.MonsterWidget.SelectedDetailsOnly, v => EditingOverlay.MonsterWidget.SelectedDetailsOnly = v, false);
            Settings.Add(new Setting("Monsters", "LOC_UI_MONSTER_COLUMNS", () => EditingOverlay.MonsterWidget.Columns.ToString(), v =>
            {
                EditingOverlay.MonsterWidget.Columns = Int32.Parse(v);
                ConfigHelper.SaveSettings();
            }, Enumerable.Range(1, 12).Select(i => new SettingOption(i.ToString(), "LOC_UI_LAYOUT_COLUMNS_" + i)).ToArray()));
            Settings.Add(new Setting("Monsters", "LOC_UI_MONSTER_ROWS", () => EditingOverlay.MonsterWidget.Rows.ToString(), v =>
            {
                EditingOverlay.MonsterWidget.Rows = Int32.Parse(v);
                ConfigHelper.SaveSettings();
            }, Enumerable.Range(1, 12).Select(i => new SettingOption(i.ToString(), "LOC_UI_LAYOUT_ROWS_" + i)).ToArray()));
            Add("Monsters", "LOC_UI_PRIORITIZE_SELECTED_MONSTER", () => EditingOverlay.MonsterWidget.PrioritizeSelectedMonster, v => EditingOverlay.MonsterWidget.PrioritizeSelectedMonster = v, false);
            Add("Monsters", "LOC_SETTING_SHOW_UNCHANGED_MONSTERS", () => EditingOverlay.MonsterWidget.ShowUnchangedMonsters, v => EditingOverlay.MonsterWidget.ShowUnchangedMonsters = v, false);
            Add("Monsters", "LOC_SETTING_SHOW_PARTS", () => EditingOverlay.MonsterWidget.ShowParts, v => EditingOverlay.MonsterWidget.ShowParts = v, false);
            Add("Monsters", "LOC_SETTING_SHOW_UNCHANGED_PARTS", () => EditingOverlay.MonsterWidget.ShowUnchangedParts, v => EditingOverlay.MonsterWidget.ShowUnchangedParts = v, false);
            Add("Monsters", "LOC_SETTING_SHOW_SOFTEN_PARTS", () => EditingOverlay.MonsterWidget.ShowSoftenParts, v => EditingOverlay.MonsterWidget.ShowSoftenParts = v, false);
            Add("Monsters", "LOC_SETTING_SHOW_STATUS_EFFECT", () => EditingOverlay.MonsterWidget.ShowStatusEffects, v => EditingOverlay.MonsterWidget.ShowStatusEffects = v, false);
            Add("Monsters", "LOC_SETTING_SHOW_UNCHANGED_STATUS_EFFECTS", () => EditingOverlay.MonsterWidget.ShowUnchangedStatusEffects, v => EditingOverlay.MonsterWidget.ShowUnchangedStatusEffects = v, false);
            Add("Monsters", "LOC_SETTING_SHOW_MONSTER_SIZE", () => EditingOverlay.MonsterWidget.ShowSize, v => EditingOverlay.MonsterWidget.ShowSize = v, false);
            Add("Monsters", "LOC_SETTING_SHOW_MONSTER_CROWN", () => EditingOverlay.MonsterWidget.ShowCrown, v => EditingOverlay.MonsterWidget.ShowCrown = v, false);
            Add("Monsters", "LOC_SETTING_MONSTER_WIDGET_SHOW_BARS", () => EditingOverlay.MonsterWidget.ShowBars, v => EditingOverlay.MonsterWidget.ShowBars = v, false);
            Add("Monsters", "LOC_SETTING_MONSTER_WIDGET_SHOW_NUMBERS", () => EditingOverlay.MonsterWidget.ShowNumbers, v => EditingOverlay.MonsterWidget.ShowNumbers = v, false);
            Add("Monsters", "LOC_SETTING_MONSTER_WIDGET_SHOW_PERCENTS", () => EditingOverlay.MonsterWidget.ShowPercents, v => EditingOverlay.MonsterWidget.ShowPercents = v, false);
            Add("Monsters", "LOC_SETTING_USE_ANIMATIONS", () => EditingOverlay.MonsterWidget.UseAnimations, v => EditingOverlay.MonsterWidget.UseAnimations = v, false);
            Add("Monsters", "LOC_SETTING_SHOW_ONLY_SELECTED_MONSTER", () => EditingOverlay.MonsterWidget.ShowOnlySelectedMonster, v => EditingOverlay.MonsterWidget.ShowOnlySelectedMonster = v, false);
            Add("Monsters", "LOC_SETTING_ALWAYS_SHOW_PARTS", () => EditingOverlay.MonsterWidget.AlwaysShowParts, v => EditingOverlay.MonsterWidget.AlwaysShowParts = v, false);
            Add("General", "LOC_SETTING_PLAYER_WIDGET", () => EditingOverlay.PlayerWidget.IsVisible, v => EditingOverlay.PlayerWidget.IsVisible = v, false);
            AddOverlayFont("General", "LOC_UI_PLAYER_FONT_SIZE", () => EditingOverlay.PlayerWidget, () => OverlayViewModel.Instance.PlayerWidget.Context.UpdateFromConfig());
            Add("Network", "LOC_UI_RELAY_ENABLED", () => EditingRelay.Enabled, v => EditingRelay.Enabled = v, false);
            Settings.Add(new Setting("Network", "LOC_UI_RELAY_URL", () => EditingRelay.ServerUrl, v =>
            {
                string address = (v ?? "").Trim();
                EditingRelay.ServerUrl = address.Substring(0, Math.Min(2048, address.Length)); ConfigHelper.SaveSettings();
            }, true));
            Settings.Add(new Setting("Network", "LOC_UI_RELAY_ROLE", () => EditingRelay.Role.ToString(), v =>
            {
                EditingRelay.Role = (RelayRole)Enum.Parse(typeof(RelayRole), v); ConfigHelper.SaveSettings();
            }, new SettingOption("Auto", "LOC_UI_RELAY_AUTO"), new SettingOption("Host", "LOC_UI_RELAY_HOST"), new SettingOption("Guest", "LOC_UI_RELAY_GUEST")));
            Add("Network", "LOC_UI_RELAY_BUILDUP", () => EditingRelay.SyncAilmentBuildup, v => EditingRelay.SyncAilmentBuildup = v, false);
            Add("Network", "LOC_UI_WEB_SHARE_ENABLED", () => EditingRelay.ShareWebView, v => EditingRelay.ShareWebView = v, false);
            Settings.Add(new Setting("Network", "LOC_UI_WEB_SHARE_SESSION_LINK", new SmartHunter.Core.Command(_ => SmartHunter.Core.ClipboardService.Copy(SmartHunter.Core.WebShareIdentity.Link(EditingGame, EditingRelay.ServerUrl))), null,
                () => SmartHunter.Core.WebShareIdentity.Link(EditingGame, EditingRelay.ServerUrl)));
            Add("Advanced", "LOC_UI_DEBUG_OVERLAY", () => EditingOverlay.DebugWidget.IsVisible, v => EditingOverlay.DebugWidget.IsVisible = v, false);
            AddOverlayFont("Advanced", "LOC_UI_DEBUG_FONT_SIZE", () => EditingOverlay.DebugWidget, () => OverlayViewModel.Instance.DebugWidget.Context.UpdateFromConfig());
            Add("Advanced", "LOC_UI_PREVIEW", () => EditingOptions.Debug.UseSampleData, v => EditingOptions.Debug.UseSampleData = v, false);
            Add("Advanced", "LOC_UI_NETWORK_DIAGNOSTICS", () => EditingOptions.Debug.CollectNetworkDiagnostics, v => EditingOptions.Debug.CollectNetworkDiagnostics = v, false);
            Add("Advanced", "LOC_UI_MANTLE_DIAGNOSTICS", () => EditingOptions.Debug.CollectMantleDiagnostics, v => EditingOptions.Debug.CollectMantleDiagnostics = v, false);
            InitializeAppearance();
            FilteredSettings = new ListCollectionView(Settings.ToList());
            FilteredSettings.Filter = item =>
            {
                var setting = (Setting)item;
                if(setting.Key.StartsWith("LOC_UI_COLOR_STATUS",StringComparison.Ordinal) && !HasColorStates) return false;
                if(EditingGame != SmartHunter.World.GameChoice.World && setting.Key == "LOC_SETTING_SHOW_SOFTEN_PARTS") return false;
                if (EditingGame == SmartHunter.World.GameChoice.World &&
                    new[] { "LOC_UI_RELAY_ROLE", "LOC_UI_RELAY_BUILDUP", "LOC_UI_WEB_SHARE_ENABLED", "LOC_UI_WEB_SHARE_SESSION_LINK", "LOC_UI_NETWORK_DIAGNOSTICS", "LOC_UI_MANTLE_DIAGNOSTICS", "LOC_UI_SHOW_IN_CAMP", "LOC_UI_PRIORITIZE_SELECTED_MONSTER", "LOC_UI_MONSTER_SELECTED_DETAILS" }.Contains(setting.Key)) return false;
                if (!String.IsNullOrWhiteSpace(Search))
                    return (setting.Name + " " + setting.Description).IndexOf(Search.Trim(), StringComparison.CurrentCultureIgnoreCase) >= 0;
                return setting.Section == SelectedSection?.Id;
            };
            SelectedSection = Sections.Single(section => section.Id == "Logs");
            ConfigHelper.Localization.Loaded += (s, e) => Refresh();
            ConfigHelper.Main.Loaded += (s, e) => Refresh();
        }
        public SmartHunter.World.SettingsImportResult ImportFiles(IEnumerable<string> files)
        {
            var result = SmartHunter.World.SettingsImporter.Import(files, FileContainer.GetFullPath(), EditingGame, EditingLanguage, ConfigHelper.Main.Values);
            ConfigHelper.Main.Reload();
            ConfigHelper.SwitchGameData(SmartHunter.World.GameProfile.Active);
            if(!IsEditingOtherGame) ConfigHelper.Localization.Reload();
            RefreshGameProfile();
            return result;
        }
        RelayConfig EditingRelay => ConfigHelper.Main.Values.GetRelay(EditingGame);
        void AddOverlayFont(string section, string key, Func<SmartHunter.Core.Config.WidgetConfig> config, Action refresh)
        {
            Settings.Add(new Setting(section, key, () => Math.Max(12, Math.Min(24, config().FontSize)).ToString(), v =>
            {
                config().FontSize = Int32.Parse(v); ConfigHelper.SaveSettings(); refresh();
            }, new[] { 12, 14, 16, 18, 20, 22, 24 }.Select(i => new SettingOption(i.ToString(), "LOC_UI_OVERLAY_FONT_" + i)).ToArray()));
        }
        void Add(string section, string key, Func<bool> get, Action<bool> set, bool restart)
        {
            Settings.Add(new Setting(section, key, get, v =>
            {
                set(v); ConfigHelper.SaveSettings();
                if (restart) RestartNeeded = true;
            }, restart));
        }
        void Refresh()
        {
            RefreshReleaseActions();
            ReloadAppearanceStates();
            foreach(var option in ProfileOptions) option.Refresh();
            NotifyPropertyChanged(nameof(ActiveGameProfile)); NotifyPropertyChanged(nameof(EditingNotice));
            foreach (var section in Sections) section.Refresh();
            foreach (var setting in Settings) setting.Refresh();
            // Keep item identities stable: replacing the list while ComboBox is
            // committing a selection can erase its editable display text.
            foreach (var language in LanguageCatalog.Discover(FileContainer.GetFullPath(), EditingLanguage, EditingGame))
                if (!Languages.Any(l => String.Equals(l.FileName, language.FileName, StringComparison.OrdinalIgnoreCase))) Languages.Add(language);
            NotifyPropertyChanged(nameof(SelectedLanguage));
            NotifyPropertyChanged(nameof(BackgroundImage));
            RefreshFontSizes();
            FilteredSettings.Refresh(); NotifyPropertyChanged(nameof(NoResults));
        }
    }
}
