using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using SmartHunter.Core;
using SmartHunter.Game.Data.ViewModels;
using SmartHunter.Game.Helpers;
using SmartHunter.Wilds;
using Newtonsoft.Json;

namespace SmartHunter.Game
{
    public sealed class WildsMemoryUpdater : IDisposable
    {
        readonly DispatcherTimer timer;
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly CancellationTokenSource shutdown = new CancellationTokenSource();
        readonly WildsSnapshotApplier apply = new WildsSnapshotApplier();
        readonly WildsRelayClient relay = new WildsRelayClient();
        Process process;
        WildsProcessMemory memory;
        WildsReader reader;
        WildsScanReport scanReport;
        WildsScanSummary previousSuccessfulScan;
        WildsNetworkProbe networkProbe;
        WildsNetworkReport networkReport;
        WildsMantleDiagnostics mantleReport;
        long nextMantleDiagnostic;
        long nextNetworkDiagnostic;
        string lastNetworkState;
        bool busy, disposed, preview;
        long readyAt, nextPoll;
        string lastError = "";
        string announcedUnknownVersion;
        int misses;
        long lastDiagnosticAt;
        string lastDiagnosticState;
        public WildsMemoryUpdater()
        {
            previousSuccessfulScan = WildsScanHistory.Read(FileContainer.GetFullPathFileName("Scan.Wilds.json"));
            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            timer.Tick += Tick; timer.Start();
            ConsoleViewModel.Instance.State = "WaitingForProcess";
        }

        async void Tick(object sender, EventArgs args)
        {
            if (disposed || busy || clock.ElapsedMilliseconds < nextPoll) return;
            nextPoll = clock.ElapsedMilliseconds + 1000 / Math.Max(2, Math.Min(20, ConfigHelper.Main.Values.Overlay.UpdatesPerSecond));
            busy = true;
            try
            {
                bool wantsPreview = ConfigHelper.Main.Values.Debug.UseSampleData;
                var debug = OverlayViewModel.Instance.DebugWidget.Context;
                debug.SetCollectionEnabled(ConfigHelper.Main.Values.Debug.CollectNetworkDiagnostics);
                if (wantsPreview != preview) { apply.Clear(); debug.ClearPreview(); relay.Reset(); preview = wantsPreview; }
                OverlayViewModel.Instance.IsPreview = preview;
                if (preview) { var sample = WildsSnapshotApplier.Preview(); apply.Apply(sample); debug.ShowPreview(sample); OverlayViewModel.Instance.IsGameActive = true; ConsoleViewModel.Instance.State = "Preview"; nextPoll += 400; return; }
                if (process != null && process.HasExited)
                {
                    Log.WriteLine("Monster Hunter Wilds exited."); Detach();
                    if (ConfigHelper.Main.Values.ShutdownWhenProcessExits) { Application.Current.Shutdown(); return; }
                }
                if (process == null)
                {
                    var found = Process.GetProcessesByName("MonsterHunterWilds");
                    process = found.FirstOrDefault(); foreach (var p in found.Skip(1)) p.Dispose();
                    if (process == null) { ConsoleViewModel.Instance.State = "WaitingForProcess"; nextPoll += 900; return; }
                    debug.HasGameProcess = true;
                    readyAt = clock.ElapsedMilliseconds + Math.Max(0, ConfigHelper.Memory.Values.InitialScanDelaySeconds) * 1000L;
                    Log.WriteLine("Monster Hunter Wilds found; waiting for initialization.");
                }
                debug.GameHasFocus = WindowsApi.GetForegroundWindow() == process.MainWindowHandle;
                if (clock.ElapsedMilliseconds < readyAt) { ConsoleViewModel.Instance.State = "ProcessFound"; return; }
                if (reader == null)
                {
                    var module = process.MainModule;
                    var info = module.FileVersionInfo;
                    string version = $"{info.FileMajorPart}.{info.FileMinorPart}.{info.FileBuildPart}.{info.FilePrivatePart}";
                    ConsoleViewModel.Instance.GameVersion = version;
                    if (memory == null) memory = new WildsProcessMemory(process.Id);
                    ulong moduleBase = unchecked((ulong)module.BaseAddress.ToInt64());
                    int moduleSize = module.ModuleMemorySize;
                    ConsoleViewModel.Instance.State = "PatternScanning";
                    if (!WildsPatternScanner.SupportsLayout(version) && announcedUnknownVersion != version)
                    {
                        Log.WriteLine(String.Format(UiText.Instance["LOC_LOG_SCAN_UNKNOWN_VERSION"], version));
                        announcedUnknownVersion = version;
                    }
                    var scanMemory = memory;
                    var scan = await Task.Run(() => WildsPatternScanner.Resolve(scanMemory, version, moduleBase, moduleSize, shutdown.Token));
                    if (disposed) return;
                    scanReport = scan.Report; scanReport.PreviousSuccessfulScan = previousSuccessfulScan;
                    if (scan.Addresses != null) previousSuccessfulScan = WildsScanSummary.From(scanReport);
                    SaveDiagnostics();
                    if (scan.Addresses == null)
                    {
                        string error = "Wilds signature scan rejected; waiting to retry: " + scan.Report.Reason;
                        if (error != lastError) Log.WriteLine(lastError = error);
                        ConsoleViewModel.Instance.State = "WaitingForScanRetry"; nextPoll = clock.ElapsedMilliseconds + 10000; return;
                    }
                    if (WildsPatternScanner.SupportsLayout(version))
                        Log.WriteLine(String.Format(UiText.Instance["LOC_LOG_SCAN_RESOLVED"], version, scan.Report.ElapsedMilliseconds));
                    else
                    {
                        Log.WriteLine(String.Format(UiText.Instance["LOC_LOG_SCAN_UNKNOWN_RESOLVED"], version, scan.Report.ElapsedMilliseconds, scan.Report.LayoutVersion));
                        if (ConfigHelper.Main.Values.Relay.Enabled) Log.WriteLine(UiText.Instance["LOC_LOG_SCAN_UNKNOWN_RELAY"]);
                    }
                    reader = new WildsReader(memory, scan.Addresses, moduleBase, moduleSize: moduleSize);
                    networkProbe = new WildsNetworkProbe(memory, scan.Addresses, moduleBase, moduleSize: moduleSize);
                    networkReport = new WildsNetworkReport { GameVersion = version };
                    mantleReport = new WildsMantleDiagnostics { GameVersion = version };
                }
                bool selectedOnly = ConfigHelper.Main.Values.Overlay.MonsterWidget.ShowOnlySelectedMonster && !ConfigHelper.Main.Values.Relay.Enabled;
                bool prioritizeSelected = ConfigHelper.Main.Values.Overlay.MonsterWidget.PrioritizeSelectedMonster;
                bool showInCamp = ConfigHelper.Main.Values.Overlay.MonsterWidget.ShowInCamp;
                bool captureMantles = ConfigHelper.Main.Values.Debug.CollectMantleDiagnostics && clock.ElapsedMilliseconds >= nextMantleDiagnostic;
                if (captureMantles) nextMantleDiagnostic = clock.ElapsedMilliseconds + 2000;
                var snapshot = await Task.Run(() => reader.Read(selectedOnly, prioritizeSelected, showInCamp, captureMantles));
                if (disposed) return;
                if (ConfigHelper.Main.Values.Debug.UseSampleData) return; // a pending read cannot overwrite preview
                debug.Observe(snapshot);
                RecordSnapshot(snapshot);
                if (snapshot.Mantles != null && ConfigHelper.Main.Values.Debug.CollectMantleDiagnostics)
                {
                    try
                    {
                        mantleReport.Add(snapshot.Mantles);
                        var capturedMantles = mantleReport;
                        await Task.Run(() => capturedMantles.Save(FileContainer.GetFullPathFileName("Equipment.Wilds.json")));
                    }
                    catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { Log.WriteLine("Mantle diagnostics skipped: " + error.Message); }
                    if (disposed) return;
                }
                string networkState = $"{snapshot.StageId}/{snapshot.QuestSuccessState}/{snapshot.QuestFailureState}/{snapshot.IsQuestDamageFrozen}/{snapshot.Players.Count}";
                if (snapshot.IsReady && ConfigHelper.Main.Values.Debug.CollectNetworkDiagnostics && (clock.ElapsedMilliseconds >= nextNetworkDiagnostic || lastNetworkState != networkState))
                {
                    lastNetworkState = networkState;
                    nextNetworkDiagnostic = clock.ElapsedMilliseconds + 10000;
                    try
                    {
                        var check = await Task.Run(() => networkProbe.Capture(snapshot));
                        if (disposed) return;
                        if (ConfigHelper.Main.Values.Debug.UseSampleData || !ConfigHelper.Main.Values.Debug.CollectNetworkDiagnostics) { debug.SetCollectionEnabled(ConfigHelper.Main.Values.Debug.CollectNetworkDiagnostics); return; }
                        check.RelayState = relay.State; check.RelayMatch = relay.MatchReport; check.RelayConnection = relay.ConnectionReport; check.ReadStatistics=reader.LastReadStatistics;
                        debug.ApplyNetwork(check);
                        if (networkReport.Checks.Count >= 96) networkReport.Checks.RemoveAt(0);
                        networkReport.Checks.Add(check);
                        var capturedReport = networkReport;
                        await Task.Run(() =>
                        {
                            WildsDiagnosticsFile.Write(FileContainer.GetFullPathFileName("Network.Wilds.json"),capturedReport);
                        });
                    }
                    catch (Exception error) { debug.SetError(error.Message); Log.WriteLine("Network diagnostics skipped: " + error.Message); }
                }
                var sessionProbe = networkProbe;
                relay.Update(snapshot, () => sessionProbe.ResolveSession(snapshot));
                debug.SetRelayState(relay.State, relay.Peers, relay.Applied, relay.Skipped);
                if (ConfigHelper.Main.Values.Relay.Enabled && ConfigHelper.Main.Values.Overlay.MonsterWidget.ShowOnlySelectedMonster)
                    snapshot.Monsters = snapshot.Monsters.Where(m => m.Selected).ToList();
                apply.Apply(snapshot);
                OverlayViewModel.Instance.IsGameActive = snapshot.CanDisplayData &&
                    (!ConfigHelper.Main.Values.Overlay.HideWhenGameWindowIsInactive || WindowsApi.GetForegroundWindow() == process.MainWindowHandle);
                ConsoleViewModel.Instance.State = snapshot.DisplayState;
                if (!snapshot.IsReady)
                {
                    int initial = Math.Max(1, ConfigHelper.Memory.Values.ScanRetryIntervalSeconds);
                    int maximum = Math.Max(initial, ConfigHelper.Memory.Values.ScanMaxRetryIntervalSeconds);
                    nextPoll = clock.ElapsedMilliseconds + Math.Min(maximum, initial * (1L << Math.Min(6, misses++))) * 1000;
                }
                else misses = 0;
                lastError = "";
            }
            catch (Exception error)
            {
                if (!disposed)
                {
                    if (lastError != error.Message) { Log.WriteException(error); lastError = error.Message; }
                    Detach(); ConsoleViewModel.Instance.State = error is System.ComponentModel.Win32Exception || error is UnauthorizedAccessException ? "AccessDenied" : "Retrying";
                    nextPoll = clock.ElapsedMilliseconds + 5000;
                }
            }
            finally { busy = false; }
        }
        void SaveDiagnostics()
        {
            if (scanReport == null) return;
            try { File.WriteAllText(FileContainer.GetFullPathFileName("Scan.Wilds.json"), JsonConvert.SerializeObject(scanReport, Formatting.Indented)); }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { Log.WriteLine("Could not write scan diagnostics: " + error.Message); }
        }
        void RecordSnapshot(WildsSnapshot snapshot)
        {
            if (scanReport == null) return;
            var check = WildsRuntimeCheck.From(snapshot);
            string state = $"{check.IsReady}/{check.InHuntingZone}/{check.StageId}/{check.MonsterCount}/{check.SelectedCount}/{check.PartCount}/{check.AilmentCount}/{check.PartyCount}";
            long elapsed = clock.ElapsedMilliseconds - lastDiagnosticAt;
            if (scanReport.RuntimeChecks.Count != 0 && (elapsed < 5000 || (state == lastDiagnosticState && elapsed < 30000))) return;
            if (scanReport.RuntimeChecks.Count >= 12) scanReport.RuntimeChecks.RemoveAt(1); // retain initialization + recent checks
            scanReport.RuntimeChecks.Add(check); lastDiagnosticState = state; lastDiagnosticAt = clock.ElapsedMilliseconds; SaveDiagnostics();
        }
        void Detach()
        {
            announcedUnknownVersion = null;
            relay.Reset();
            OverlayViewModel.Instance.DebugWidget.Context.Reset();
            apply.Clear(); reader = null; scanReport = null; networkProbe = null; networkReport = null; nextNetworkDiagnostic = 0; lastNetworkState = null; lastDiagnosticState = null; lastDiagnosticAt = 0; memory?.Dispose(); memory = null; process?.Dispose(); process = null;
            mantleReport = null; nextMantleDiagnostic = 0;
        }
        public void Dispose() { disposed = true; shutdown.Cancel(); timer.Stop(); timer.Tick -= Tick; Detach(); relay.Dispose(); }
    }
}
