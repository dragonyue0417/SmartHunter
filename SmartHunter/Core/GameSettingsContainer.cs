using System;
using System.IO;
using Newtonsoft.Json.Linq;
using SmartHunter.Game.Config;
using SmartHunter.World;

namespace SmartHunter.Core
{
    public sealed class GameSettingsContainer
    {
        public MainConfig Values;
        public event EventHandler Loaded;
        public event EventHandler Changed;
        readonly FileContainer[] files;
        public string FileName => "Settings." + GameProfile.Active + ".json";
        public string FullPathFileName => GameSettingsStore.PathFor(FileContainer.GetFullPath(), GameProfile.Active);
        public GameSettingsContainer()
        {
            Values = GameSettingsStore.Read(FileContainer.GetFullPath());
            foreach (var game in new[] { GameChoice.World, GameChoice.Wilds })
                if (!File.Exists(GameSettingsStore.PathFor(FileContainer.GetFullPath(), game))) GameSettingsStore.Write(FileContainer.GetFullPath(), Values, game);
            files = new[] { new FileContainer("Settings.World.json"), new FileContainer("Settings.Wilds.json") };
            foreach (var file in files) file.Changed += FileChanged;
        }
        void FileChanged(object sender, EventArgs e)
        {
            var file = (FileContainer)sender;
            try
            {
                var game = file.FileName == "Settings.World.json" ? GameChoice.World : GameChoice.Wilds;
                GameSettingsStore.Apply(Values, game, JObject.Parse(File.ReadAllText(file.FullPathFileName)), game == GameProfile.Active);
                Loaded?.Invoke(this, EventArgs.Empty); Changed?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception error) { Log.WriteException(error); }
        }
        public void Reload()
        {
            GameSettingsStore.Read(FileContainer.GetFullPath(), Values);
            Loaded?.Invoke(this, EventArgs.Empty); Changed?.Invoke(this, EventArgs.Empty);
        }
        public void Save(bool printToLog = true)
        {
            foreach (var file in files) file.PauseWatching();
            try
            {
                GameSettingsStore.Write(FileContainer.GetFullPath(), Values);
                if (printToLog) Log.WriteLine("World / Wilds Settings.json saved");
            }
            catch (Exception error) { Log.WriteException(error); }
            finally { foreach (var file in files) file.ResumeWatching(); }
        }
    }
}
