using System;
using System.Collections.Generic;
using System.Windows.Threading;
using SmartHunter.Core;
using SmartHunter.Wilds;
using SmartHunter.Core.Data;
using SmartHunter.Game.Helpers;

namespace SmartHunter.Game.Data.ViewModels
{
    public sealed class ConsoleViewModel : Bindable, IDisposable
    {
        public static ConsoleViewModel Instance { get; } = new ConsoleViewModel();
        const int MaxLines = 2000;
        const int MaxLineLength = 2048;
        readonly Queue<string> m_Pending = new Queue<string>();
        readonly Queue<string> m_Lines = new Queue<string>();
        readonly DispatcherTimer m_Timer;
        string m_Output = "";
        public string Output { get => m_Output; private set => SetProperty(ref m_Output, value); }
        bool m_AutoScroll = true;
        public bool AutoScroll { get => m_AutoScroll; set => SetProperty(ref m_AutoScroll, value); }
        string m_State = "None";
        public string State
        {
            get => m_State;
            set { if (SetProperty(ref m_State, value)) NotifyPropertyChanged(nameof(Status)); }
        }
        public string Status => UiText.Instance["LOC_UI_STATE_" + (State=="WaitingForProcess" && ConfigHelper.Main.Values.Game==SmartHunter.World.GameChoice.Auto ? "WaitingForAnyGame" : State)];
        string m_GameVersion = "";
        public string GameVersion { get => m_GameVersion; set { if (SetProperty(ref m_GameVersion, value)) NotifyPropertyChanged(nameof(GameVersionLabel)); } }
        public string GameVersionLabel => String.IsNullOrEmpty(GameVersion) ? UiText.Instance["LOC_UI_VERSION_AUTO"] : (SmartHunter.World.GameProfile.Active == SmartHunter.World.GameChoice.World ? UiText.Instance["LOC_UI_GAME_WORLD"] : UiText.Instance["LOC_UI_GAME_WILDS"] + " v" + GameVersion);
        volatile WildsRelayLive pendingRelay = WildsRelayDiagnostics.Current;
        WildsRelayLive relay = WildsRelayDiagnostics.Current;
        void ReceiveRelay(WildsRelayLive value) { pendingRelay = value; }
        public string RelayStatus => String.Format(UiText.Instance["LOC_UI_RELAY_LIVE_STATUS"],
            UiText.Instance["LOC_UI_RELAY_" + (relay.State ?? "Disabled")],
            UiText.Instance[relay.Connected == true ? "LOC_UI_RELAY_LINK_OK" : relay.Connected == false ? "LOC_UI_RELAY_LINK_FAILED" : "LOC_UI_RELAY_LINK_IDLE"]);
        public string RelayStatistics => String.Format(UiText.Instance["LOC_LOG_RELAY_SUMMARY"],
            String.IsNullOrEmpty(relay.Server) ? "—" : relay.Server, relay.Requests, relay.Successes, relay.Failures,
            WildsRelayDiagnostics.Size(relay.Uploaded), WildsRelayDiagnostics.Size(relay.Downloaded), relay.AverageMs, relay.LatestMs);
        public string RelayApplication => String.Format(UiText.Instance["LOC_UI_RELAY_LIVE_APPLIED"], relay.Applied, relay.Skipped, relay.Approximate,
            relay.LastRequestUtc.HasValue ? relay.LastRequestUtc.Value.ToLocalTime().ToString("HH:mm:ss") : "—");
        void RefreshRelay()
        {
            NotifyPropertyChanged(nameof(RelayStatus)); NotifyPropertyChanged(nameof(RelayStatistics)); NotifyPropertyChanged(nameof(RelayApplication));
        }
        public ConsoleViewModel()
        {
            Log.LineReceived += Log_LineReceived;
            WildsRelayDiagnostics.LiveChanged += ReceiveRelay;
            ConfigHelper.Localization.Loaded += LanguageLoaded;
            m_Timer = new DispatcherTimer(TimeSpan.FromMilliseconds(150), DispatcherPriority.Background,
                (s, e) => Flush(), Dispatcher.CurrentDispatcher);
            m_Timer.Start();
        }
        void LanguageLoaded(object s, EventArgs e) { RefreshRelay(); NotifyPropertyChanged(nameof(Status)); NotifyPropertyChanged(nameof(GameVersionLabel)); }
        void Log_LineReceived(object s, GenericEventArgs<string> e) => AddLine(e.Value);
        internal void AddLine(string value)
        {
            lock (m_Pending)
            {
                foreach (var line in (value ?? "").Replace("\r", "").Split('\n'))
                {
                    m_Pending.Enqueue(line.Length > MaxLineLength ? line.Substring(0, MaxLineLength) + "…" : line);
                    while (m_Pending.Count > MaxLines) m_Pending.Dequeue();
                }
            }
        }
        internal void Flush()
        {
            if (!Object.ReferenceEquals(relay, pendingRelay)) { relay = pendingRelay; RefreshRelay(); }
            lock (m_Pending)
            {
                if (m_Pending.Count == 0) return;
                while (m_Pending.Count > 0) m_Lines.Enqueue(m_Pending.Dequeue());
            }
            while (m_Lines.Count > MaxLines) m_Lines.Dequeue();
            Output = String.Join("\r\n", m_Lines) + "\r\n";
        }
        public void Clear()
        {
            lock (m_Pending) m_Pending.Clear();
            m_Lines.Clear(); Output = "";
        }
        public void Dispose()
        {
            m_Timer.Stop(); Log.LineReceived -= Log_LineReceived;
            WildsRelayDiagnostics.LiveChanged -= ReceiveRelay;
            ConfigHelper.Localization.Loaded -= LanguageLoaded;
        }
    }
}
