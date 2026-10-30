using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using SmartHunter.Core.Helpers;
using SmartHunter.Game.Data.ViewModels;
using SmartHunter.Game.Helpers;
using ConfigHelper = SmartHunter.World.WorldConfigHelper;

namespace SmartHunter.Core
{
    public abstract class MemoryUpdater : IDisposable
    {
        enum State
        {
            None,
            CheckingForUpdates,
            DownloadingUpdates,
            Restarting,
            WaitingForProcess,
            StartMHW,
            ProcessFound,
            FastPatternScanning,
            PatternScanning,
            WaitingForScanRetry,
            ServerChecking,
            Working
        }

        StateMachine<State> m_StateMachine;
        List<ThreadedMemoryScan> m_MemoryScans;
        List<ThreadedMemoryScan> m_FastMemoryScans;
        DispatcherTimer m_DispatcherTimer;
        long m_NextScanMilliseconds;
        int m_ScanAttempts;
        AddressRange m_ScanAddressRange;
        bool m_BackgroundScan;
        int m_LastSavedPatternCount;

        protected abstract string ProcessName { get; }
        protected abstract BytePattern[] Patterns { get; }
        protected virtual string UserDataPath { get { return @"C:\Program Files (x86)\Steam\userdata"; } }
        protected virtual int ThreadsPerScan { get { return 2; } }
        protected virtual int UpdatesPerSecond { get { return 20; } }
        protected virtual bool ShutdownWhenProcessExits { get { return false; } }
        protected virtual bool BackupWhenProcessExits { get { return false; } }
        protected virtual bool StartMHWWhenSmartHunterStart { get { return false; } }
        protected virtual int InitialScanDelaySeconds { get { return 10; } }
        protected virtual int ScanRetryIntervalSeconds { get { return 5; } }
        protected virtual int ScanMaxRetryIntervalSeconds { get { return 30; } }
        protected virtual long ScanClockMilliseconds { get { return (long)(Stopwatch.GetTimestamp() * 1000d / Stopwatch.Frequency); } }
        internal string CurrentState { get { return m_StateMachine.State.ToString(); } }

        protected Process Process { get; private set; }

        public MemoryUpdater() : this(true) { }

        protected MemoryUpdater(bool startTimer)
        {
            CreateStateMachine();

            Initialize();

            m_DispatcherTimer = new DispatcherTimer();
            m_DispatcherTimer.Tick += Update;
            TryUpdateTimerInterval();
            if (startTimer) m_DispatcherTimer.Start();
        }

        public void Dispose()
        {
            m_DispatcherTimer.Stop();
            m_DispatcherTimer.Tick -= Update;
            Initialize();
            ServerManager.Instance.Invalidate();
        }
        void CreateStateMachine()
        {

            m_StateMachine = new StateMachine<State>();

            m_StateMachine.Add(State.None, new StateMachine<State>.StateData(
                null,
                new StateMachine<State>.Transition[]
                {
                    new StateMachine<State>.Transition(
                        State.StartMHW,
                        () => !ConfigHelper.Main.Values.Overlay.MonsterWidget.UseNetworkServer,
                        () =>
                        {
                            Initialize();
                        }),
                    new StateMachine<State>.Transition(
                        State.ServerChecking,
                        () => ConfigHelper.Main.Values.Overlay.MonsterWidget.UseNetworkServer,
                        () =>
                        {
                            Log.WriteLine("Checking Server...");
                            ServerManager.Instance.RequestCommadWithHandler(ServerManager.Command.ALIVE, null, null, false, 0, null, (result, ping) =>
                            {
                                if (result != null)
                                {
                                    if (result["status"].ToString().Equals("ok"))
                                    {
                                        Log.WriteLine($"Server is online with response time of {ping} ms");
                                        ServerManager.Instance.IsServerOline = 1;
                                    }
                                    else
                                    {
                                        if (result["result"].ToString().Equals("v"))
                                        {
                                            Log.WriteLine("You are using an outdated version of SmartHunter please update it to use this feature");
                                        }
                                        else if (result["result"].ToString().Equals("dev"))
                                        {
                                            Log.WriteLine("Server is under maintenance, a new version should be available soon");
                                        }
                                        else
                                        {
                                            Log.WriteLine("An internal server error has occured, please restart the application if you want to use this function (The overlay will work fine even withouth the server)");
                                        }
                                        ServerManager.Instance.IsServerOline = -1;
                                    }
                                }
                                else
                                {
                                    Log.WriteLine("Server appears to be offline. Please check your connection (The overlay will work fine even withouth the server)");
                                    ServerManager.Instance.IsServerOline = -1;
                                }
                                ServerManager.Instance.ResetStats();
                            }, (error) =>
                            {
                                Log.WriteLine("An error has occured while performing the request, please restart the application if you want to use this function (The overlay will work fine even withouth the server)");
                                ServerManager.Instance.IsServerOline = -1;
                                ServerManager.Instance.ResetStats();
                            });
                        })
                }));

            m_StateMachine.Add(State.ServerChecking, new StateMachine<State>.StateData(
                null,
                new StateMachine<State>.Transition[]
                {
                    new StateMachine<State>.Transition(
                        State.StartMHW,
                        () => ServerManager.Instance.IsServerOline != 0,
                        () =>
                        {
                            Initialize();
                        })
                }));

            m_StateMachine.Add(State.StartMHW, new StateMachine<State>.StateData(
                null,
                new StateMachine<State>.Transition[]
                {
                    new StateMachine<State>.Transition(
                        State.WaitingForProcess,
                        () =>
                        {
                            if(CheckProcess())
                                return true;
                            if(!StartMHWWhenSmartHunterStart)
                            {
                                Log.WriteLine("Start MHW When SmartHunter Start = false");
                                return true;
                            }
                            Log.WriteLine("Start MHW.");
                            try
                            {
                                Process.Start(new ProcessStartInfo
                                {
                                    FileName =  "steam://run/582010",
                                    Arguments = "",
                                    UseShellExecute = true
                                });
                                return true;
                            }
                            catch(Exception ex)
                            {
                                Log.WriteException(ex);
                                return false;
                            }
                        },
                        null)
                }));

            m_StateMachine.Add(State.WaitingForProcess, new StateMachine<State>.StateData(
                null, new[]
                {
                    new StateMachine<State>.Transition(State.ProcessFound, CheckProcess, () =>
                    {
                        m_ScanAttempts = 0;
                        int delay = Math.Max(0, InitialScanDelaySeconds);
                        m_NextScanMilliseconds = ScanClockMilliseconds + (long)delay * 1000;
                        Log.WriteLine($"Game process found; waiting {delay}s for initialization before scanning.");
                    })
                }));

            m_StateMachine.Add(State.ProcessFound, new StateMachine<State>.StateData(
                null, new[]
                {
                    ProcessExitedTransition(),
                    new StateMachine<State>.Transition(State.FastPatternScanning, ReadyForScan, BeginFastScan)
                }));

            m_StateMachine.Add(State.FastPatternScanning, new StateMachine<State>.StateData(
                null, new[]
                {
                    ProcessExitedTransition(),
                    new StateMachine<State>.Transition(State.PatternScanning,
                        () => m_FastMemoryScans.All(scan => scan.HasCompleted), BeginFullScan)
                }));

            m_StateMachine.Add(State.PatternScanning, new StateMachine<State>.StateData(
                null, new[]
                {
                    ProcessExitedTransition(),
                    new StateMachine<State>.Transition(State.Working,
                        () => m_MemoryScans.All(scan => scan.HasCompleted) && Patterns.Any(p => p.MatchedAddresses.Count > 0),
                        FinishScanAttempt),
                    new StateMachine<State>.Transition(State.WaitingForScanRetry,
                        () => m_MemoryScans.All(scan => scan.HasCompleted), FinishScanAttempt)
                }));

            m_StateMachine.Add(State.WaitingForScanRetry, new StateMachine<State>.StateData(
                null, new[]
                {
                    ProcessExitedTransition(),
                    new StateMachine<State>.Transition(State.FastPatternScanning, ReadyForScan, BeginFastScan)
                }));

            m_StateMachine.Add(State.Working, new StateMachine<State>.StateData(
                () =>
                {
                    try
                    {
                        UpdateMemory();
                        UpdateMissingPatterns();
                    }
                    catch (Exception ex) { Log.WriteException(ex); }
                },
                new[] { ProcessExitedTransition() }));
        }

        protected virtual bool HasProcessExited()
        {
            try { return Process == null || Process.HasExited; }
            catch (InvalidOperationException) { return true; }
        }

        StateMachine<State>.Transition ProcessExitedTransition()
        {
            return new StateMachine<State>.Transition(State.WaitingForProcess, HasProcessExited, () => Initialize(true));
        }

        protected virtual AddressRange GetScanAddressRange()
        {
            Process.Refresh();
            var module = Process.MainModule;
            if (module == null || module.ModuleMemorySize <= 0) return null;
            return new AddressRange((ulong)module.BaseAddress.ToInt64(), (ulong)module.ModuleMemorySize);
        }

        bool ReadyForScan()
        {
            if (ScanClockMilliseconds < m_NextScanMilliseconds) return false;
            try
            {
                m_ScanAddressRange = GetScanAddressRange();
                if (m_ScanAddressRange != null && m_ScanAddressRange.Size > 0) return true;
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                Log.WriteLine($"Game module is not readable yet: {ex.Message}");
            }
            catch (InvalidOperationException) { }
            Log.WriteLine("Waiting for the game module to become available.");
            ScheduleRetry();
            return false;
        }

        void BeginFastScan()
        {
            m_ScanAttempts++;
            m_FastMemoryScans.Clear();
            m_MemoryScans.Clear();
            foreach (var pattern in Patterns.Where(p => p.MatchedAddresses.Count == 0))
            {
                long address;
                if (!string.IsNullOrEmpty(pattern.Config.LastResultAddress) &&
                    MhwHelper.TryParseHex(pattern.Config.LastResultAddress, out address))
                {
                    m_FastMemoryScans.Add(new ThreadedMemoryScan(Process, pattern,
                        new AddressRange((ulong)address, (ulong)pattern.Bytes.Length), true, ThreadsPerScan));
                }
            }
        }

        void BeginFullScan()
        {
            m_MemoryScans.Clear();
            var missing = Patterns.Where(p => p.MatchedAddresses.Count == 0).ToArray();
            if (missing.Length == 0) return;
            Log.WriteLine($"Scan attempt {m_ScanAttempts}: {missing.Length} missing patterns; " +
                $"Base: 0x{m_ScanAddressRange.Start:X}, End: 0x{m_ScanAddressRange.End:X}, Size: 0x{m_ScanAddressRange.Size:X}");
            foreach (var pattern in missing)
                m_MemoryScans.Add(new ThreadedMemoryScan(Process, pattern, m_ScanAddressRange, true, ThreadsPerScan));
        }

        void ScheduleRetry()
        {
            int minimum = Math.Max(1, ScanRetryIntervalSeconds);
            int maximum = Math.Max(minimum, ScanMaxRetryIntervalSeconds);
            double delay = Math.Min(maximum, minimum * Math.Pow(2, Math.Min(10, Math.Max(0, m_ScanAttempts - 3))));
            m_NextScanMilliseconds = ScanClockMilliseconds + (long)(delay * 1000);
        }

        void FinishScanAttempt()
        {
            var matches = Patterns.SelectMany(p => p.MatchedAddresses).OrderBy(address => address).ToArray();
            int foundPatternCount = Patterns.Count(p => p.MatchedAddresses.Count > 0);
            if (foundPatternCount > m_LastSavedPatternCount)
            {
                ConfigHelper.Memory.Save(false);
                m_LastSavedPatternCount = foundPatternCount;
                Log.WriteLine($"Match Range: {matches.First():X} - {matches.Last():X}");
            }
            var missing = Patterns.Where(p => p.MatchedAddresses.Count == 0).ToArray();
            if (missing.Length == 0)
            {
                Log.WriteLine($"All {Patterns.Length} patterns found; scanning complete.");
                return;
            }
            ScheduleRetry();
            long seconds = Math.Max(1, (m_NextScanMilliseconds - ScanClockMilliseconds) / 1000);
            Log.WriteLine($"Patterns not ready [{missing.Length}/{Patterns.Length}]: " +
                string.Join(" ", missing.Select(p => p.Config.Name)) +
                $". Retrying in {seconds}s; keep the game running.");
            if (m_ScanAttempts == 6)
                Log.WriteLine("Repeated misses can also indicate changed game signatures or insufficient process access; retries will continue with backoff.");
        }

        void UpdateMissingPatterns()
        {
            if (m_BackgroundScan)
            {
                if (!m_MemoryScans.All(scan => scan.HasCompleted)) return;
                m_BackgroundScan = false;
                FinishScanAttempt();
            }
            if (Patterns.All(p => p.MatchedAddresses.Count > 0) || !ReadyForScan()) return;
            m_ScanAttempts++;
            BeginFullScan();
            m_BackgroundScan = true;
        }

        protected void UpdateStateMachine()
        {
            m_StateMachine.Update();
            ConsoleViewModel.Instance.State = CurrentState;
        }

        private void Initialize(bool processExited = false)
        {
            var previousProcess = Process;
            Process = null;

            if (m_MemoryScans != null)
            {
                foreach (var memoryScan in m_MemoryScans)
                {
                    memoryScan.TryCancel();
                }
            }
            if (m_FastMemoryScans != null)
                foreach (var memoryScan in m_FastMemoryScans) memoryScan.TryCancel();
            previousProcess?.Dispose();
            foreach (var pattern in Patterns)
                if (pattern != null) pattern.MatchedAddresses.Clear();
            m_BackgroundScan = false;
            m_ScanAttempts = 0;
            m_NextScanMilliseconds = 0;
            m_LastSavedPatternCount = 0;

            m_FastMemoryScans = new List<ThreadedMemoryScan>();
            m_MemoryScans = new List<ThreadedMemoryScan>();

            OverlayViewModel.Instance.IsGameActive = false;
            if (processExited && BackupWhenProcessExits)
            {
                try
                {
                    string zipPath = @"UserDataBackup\";
                    string fileName = DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss") + ".zip";
                    string filepath = Path.Combine(zipPath, fileName);
                    if (!Directory.Exists(zipPath))
                        Directory.CreateDirectory(zipPath);
                    if (File.Exists(filepath))
                        File.Delete(filepath);
                    if (Directory.Exists(UserDataPath))
                    {
                        ZipFile.CreateFromDirectory(UserDataPath, filepath);
                        Log.WriteLine("MonsterHunterWorld process exits. Start backup 'UserData'.");
                    }
                    else
                    {
                        Log.WriteLine("Backup fail 'UserData' path not exist.");
                    }
                }
                catch (Exception ex)
                {
                    Log.WriteException(ex);
                }
            }
            if (processExited && ShutdownWhenProcessExits)
            {
                Log.WriteLine("Process exited. Shutting down");
                Application.Current.Shutdown();
            }
        }

        private void Update(object sender, EventArgs e)
        {
            try
            {
                UpdateStateMachine();
            }
            catch (Exception ex)
            {
                m_DispatcherTimer.IsEnabled = false;
                Log.WriteException(ex);
            }
        }

        abstract protected void UpdateMemory();

        protected void TryUpdateTimerInterval()
        {
            const int max = 60;
            const int min = 1;
            int clampedUpdatesPerSecond = Math.Min(Math.Max(UpdatesPerSecond, min), max); // TODO: Dynamic updates per second number based on game perfomance

            int targetMilliseconds = (int)(1000f / clampedUpdatesPerSecond);
            if (m_DispatcherTimer != null && m_DispatcherTimer.Interval.TotalMilliseconds != targetMilliseconds)
            {
                m_DispatcherTimer.Interval = new TimeSpan(0, 0, 0, 0, targetMilliseconds);
            }
        }

        private bool CheckProcess()
        {
            if (Process != null && !HasProcessExited()) return true;
            Process = FindProcess();
            return Process != null;
        }

        protected virtual Process FindProcess()
        {
            var processes = Process.GetProcessesByName(ProcessName);
            foreach (var p in processes)
            {
                try
                {
                    if (p != null && !p.HasExited)
                    {
                        foreach (var other in processes)
                            if (!ReferenceEquals(other, p)) other.Dispose();
                        return p;
                    }
                }
                catch
                {
                    // nothing here
                }
                p?.Dispose();
            }
            return null;
        }
    }
}
