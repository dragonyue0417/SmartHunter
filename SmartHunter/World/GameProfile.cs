using System;
using System.Diagnostics;
using System.Linq;
using System.Windows.Threading;
using SmartHunter.Core;
using SmartHunter.Core.Config;
using SmartHunter.Game;
using SmartHunter.Game.Config;
using SmartHunter.Game.Data.ViewModels;
using SmartHunter.Game.Helpers;

namespace SmartHunter.World
{
    public enum GameChoice { Auto, World, Wilds }
    public static class GameProfile
    {
        public static GameChoice Active { get; internal set; } = GameChoice.Wilds;
        public static GameChoice Select(GameChoice preference, GameChoice current, bool world, bool wilds)
        {
            if (preference != GameChoice.Auto) return preference;
            if (world && !wilds) return GameChoice.World;
            if (wilds && !world) return GameChoice.Wilds;
            return current == GameChoice.World ? GameChoice.World : GameChoice.Wilds;
        }
        public static bool Running(string name)
        {
            var processes = Process.GetProcessesByName(name);
            try { return processes.Length > 0; }
            finally { foreach (var process in processes) process.Dispose(); }
        }
        public static void Initialize()
        {
            Active = Select(ConfigHelper.Main.Values.Game, Active, Running("MonsterHunterWorld"), Running("MonsterHunterWilds"));
            string suffix=Active==GameChoice.World ? "World" : "Wilds";
            ConfigHelper.Main.Values.MonsterDataFileName="MonsterData."+suffix+".json";
            ConfigHelper.Main.Values.PlayerDataFileName="PlayerData."+suffix+".json";
        }
    }
    public static class WorldConfigHelper
    {
        public static GameSettingsContainer Main => ConfigHelper.Main;
        static ConfigContainer<WorldMemoryConfig> memory;
        public static ConfigContainer<WorldMemoryConfig> Memory => memory ?? (memory = new ConfigContainer<WorldMemoryConfig>("Memory.World.json"));
    }
    // Exactly one reader and one set of overlay windows. Changing the game stops
    // the previous reader before replacing its data definitions and session state.
    public sealed class UnifiedMemoryUpdater : IDisposable
    {
        readonly DispatcherTimer timer;
        readonly Func<GameChoice, bool> running;
        readonly Func<GameChoice, bool, IDisposable> factory;
        IDisposable reader;
        GameChoice readerGame;
        bool disposed, preview;
        string relaySettings;
        public UnifiedMemoryUpdater() : this(game => GameProfile.Running(game == GameChoice.World ? "MonsterHunterWorld" : "MonsterHunterWilds"), null, true) { }
        internal UnifiedMemoryUpdater(Func<GameChoice,bool> running, Func<GameChoice,bool,IDisposable> factory, bool startTimer)
        {
            this.running=running; this.factory=factory;
            ConfigHelper.SwitchGameData(GameProfile.Active);
            timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += Tick; if (startTimer) timer.Start();
            ConsoleViewModel.Instance.State = "WaitingForProcess";
            if (ConfigHelper.Main.Values.StartGameWithSmartHunter)
            {
                try { Process.Start(new ProcessStartInfo(GameProfile.Active == GameChoice.World ? "steam://rungameid/582010" : "steam://rungameid/2246340") { UseShellExecute = true }); }
                catch (Exception error) { Log.WriteException(error); }
            }
            Tick(null, EventArgs.Empty);
        }
        internal void Poll() => Tick(null, EventArgs.Empty);
        void Tick(object sender, EventArgs args)
        {
            try { Update(); }
            catch(Exception error) { Log.WriteException(error); ConsoleViewModel.Instance.State="Retrying"; }
        }
        void Update()
        {
            if (disposed) return;
            bool world = running(GameChoice.World), wilds = running(GameChoice.Wilds);
            var next = GameProfile.Select(ConfigHelper.Main.Values.Game, GameProfile.Active, world, wilds);
            bool sample = ConfigHelper.Main.Values.GetOptions(next).Debug.UseSampleData;
            bool present = next == GameChoice.World ? world : wilds;
            if(reader!=null && !preview && !(readerGame==GameChoice.World?world:wilds) && ConfigHelper.Main.Values.ShutdownWhenProcessExits)
            { System.Windows.Application.Current.Shutdown(); return; }
            if (reader != null && (readerGame != next || preview != sample || (!present && !sample))) StopReader();
            if (GameProfile.Active != next)
            {
                GameProfile.Active = next;
                ConfigHelper.SwitchGameData(next);
                ConsoleViewModel.Instance.GameVersion = "";
                ConsoleViewModel.Instance.NotifyPropertyChanged(nameof(ConsoleViewModel.GameVersionLabel));
                Log.WriteLine("Active game: " + next);
            }
            ConfigHelper.Main.Values.Overlay.MonsterWidget.UseNetworkServer = next == GameChoice.World && ConfigHelper.Main.Values.Relay.Enabled;
            string signature=ConfigHelper.Main.Values.Relay.Enabled+"/"+ConfigHelper.Main.Values.Relay.ServerUrl;
            if(next==GameChoice.World && reader!=null && !sample && relaySettings!=null && relaySettings!=signature)
            {
                var server=SmartHunter.Core.Helpers.ServerManager.Instance;
                server.Invalidate(); MhwHelper.ResetSession(); OverlayViewModel.Instance.DebugWidget.Context.ResetWorldSession();
                if(ConfigHelper.Main.Values.Relay.Enabled)
                    server.RequestCommadWithHandler(SmartHunter.Core.Helpers.ServerManager.Command.ALIVE,null,null,false,0,null,
                        (response,latency)=>server.IsServerOline=response!=null && (string)response["status"]=="ok"?1:-1,
                        error=>server.IsServerOline=-1);
            }
            relaySettings=signature;
            if (reader == null && (present || sample))
            {
                readerGame = next; preview = sample;
                if (factory != null) reader = factory(next, sample);
                else if (next == GameChoice.World)
                {
                    reader = sample ? (IDisposable)new WorldPreview() : new MhwMemoryUpdater();
                    ConsoleViewModel.Instance.GameVersion = "World / Iceborne";
                }
                else reader = new WildsMemoryUpdater();
                Log.WriteLine("Reader started: " + next);
            }
            if (next == GameChoice.World)
            {
                var debug=OverlayViewModel.Instance.DebugWidget.Context;
                debug.HasGameProcess=present || sample;
                var live=SmartHunter.Wilds.WildsRelayDiagnostics.Current;
                debug.SetRelayState(live.State,0,live.Applied,live.Skipped);
                debug.RefreshWorld();
            }
            if (reader == null) ConsoleViewModel.Instance.State = "WaitingForProcess";
        }
        void StopReader()
        {
            reader?.Dispose(); reader = null;
            var model = OverlayViewModel.Instance;
            model.MonsterWidget.Context.Monsters.Clear(); model.TeamWidget.Context.ClearPlayers(); model.PlayerWidget.Context.StatusEffects.Clear();
            model.DebugWidget.Context.ResetWorldSession(); model.DebugWidget.Context.Reset(); MhwHelper.ResetSession();
            model.IsPreview = false; model.IsGameActive = false;
        }
        public void Dispose() { if (disposed) return; disposed = true; timer.Stop(); timer.Tick -= Tick; StopReader(); }
        sealed class WorldPreview : IDisposable
        {
            public WorldPreview()
            {
                var model = OverlayViewModel.Instance; int index=0;
                foreach (var item in ConfigHelper.MonsterData.Values.Monsters.Take(3))
                {
                    var monster = model.MonsterWidget.Context.UpdateAndGetMonster(0x100000UL+(ulong)index++, item.Key, 32000, 18450, 1.12f, 1);
                    monster.IsSelected = true;
                    var part = item.Value.Parts.FirstOrDefault(p=>!p.IsRemovable);
                    if(part!=null) monster.UpdateAndGetPart(monster.Address+0x1000,false,1800,650,1);
                }
                model.TeamWidget.Context.UpdateAndGetPlayer(0,"Preview Hunter",12500);
                model.TeamWidget.Context.UpdateAndGetPlayer(1,"Hunter 02",9800);
                model.TeamWidget.Context.UpdateFractions();
                model.IsPreview=true; model.IsGameActive=true; ConsoleViewModel.Instance.State="Preview";
            }
            public void Dispose() { }
        }
    }
}
