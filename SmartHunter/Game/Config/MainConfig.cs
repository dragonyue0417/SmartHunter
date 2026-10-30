using System.Collections.Generic;
using System.Windows.Input;
using SmartHunter.Core.Config;

namespace SmartHunter.Game.Config
{
    public sealed class GameOverlayConfig
    {
        public OverlayConfig World = WorldDefaults();
        public OverlayConfig Wilds = new OverlayConfig();
        public int WorldWidthVersion = 1;

        public static OverlayConfig WorldDefaults()
        {
            var overlay = new OverlayConfig();
            overlay.MonsterWidget.CardWidth = 300;
            return overlay;
        }
        [System.Runtime.Serialization.OnDeserializing]
        void BeforeLoad(System.Runtime.Serialization.StreamingContext context) { WorldWidthVersion = 0; }
        [System.Runtime.Serialization.OnDeserialized]
        void AfterLoad(System.Runtime.Serialization.StreamingContext context)
        {
            // Only the old shared default changes. Explicit custom widths and
            // a user's later choice of 220 remain independent of this upgrade.
            if (WorldWidthVersion < 1 && World?.MonsterWidget?.CardWidth == 220)
                World.MonsterWidget.CardWidth = 300;
            WorldWidthVersion = 1;
        }
    }
    public class MainConfig
    {
        public SmartHunter.World.GameChoice Game = SmartHunter.World.GameChoice.Auto;
        public SmartHunter.World.GameChoice SettingsGame = SmartHunter.World.GameChoice.Auto;
        public OverlayConfig GetOverlay(SmartHunter.World.GameChoice game) => game == SmartHunter.World.GameChoice.World ? Overlays.World : Overlays.Wilds;
        public string GetLanguage(SmartHunter.World.GameChoice game) => game == SmartHunter.World.GameChoice.World ? WorldLocalizationFileName : WildsLocalizationFileName;
        public void SetLanguage(SmartHunter.World.GameChoice game, string value)
        { if(game==SmartHunter.World.GameChoice.World) WorldLocalizationFileName=value; else WildsLocalizationFileName=value; }
        [Newtonsoft.Json.JsonIgnore]
        public string ServerUrl => SmartHunter.Core.Helpers.ServerManager.LegacyEndpoint(Relay.ServerUrl);
        public string WorldLocalizationFileName = "zh-TW.World.json";
        public string WildsLocalizationFileName = "zh-TW.Wilds.json";
        [Newtonsoft.Json.JsonIgnore]
        public string LocalizationFileName
        {
            get => SmartHunter.World.GameProfile.Active == SmartHunter.World.GameChoice.World ? WorldLocalizationFileName : WildsLocalizationFileName;
            set { if (SmartHunter.World.GameProfile.Active == SmartHunter.World.GameChoice.World) WorldLocalizationFileName = value; else WildsLocalizationFileName = value; }
        }
        [Newtonsoft.Json.JsonIgnore] public GameOptions WorldOptions = new GameOptions();
        [Newtonsoft.Json.JsonIgnore] public GameOptions WildsOptions = new GameOptions();
        public GameOptions GetOptions(SmartHunter.World.GameChoice game) => game == SmartHunter.World.GameChoice.World ? WorldOptions : WildsOptions;
        GameOptions ActiveOptions => GetOptions(SmartHunter.World.GameProfile.Active);
        public string SkinFileName { get => ActiveOptions.SkinFileName; set => ActiveOptions.SkinFileName=value; }
        public string BackgroundImagePath { get => ActiveOptions.BackgroundImagePath; set => ActiveOptions.BackgroundImagePath=value; }
        public int InterfaceFontSize { get => ActiveOptions.InterfaceFontSize; set => ActiveOptions.InterfaceFontSize=value; }
        public bool ShutdownWhenProcessExits { get => ActiveOptions.ShutdownWhenProcessExits; set => ActiveOptions.ShutdownWhenProcessExits=value; }
        public bool StartGameWithSmartHunter { get => ActiveOptions.StartGameWithSmartHunter; set => ActiveOptions.StartGameWithSmartHunter=value; }
        // These are runtime file aliases, never stored in a game's Settings.json.
        public string MonsterDataFileName = "MonsterData.Wilds.json";
        public string PlayerDataFileName = "PlayerData.Wilds.json";
        public string MemoryFileName = "Memory.Wilds.json";

        public GameOverlayConfig Overlays = new GameOverlayConfig();
        [Newtonsoft.Json.JsonIgnore]
        public OverlayConfig Overlay => SmartHunter.World.GameProfile.Active == SmartHunter.World.GameChoice.World ? Overlays.World : Overlays.Wilds;
        // Read old single-game settings, but save separate named profiles.
        [Newtonsoft.Json.JsonProperty("Overlay")]
        OverlayConfig LegacyOverlay { set { if (value != null) Overlays.Wilds = value; } }
        GameServerProfiles servers = new GameServerProfiles();
        RelayConfig legacyRelay;
        bool hasServers;
        public GameServerProfiles Servers
        {
            get => servers;
            set { servers = value ?? new GameServerProfiles(); hasServers = true; }
        }
        [Newtonsoft.Json.JsonIgnore]
        public RelayConfig Relay => GetRelay(SmartHunter.World.GameProfile.Active);
        public RelayConfig GetRelay(SmartHunter.World.GameChoice game) => servers.Get(game);
        [Newtonsoft.Json.JsonProperty("Relay")]
        RelayConfig LegacyRelay { set { legacyRelay = value; } }
        [System.Runtime.Serialization.OnDeserializing]
        void BeforeLoad(System.Runtime.Serialization.StreamingContext context) { legacyRelay = null; hasServers = false; servers.WasLoaded = false; }
        [System.Runtime.Serialization.OnDeserialized]
        void AfterLoad(System.Runtime.Serialization.StreamingContext context)
        {
            if (!hasServers && !servers.WasLoaded && legacyRelay != null)
            {
                servers.World.CopyFrom(legacyRelay);
                servers.Wilds.CopyFrom(legacyRelay);
            }
            servers.Wilds.ApplyGameDefaults(SmartHunter.World.GameChoice.Wilds);
        }

        [PreserveCollectionIntegrity]
        public Dictionary<InputControl, Key> Keybinds { get => ActiveOptions.Keybinds; set => ActiveOptions.Keybinds=value; }
        public DebugConfig Debug { get => ActiveOptions.Debug; set => ActiveOptions.Debug=value; }
    }
}
