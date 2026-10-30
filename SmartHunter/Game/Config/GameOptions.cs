using System.Collections.Generic;
using System.Windows.Input;
using Newtonsoft.Json;
using SmartHunter.Core.Config;

namespace SmartHunter.Game.Config
{
    public sealed class GameOptions
    {
        public string SkinFileName = "Default.xaml";
        public string BackgroundImagePath = "";
        public int InterfaceFontSize = 16;
        public bool ShutdownWhenProcessExits, StartGameWithSmartHunter;
        public int ReleaseDefaultsVersion = 1;
        public UpdateConfig Updates = new UpdateConfig();
        [PreserveCollectionIntegrity]
        public Dictionary<InputControl, Key> Keybinds = new Dictionary<InputControl, Key>
        {
            { InputControl.ManipulateWidget, Key.LeftAlt }, { InputControl.HideWidgets, Key.F1 }
        };
        public DebugConfig Debug = new DebugConfig();
        public GameOptions Copy() => JsonConvert.DeserializeObject<GameOptions>(JsonConvert.SerializeObject(this));
    }
}
