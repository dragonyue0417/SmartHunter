using Newtonsoft.Json.Linq;
using SmartHunter.Core;
using SmartHunter.Core.Data;
using SmartHunter.Core.Helpers;
using SmartHunter.Game;
using SmartHunter.Game.Config;
using SmartHunter.Game.Data;
using SmartHunter.Game.Data.ViewModels;
using SmartHunter.Game.Data.WidgetContexts;
using SmartHunter.Game.Helpers;
using static TestSupport;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

static class WorldMemoryTests
{

    public static void Register(Action<string, Action> test)
    {
        var active=SmartHunter.World.GameProfile.Active;
        var relay=ConfigHelper.Main.Values.GetRelay(SmartHunter.World.GameChoice.World);
        var serverUrl=relay.ServerUrl;
        relay.ServerUrl="http://unit.invalid/index.php";
        SmartHunter.World.GameProfile.Active=SmartHunter.World.GameChoice.World;
        ConfigHelper.SwitchGameData(SmartHunter.World.GameChoice.World);
        try
        {
            test("World: scalar reads, invalid pointers and UTF-8 strings", MemoryReads);
            test("World: null pointer chains stop before applying offsets", NullPointerChain);
            test("World: wildcard search agrees with an independent oracle", PatternOracle);
            test("World: unread bytes cannot produce pattern matches", UnreadTail);
            test("World: address division retains 64-bit precision", RangeDivision);
            test("World: chunk boundary patterns are retained", ChunkBoundaries);
            test("World: parallel scans retain split boundary matches", ParallelScan);
            test("World: empty and cancelled scans terminate", ScanCancellation);
            test("World: large monster filtering excludes small monsters", LargeMonstersOnly);
            test("World: missing alternate monster name does not stop updates", MissingAlternateName);
            test("World: selected monster is identified by address", SelectedMonster);
            test("World: tracking stops at three large monsters", ThreeMonsterLimit);
            test("World: cyclic status links cannot hang the update loop", CyclicStatuses);
            test("World: progress invalidation and zero denominators", ProgressChanges);
            test("World: equal damage comparison is symmetric", PlayerComparison);
            test("World: departure preserves lobby slot identity", PlayerDeparture);
            test("World: solo hiding restores all players", SoloVisibility);
            test("World: team fractions do not overflow Int32", DamageOverflow);
            test("World: chart padding uses milliseconds", ChartPadding);
            test("World: HTTP reuse, JSON and missing Content-Length", HttpReuse);
            test("World: HTTP errors tolerate absent and throwing handlers", HttpFailures);
            test("World: startup scanning waits for initialization", StartupDelay);
            test("World: an initial scan miss retries and recovers", StartupRetry);
            test("World: partial matches keep updating during background retries", PartialScanRetry);
            test("World: game exit resets scan state and cached addresses", ExitDuringStartup);
            test("World: an unavailable game module is retried", ModuleNotReady);
            test("World: repeated misses back off to a capped retry interval", ScanRetryBackoff);
            test("World: cached pattern matches can skip the full scan", CachedStartup);
        }
        finally { relay.ServerUrl=serverUrl; SmartHunter.World.GameProfile.Active=active; ConfigHelper.SwitchGameData(active); }
    }
    static int passed;
    static readonly Process Self = Process.GetCurrentProcess();
    static BytePattern Pattern(string value) => new BytePattern(new BytePatternConfig("test", value));
    static ulong Address(IntPtr value) => unchecked((ulong)value.ToInt64());
    static void Test(string name, Action run) { run(); passed++; Console.WriteLine("PASS " + name); }

    static void MemoryReads()
    {
        IntPtr data = Marshal.AllocHGlobal(128);
        try
        {
            Marshal.WriteInt64(data, 123456789L);
            Assert(MemoryHelper.Read<long>(Self, Address(data)) == 123456789L, "int64");
            Assert(MemoryHelper.Read<int>(Self, 1) == 0, "stale read buffer");
            byte[] text = Encoding.UTF8.GetBytes("測試abc\0");
            Marshal.Copy(text, 0, data, text.Length);
            Assert(MemoryHelper.ReadString(Self, Address(data), (uint)text.Length) == "測試abc", "UTF8");
            Marshal.Copy(Encoding.UTF8.GetBytes("abcdef"), 0, data, 6);
            Assert(MemoryHelper.ReadString(Self, Address(data), 6) == "abcdef", "unterminated string");
            Assert(MemoryHelper.ReadString(Self, 1, 6) == "", "invalid string");
        }
        finally { Marshal.FreeHGlobal(data); }
    }

    static void NullPointerChain()
    {
        IntPtr data = Marshal.AllocHGlobal(16);
        try
        {
            Marshal.WriteInt64(data, 0);
            Assert(MemoryHelper.ReadMultiLevelPointer(false, Self, Address(data), 32, 64) == 0, "null plus offset");
            Marshal.WriteInt64(data, data.ToInt64());
            Assert(MemoryHelper.ReadMultiLevelPointer(false, Self, Address(data), 8) == Address(data) + 8, "valid offset");
        }
        finally { Marshal.FreeHGlobal(data); }
    }

    static void PatternOracle()
    {
        var random = new Random(1234);
        for (int attempt = 0; attempt < 2000; attempt++)
        {
            var bytes = new byte[random.Next(1, 60)];
            for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)random.Next(4);
            var pattern = Pattern(string.Join(" ", Enumerable.Range(0, random.Next(1, 8))
                .Select(_ => random.Next(3) == 0 ? "??" : random.Next(4).ToString("X2"))));
            var expected = new List<int>();
            for (int i = 0; i <= bytes.Length - pattern.Bytes.Length; i++)
                if (pattern.Bytes.Select((b, j) => !b.HasValue || b.Value == bytes[i + j]).All(v => v)) expected.Add(i);
            Assert(expected.SequenceEqual(MemoryHelper.FindPatternMatchIndices(bytes, pattern, false)), "wildcard overlap");
            Assert(expected.Take(1).SequenceEqual(MemoryHelper.FindPatternMatchIndices(bytes, pattern, true)), "first match");
        }
    }

    static void UnreadTail()
    {
        Assert(MemoryHelper.FindPatternMatchIndices(new byte[] { 1, 2, 3, 4 }, Pattern("03 04"), false, 2).Count == 0, "stale tail");
        Assert(MemoryHelper.FindPatternMatchIndices(new byte[0], Pattern("??"), false).Count == 0, "empty input");
    }

    static void RangeDivision()
    {
        var range = new AddressRange(0x140000000, 0x100000003);
        var parts = MemoryHelper.DivideAddressRange(range, 7);
        Assert(parts.First().Start == range.Start && parts.Last().End == range.End, "endpoints");
        for (int i = 1; i < parts.Count; i++) Assert(parts[i - 1].End == parts[i].Start, "gap");
        Assert(MemoryHelper.DivideAddressRange(new AddressRange(1, 2), 8).Count == 2, "zero-size divisions");
    }

    static void ChunkBoundaries()
    {
        int size = 2 * 1024 * 1024 + 128;
        IntPtr data = Marshal.AllocHGlobal(size);
        var bytes = new byte[size];
        byte[] signature = { 0xde, 0xad, 0xbe, 0xef, 0x7f };
        int[] offsets = { 1024 * 1024 - 2, 2 * 1024 * 1024 - 2 };
        foreach (int offset in offsets) Array.Copy(signature, 0, bytes, offset, signature.Length);
        Marshal.Copy(bytes, 0, data, size);
        try
        {
            var matches = MemoryHelper.FindPatternAddresses(Self, new AddressRange(Address(data), (ulong)size), Pattern("DE AD ?? EF 7F"), false);
            Assert(matches.SequenceEqual(offsets.Select(i => Address(data) + (ulong)i)), "chunk boundary");
        }
        finally { Marshal.FreeHGlobal(data); }
    }

    static void ParallelScan()
    {
        IntPtr data = Marshal.AllocHGlobal(128);
        var bytes = new byte[128];
        byte[] signature = { 0xde, 0xad, 0xbe, 0xef };
        Array.Copy(signature, 0, bytes, 30, 4);
        Array.Copy(signature, 0, bytes, 94, 4);
        Marshal.Copy(bytes, 0, data, bytes.Length);
        try
        {
            for (int i = 0; i < 15; i++)
            {
                var pattern = Pattern("DE AD BE EF");
                var scan = new ThreadedMemoryScan(Self, pattern, new AddressRange(Address(data), 128), false, 4);
                Assert(SpinWait.SpinUntil(() => scan.HasCompleted, 5000), "lost completion");
                Assert(pattern.MatchedAddresses.OrderBy(a => a).SequenceEqual(new[] { Address(data) + 30, Address(data) + 94 }), "split boundary or lost result");
            }
        }
        finally { Marshal.FreeHGlobal(data); }
    }

    static void ScanCancellation()
    {
        var empty = new ThreadedMemoryScan(Self, Pattern("01"), new AddressRange(1, 0), false, 2);
        Assert(empty.HasCompleted, "empty scan");
        using (var token = new CancellationTokenSource())
        {
            token.Cancel();
            bool cancelled = false;
            try { MemoryHelper.FindPatternAddresses(Self, new AddressRange(1, 100), Pattern("01"), false, token.Token); }
            catch (OperationCanceledException) { cancelled = true; }
            Assert(cancelled, "cancellation ignored");
        }
    }

    static void LargeMonstersOnly()
    {
        var config = ConfigHelper.Main.Values.Overlay.MonsterWidget;
        string previous = config.IncludeMonsterIdRegex;
        config.IncludeMonsterIdRegex = ".*";
        Assert(Monster.IsIncluded("em001_00"), "large monster hidden");
        Assert(!Monster.IsIncluded("ems001_00") && !Monster.IsIncluded(null), "small monster accepted");
        Assert(MhwHelper.MaxLargeMonsterCount == 3, "maximum");
        config.IncludeMonsterIdRegex = previous;
    }

    static void WithMonster(Action<IntPtr> run)
    {
        IntPtr allocation = Marshal.AllocHGlobal(0x1d040);
        IntPtr entity = IntPtr.Add(allocation, 0x40);
        IntPtr health = Marshal.AllocHGlobal(128);
        IntPtr model = Marshal.AllocHGlobal(64);
        Marshal.Copy(new byte[0x1d040], 0, allocation, 0x1d040);
        Marshal.Copy(new byte[128], 0, health, 128);
        Marshal.Copy(new byte[64], 0, model, 64);
        Marshal.Copy(Encoding.UTF8.GetBytes("em001_00\0"), 0, IntPtr.Add(model, 12), 9);
        Marshal.WriteInt64(entity, 0x7670, health.ToInt64());
        Marshal.WriteInt64(entity, 0x2a0, model.ToInt64());
        Marshal.Copy(BitConverter.GetBytes(100f), 0, IntPtr.Add(health, 0x60), 4);
        Marshal.Copy(BitConverter.GetBytes(80f), 0, IntPtr.Add(health, 0x64), 4);
        try { run(entity); }
        finally { Marshal.FreeHGlobal(allocation); Marshal.FreeHGlobal(health); Marshal.FreeHGlobal(model); }
    }

    static void MissingAlternateName()
    {
        var context = OverlayViewModel.Instance.MonsterWidget.Context;
        context.Monsters.Clear();
        WithMonster(entity => {
            MhwHelper.UpdateMonsterWidget(Self, Address(entity), 0);
            Assert(context.Monsters.Count == 1 && context.Monsters[0].Id == "em001_00", "alternate null name");
        });
        context.Monsters.Clear();
    }

    static void SelectedMonster()
    {
        var context = OverlayViewModel.Instance.MonsterWidget.Context;
        context.Monsters.Clear();
        IntPtr monsterTable = Marshal.AllocHGlobal(512);
        Marshal.Copy(new byte[512], 0, monsterTable, 512);
        try
        {
            WithMonster(entity => {
                context.UpdateAndGetMonster(Address(entity) + 1, "em001_00", 100, 90, 1, 1);
                foreach (int offset in new[] { 0x128, 0x130, 0x160 }) Marshal.WriteInt64(monsterTable, offset, 1);
                Marshal.WriteInt64(monsterTable, 0x148, entity.ToInt64());
                MhwHelper.UpdateMonsterWidget(Self, Address(entity), Address(monsterTable));
                Assert(context.Monsters.Count == 1 && context.Monsters[0].Address == Address(entity), "same-species selection");
            });
        }
        finally { Marshal.FreeHGlobal(monsterTable); context.Monsters.Clear(); }
    }

    static void ProgressChanges()
    {
        var progress = new Progress(100, 50);
        var changed = new List<string>();
        progress.PropertyChanged += (s, e) => changed.Add(e.PropertyName);
        progress.Max = 200;
        Assert(progress.Fraction == .25f && changed.Contains("Fraction") && changed.Contains("Angle"), "max invalidation");
        Assert(new Progress(0, 0).Fraction == 0, "zero denominator");
    }

    static void ThreeMonsterLimit()
    {
        var context = OverlayViewModel.Instance.MonsterWidget.Context;
        context.Monsters.Clear();
        WithMonster(a => WithMonster(b => WithMonster(c => WithMonster(d => {
            Marshal.WriteInt64(IntPtr.Add(a, -0x30), b.ToInt64() - 0x40);
            Marshal.WriteInt64(IntPtr.Add(b, -0x30), c.ToInt64() - 0x40);
            Marshal.WriteInt64(IntPtr.Add(c, -0x30), d.ToInt64() - 0x40);
            MhwHelper.UpdateMonsterWidget(Self, Address(a), 0);
            Assert(context.Monsters.Count == 3 && context.Monsters.All(m => m.Address != Address(d)), "fourth monster tracked");
        }))));
        context.Monsters.Clear();
    }

    static void CyclicStatuses()
    {
        IntPtr root = Marshal.AllocHGlobal(0x6000);
        Marshal.Copy(new byte[0x6000], 0, root, 0x6000);
        try
        {
            WithMonster(entity => {
                Marshal.WriteInt64(entity, 0x78, root.ToInt64());
                IntPtr node = IntPtr.Add(root, 0x5800);
                Marshal.WriteInt64(root, 0x57a8, node.ToInt64());
                Marshal.WriteInt64(node, 0x10, node.ToInt64());
                Marshal.WriteInt64(node, 0x40 + 0x18, node.ToInt64() + 0x40);
                var clock = Stopwatch.StartNew();
                MhwHelper.UpdateMonsterWidget(Self, Address(entity), 0);
                Assert(clock.ElapsedMilliseconds < 1000, "cyclic chain did not terminate promptly");
            });
        }
        finally { Marshal.FreeHGlobal(root); OverlayViewModel.Instance.MonsterWidget.Context.Monsters.Clear(); }
    }

    static void PlayerComparison()
    {
        var a = new Player { Damage = 50 };
        var b = new Player { Damage = 50 };
        Assert(a.CompareTo(b) == 0 && b.CompareTo(a) == 0 && a.CompareTo(null) > 0, "sort contract");
    }

    static TeamWidgetContext Team()
    {
        OverlayViewModel.Instance.DebugWidget.Context.CurrentGame.IsPlayerInExpedition = false;
        return new TeamWidgetContext();
    }

    static void PlayerDeparture()
    {
        var team = Team();
        team.UpdateAndGetPlayer(0, "A", 10);
        team.UpdateAndGetPlayer(1, "B", 20);
        team.UpdateAndGetPlayer(2, "C", 30);
        team.UpdateAndGetPlayer(1, "", 0);
        Assert(team.Players.Count == 2 && team.Players[1].Index == 2, "slot shifted");
        Assert(team.UpdateAndGetPlayer(2, "C", 35).Damage == 35, "wrong player updated");
        team.UpdateAndGetPlayer(1, "D", 5);
        Assert(team.Players.Count == 3 && team.Players[2].Name == "C", "slot rejoin");
    }

    static void SoloVisibility()
    {
        var team = Team();
        team.DontShowIfAlone = true;
        team.UpdateAndGetPlayer(0, "A", 10);
        Assert(team.Fake_Players.Count == 0, "solo visible");
        team.UpdateAndGetPlayer(2, "B", 20);
        Assert(team.Fake_Players.Count == 2, "first player missing");
        team.UpdateAndGetPlayer(2, "", 0);
        Assert(team.Fake_Players.Count == 0, "solo removal");
        team.DontShowIfAlone = false;
        Assert(team.Fake_Players.Count == 1, "config toggle");
    }

    static void DamageOverflow()
    {
        var team = Team();
        team.UpdateAndGetPlayer(0, "A", int.MaxValue);
        team.UpdateAndGetPlayer(1, "B", int.MaxValue);
        team.UpdateFractions();
        Assert(team.Players.All(p => p.DamageFraction == .5f && p.BarFraction == 1), "damage overflow");
    }

    static void ChartPadding()
    {
        var team = Team();
        var player = team.UpdateAndGetPlayer(0, "A", 10);
        player.DamagePoints.Last().TimeStamp = DateTime.Now.ToFileTime() - TimeSpan.TicksPerMillisecond * 100;
        int count = player.DamagePoints.Count;
        team.PadDamagePoints(10000);
        Assert(player.DamagePoints.Count == count, "wrong time units");
    }

    sealed class Handler : HttpMessageHandler
    {
        public int Calls;
        public bool Throw;
        public HttpStatusCode Status = HttpStatusCode.OK;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++;
            var payload = JObject.Parse(await request.Content.ReadAsStringAsync());
            Assert((string)payload["host"] == "true" && (string)payload["damage"] == "0", "legacy JSON types");
            if (Throw) throw new HttpRequestException("test failure");
            return new HttpResponseMessage(Status) { Content = new UnknownLengthContent() };
        }
    }
    sealed class UnknownLengthContent : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext context)
        {
            byte[] bytes = Encoding.UTF8.GetBytes("{\"status\":\"ok\",\"result\":\"value\"}");
            return stream.WriteAsync(bytes, 0, bytes.Length);
        }
    }

    static void HttpReuse()
    {
        var handler = new Handler();
        using (var client = new HttpClient(handler))
        {
            var server = new ServerManager(client);
            int callbacks = 0;
            for (int i = 0; i < 2; i++)
                server.RequestCommandAsync(ServerManager.Command.ALIVE, "key", null, true, 0, null,
                    (r, ping) => { Assert((string)r["status"] == "ok", "response"); callbacks++; }).GetAwaiter().GetResult();
            Assert(handler.Calls == 2 && callbacks == 2, "client reuse");
        }
    }

    static void HttpFailures()
    {
        var handler = new Handler { Throw = true };
        using (var client = new HttpClient(handler))
        {
            var server = new ServerManager(client);
            server.RequestCommandAsync(ServerManager.Command.ALIVE, "key", null, true, 0, null).GetAwaiter().GetResult();
            server.RequestCommandAsync(ServerManager.Command.ALIVE, "key", null, true, 0, null,
                onError: e => { throw new Exception("bad error callback"); }).GetAwaiter().GetResult();
            handler.Throw = false;
            handler.Status = HttpStatusCode.InternalServerError;
            bool received = false;
            server.RequestCommandAsync(ServerManager.Command.ALIVE, "key", null, true, 0, null,
                (r, ping) => received = r == null).GetAwaiter().GetResult();
            Assert(received, "HTTP failure callback");
        }
    }

    static long BaselineRead(IntPtr address)
    {
        byte[] bytes = new byte[8];
        UIntPtr read;
        WindowsApi.ReadProcessMemory(Self.Handle, address, bytes, (UIntPtr)8, out read);
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try { return (long)Marshal.PtrToStructure(handle.AddrOfPinnedObject(), typeof(long)); }
        finally { handle.Free(); }
    }

    sealed class StartupUpdater : MemoryUpdater, IDisposable
    {
        public readonly BytePattern[] TestPatterns = { Pattern("FA CE 10 01"), Pattern("FA CE 10 02") };
        public long Now;
        public bool SimulateExit;
        public bool ModuleReady = true;
        public int Updates;
        readonly AddressRange range;
        public StartupUpdater(IntPtr start) : base(false) { range = new AddressRange(Address(start), 128); }
        protected override string ProcessName { get { return Self.ProcessName; } }
        protected override BytePattern[] Patterns { get { return TestPatterns ?? new BytePattern[0]; } }
        protected override long ScanClockMilliseconds { get { return Now; } }
        protected override Process FindProcess() { return Process.GetProcessById(Self.Id); }
        protected override AddressRange GetScanAddressRange() { return ModuleReady ? range : null; }
        protected override bool HasProcessExited() { return SimulateExit || base.HasProcessExited(); }
        protected override void UpdateMemory() { Updates++; }
        public void Step() { UpdateStateMachine(); }
        public new void Dispose() { SimulateExit = true; Step(); base.Dispose(); }
    }

    static void StartupFixture(Action<StartupUpdater, IntPtr> run)
    {
        IntPtr data = Marshal.AllocHGlobal(128);
        Marshal.Copy(new byte[128], 0, data, 128);
        try
        {
            using (var updater = new StartupUpdater(data))
            {
                updater.Step(); updater.Step(); updater.Step();
                Assert(updater.CurrentState == "ProcessFound", "did not attach to process");
                run(updater, data);
            }
        }
        finally { Marshal.FreeHGlobal(data); }
    }

    static void WriteStartupPatterns(IntPtr start, bool second = true)
    {
        Marshal.Copy(new byte[] { 0xfa, 0xce, 0x10, 0x01 }, 0, IntPtr.Add(start, 16), 4);
        if (second) Marshal.Copy(new byte[] { 0xfa, 0xce, 0x10, 0x02 }, 0, IntPtr.Add(start, 32), 4);
    }

    static void Pump(StartupUpdater updater, Func<bool> complete)
    {
        var clock = Stopwatch.StartNew();
        while (!complete() && clock.ElapsedMilliseconds < 5000)
        {
            updater.Step();
            Thread.Sleep(1);
        }
        Assert(complete(), "state did not converge: " + updater.CurrentState);
    }

    static void StartupDelay()
    {
        StartupFixture((updater, start) => {
            WriteStartupPatterns(start);
            updater.Now = 9999;
            updater.Step();
            Assert(updater.CurrentState == "ProcessFound" && updater.TestPatterns.All(p => p.MatchedAddresses.Count == 0), "scan started before warmup");
            updater.Now = 10000;
            Pump(updater, () => updater.CurrentState == "Working");
            Assert(updater.TestPatterns.All(p => p.MatchedAddresses.Count == 1), "ready patterns not found");
        });
    }

    static void StartupRetry()
    {
        StartupFixture((updater, start) => {
            updater.Now = 10000;
            Pump(updater, () => updater.CurrentState == "WaitingForScanRetry");
            WriteStartupPatterns(start);
            updater.Now = 14999;
            updater.Step();
            Assert(updater.CurrentState == "WaitingForScanRetry", "retry loop is too fast");
            updater.Now = 15000;
            Pump(updater, () => updater.CurrentState == "Working");
            Assert(updater.TestPatterns.All(p => p.MatchedAddresses.Count == 1), "failed scan never recovered");
        });
    }

    static void PartialScanRetry()
    {
        StartupFixture((updater, start) => {
            WriteStartupPatterns(start, false);
            updater.Now = 10000;
            Pump(updater, () => updater.CurrentState == "Working");
            Assert(updater.TestPatterns[1].MatchedAddresses.Count == 0, "fixture must initially miss second pattern");
            int previous = updater.Updates;
            updater.Now = 15000;
            WriteStartupPatterns(start);
            updater.Step();
            Assert(updater.CurrentState == "Working" && updater.Updates > previous, "background retry froze active updates");
            Pump(updater, () => updater.TestPatterns[1].MatchedAddresses.Count == 1);
            Assert(updater.TestPatterns[0].MatchedAddresses.Single() == Address(start) + 16, "existing matches were lost");
        });
    }

    static void ExitDuringStartup()
    {
        StartupFixture((updater, start) => {
            updater.TestPatterns[0].MatchedAddresses.Add(Address(start));
            updater.SimulateExit = true;
            updater.Step();
            Assert(updater.CurrentState == "WaitingForProcess" && updater.TestPatterns.All(p => p.MatchedAddresses.Count == 0), "stale process addresses retained");
            updater.SimulateExit = false;
            updater.Now = 25000;
            updater.Step();
            Assert(updater.CurrentState == "ProcessFound", "could not attach after exit");
            updater.Now = 34999;
            updater.Step();
            Assert(updater.CurrentState == "ProcessFound", "relaunch skipped new warmup");
        });
    }

    static void ModuleNotReady()
    {
        StartupFixture((updater, start) => {
            updater.ModuleReady = false;
            updater.Now = 10000;
            updater.Step();
            Assert(updater.CurrentState == "ProcessFound", "module failure became fatal");
            updater.ModuleReady = true;
            WriteStartupPatterns(start);
            updater.Now = 15000;
            Pump(updater, () => updater.CurrentState == "Working");
        });
    }

    static void ScanRetryBackoff()
    {
        StartupFixture((updater, start) => {
            updater.Now = 10000;
            foreach (long delay in new long[] { 5000, 5000, 5000, 10000, 20000, 30000, 30000 })
            {
                Pump(updater, () => updater.CurrentState == "WaitingForScanRetry");
                updater.Now += delay - 1;
                updater.Step();
                Assert(updater.CurrentState == "WaitingForScanRetry", "retry backoff was bypassed");
                updater.Now++;
                updater.Step();
                Assert(updater.CurrentState == "FastPatternScanning", "retry deadline was missed");
            }
        });
    }

    static void CachedStartup()
    {
        StartupFixture((updater, start) => {
            WriteStartupPatterns(start);
            updater.TestPatterns[0].Config.LastResultAddress = (Address(start) + 16).ToString("X");
            updater.TestPatterns[1].Config.LastResultAddress = (Address(start) + 32).ToString("X");
            updater.Now = 10000;
            Pump(updater, () => updater.CurrentState == "Working");
            Assert(updater.TestPatterns.All(p => p.MatchedAddresses.Count == 1), "cache-only startup failed");
        });
    }

    static void Benchmark()
    {
        IntPtr data = Marshal.AllocHGlobal(8);
        try
        {
            Marshal.WriteInt64(data, 1234);
            Action baseline = () => BaselineRead(data);
            Action optimized = () => MemoryHelper.Read<long>(Self, Address(data));
            foreach (var entry in new[] { new { Name = "baseline", Run = baseline }, new { Name = "optimized", Run = optimized } })
            {
                for (int i = 0; i < 1000; i++) entry.Run();
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                int before = GC.CollectionCount(0);
                var clock = Stopwatch.StartNew();
                for (int i = 0; i < 100000; i++) entry.Run();
                clock.Stop();
                Console.WriteLine($"BENCHMARK {entry.Name}: 100000 int64 reads {clock.Elapsed.TotalMilliseconds:F2}ms, Gen0 collections {GC.CollectionCount(0) - before}");
            }
        }
        finally { Marshal.FreeHGlobal(data); }
    }
}
