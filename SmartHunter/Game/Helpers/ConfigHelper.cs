using System;
using SmartHunter.Config;
using SmartHunter.Core.Config;
using SmartHunter.Game.Config;

namespace SmartHunter.Game.Helpers
{
    public static class ConfigHelper
    {
        public static event EventHandler SettingsSaved;
        public static void SaveSettings()
        {
            Main.Save();
            NotifySettingsSaved();
        }
        static void NotifySettingsSaved()
        {
            var listeners = SettingsSaved;
            if (listeners == null) return;
            foreach (EventHandler listener in listeners.GetInvocationList())
                try { listener(null, EventArgs.Empty); }
                catch (Exception error) { SmartHunter.Core.Log.WriteException(error); }
        }
        static SmartHunter.Core.GameSettingsContainer s_Main;
        static ConfigContainer<LocalizationConfig> s_Localization;
        static ConfigContainer<MonsterDataConfig> s_MonsterData;
        static ConfigContainer<PlayerDataConfig> s_PlayerData;
        static ConfigContainer<MemoryConfig> s_Memory;

        public static SmartHunter.Core.GameSettingsContainer Main
        {
            get
            {
                if (s_Main == null)
                {
                    s_Main = new SmartHunter.Core.GameSettingsContainer();
                    s_Main.Changed += Main_Loaded;
                }

                return s_Main;
            }
        }

        public static ConfigContainer<LocalizationConfig> Localization
        {
            get
            {
                if (s_Localization == null)
                {
                    s_Localization = new ConfigContainer<LocalizationConfig>(Main.Values.LocalizationFileName);
                }

                return s_Localization;
            }
        }

        public static ConfigContainer<MonsterDataConfig> MonsterData
        {
            get
            {
                if (s_MonsterData == null)
                {
                    s_MonsterData = new ConfigContainer<MonsterDataConfig>(Main.Values.MonsterDataFileName);
                }

                return s_MonsterData;
            }
        }

        public static ConfigContainer<PlayerDataConfig> PlayerData
        {
            get
            {
                if (s_PlayerData == null)
                {
                    s_PlayerData = new ConfigContainer<PlayerDataConfig>(Main.Values.PlayerDataFileName);
                }

                return s_PlayerData;
            }
        }

        public static ConfigContainer<MemoryConfig> Memory
        {
            get
            {
                if (s_Memory == null)
                {
                    s_Memory = new ConfigContainer<MemoryConfig>(Main.Values.MemoryFileName);
                }

                return s_Memory;
            }
        }

        public static void SwitchGameData(SmartHunter.World.GameChoice game)
        {
            var overlay = SmartHunter.Game.Data.ViewModels.OverlayViewModel.Instance;
            var values = Main.Values.Overlay;
            overlay.MonsterWidget.UseConfig(values.MonsterWidget); overlay.TeamWidget.UseConfig(values.TeamWidget);
            overlay.PlayerWidget.UseConfig(values.PlayerWidget); overlay.DebugWidget.UseConfig(values.DebugWidget);
            Localization.TryChangeFileName(Main.Values.LocalizationFileName);
            SmartHunter.Game.Data.ViewModels.SettingsViewModel.Instance.RefreshGameProfile();
            string suffix = game == SmartHunter.World.GameChoice.World ? "World" : "Wilds";
            Main.Values.MonsterDataFileName = "MonsterData." + suffix + ".json";
            Main.Values.PlayerDataFileName = "PlayerData." + suffix + ".json";
            if (s_MonsterData != null && s_MonsterData.FileName != Main.Values.MonsterDataFileName) { s_MonsterData.Values = new MonsterDataConfig(); s_MonsterData.TryChangeFileName(Main.Values.MonsterDataFileName); }
            if (s_PlayerData != null && s_PlayerData.FileName != Main.Values.PlayerDataFileName) { s_PlayerData.Values = new PlayerDataConfig(); s_PlayerData.TryChangeFileName(Main.Values.PlayerDataFileName); }
            NotifySettingsSaved();
        }
        // Ensures configs are loaded
        public static void EnsureConfigs()
        {
            SmartHunter.Core.DefaultFiles.Ensure(SmartHunter.Core.FileContainer.GetFullPath());
            var main = Main;
            var localization = Localization;
            var monsterData = MonsterData;
            var playerData = PlayerData;
            var memory = Memory;
        }

        static void Main_Loaded(object sender, EventArgs e)
        {
            SwitchGameData(SmartHunter.World.GameProfile.Active);
        }
    }
}
