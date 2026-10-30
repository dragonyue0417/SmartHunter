using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SmartHunter.Core;
using SmartHunter.Game.Config;
using SmartHunter.Game.Data;
using SmartHunter.Game.Data.ViewModels;
using SmartHunter.Game.Data.WidgetContexts;
using SmartHunter.Game.Helpers;
using SmartHunter.Ui.Controls;
using SmartHunter.Ui.Windows;
using SmartHunter.Wilds;
using SmartHunter.World;
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
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

static class DebugOverlayTests
{
internal static void SessionDetails()
    {
        var context = OverlayViewModel.Instance.DebugWidget.Context;
        var data = Sample(); string lobbyHash = new string('b', 64);
        data.NativeSessionCandidates.Add(new WildsNativeSessionCandidate { Path = "NetworkManager+0x90+0x18+0x60", Status = "BufferFingerprintCandidate", Sha256 = lobbyHash });
        data.QuestMembers.NativeTableStatus = "Completed"; data.QuestMembers.NativeMembers.Clear();
        data.QuestMembers.NativeMembers.Add(new WildsNetworkNativeMember { NativeIndex = 0, IsHost = true, IsSelf = false });
        data.QuestMembers.NativeMembers.Add(new WildsNetworkNativeMember { NativeIndex = 1, IsHost = false, IsSelf = true });
        var live = new WildsSnapshot { IsReady = true, StageId = data.StageId };
        try
        {
            context.Reset(); context.SetCollectionEnabled(true); context.Observe(live); context.ApplyNetwork(data);
            Assert(context.WildsDetails.Length == 2 && context.LobbySessionLine.Contains(lobbyHash.Substring(0,12)) && !context.SessionLine.Contains(lobbyHash.Substring(0,12)), "lobby fingerprint is shown separately from the quest session");
            Assert(context.NativeRoleLine.Contains("2") && context.NativeRoleLine.Contains("1"), "native self/host flags display one-based player positions");
            live.IsQuestDamageFrozen = true; context.Observe(live);
            Assert(context.LobbySessionLine.Contains(lobbyHash.Substring(0,12)) && !context.NativeRoleLine.Contains("2"), "ending a quest hides the old role without ending the separate lobby session");
            live.IsLoading = true; context.Observe(live); Assert(!context.LobbySessionLine.Contains(lobbyHash.Substring(0,12)), "loading does not display a retained lobby fingerprint as live");
            live.IsLoading = false; live.IsQuestDamageFrozen = false; data.TimestampUtc = DateTime.UtcNow.AddSeconds(-30); context.ApplyNetwork(data);
            Assert(!context.LobbySessionLine.Contains(lobbyHash.Substring(0,12)), "stale lobby captures are hidden");
            data.TimestampUtc = DateTime.UtcNow; data.NativeSessionCandidates[0].Status = "EmptyBuffer"; data.NativeSessionCandidates[0].Sha256 = null; context.ApplyNetwork(data);
            Assert(!context.LobbySessionLine.Contains(lobbyHash.Substring(0,12)) && !context.LobbySessionLine.Contains(data.QuestSessionCandidate.CurrentCandidateSha256.Substring(0,12)), "empty lobby never falls back to the unrelated quest ID");
            context.SetCollectionEnabled(false); Assert(!context.LobbySessionLine.Contains(lobbyHash.Substring(0,12)), "disabling diagnostics clears lobby evidence");
        }
        finally { context.Reset(); context.SetCollectionEnabled(true); }
    }
    static WildsNetworkCheck Sample()
    {
        var assembly = typeof(DebugOverlayTests).Assembly;
        using (var input = new StreamReader(assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(n => n.EndsWith("UserNetworkSchema4Evidence.json")))))
        {
            var sample = JObject.Parse(input.ReadToEnd())["Sample"].ToObject<WildsNetworkCheck>(); sample.TimestampUtc = DateTime.UtcNow; return sample;
        }
    }
internal static void Transitions()
    {
        var vm = OverlayViewModel.Instance; var context = vm.DebugWidget.Context;
        bool hidden = vm.HideWidgetsRequested, preview = vm.IsPreview, hideInactive = ConfigHelper.Main.Values.Overlay.HideWhenGameWindowIsInactive;
        try
        {
            context.Reset(); context.SetCollectionEnabled(true); vm.IsPreview = false; vm.HideWidgetsRequested = false;
            context.HasGameProcess = true; context.GameHasFocus = true; vm.IsGameActive = false;
            Assert(vm.IsDebugVisible, "loading and safe zones do not suppress diagnostics");
            var snapshot = new WildsSnapshot { IsReady = true, IsLoading = false, StageId = 3 }; context.Observe(snapshot); var check = Sample(); context.ApplyNetwork(check);
            Assert(context.HasCurrentSession && context.SessionLine.Contains(check.QuestSessionCandidate.CurrentCandidateSha256.Substring(0, 12)), "current candidate fingerprint is visible");
            snapshot.IsQuestDamageFrozen = true; context.Observe(snapshot);
            Assert(!context.HasCurrentSession && !context.SessionLine.Contains(check.QuestSessionCandidate.CurrentCandidateSha256.Substring(0, 12)), "live quest completion hides the key before the next ten-second capture");
            snapshot.IsQuestDamageFrozen = false; check.TimestampUtc = DateTime.UtcNow.AddSeconds(-30); context.ApplyNetwork(check); context.Observe(snapshot);
            Assert(!context.HasCurrentSession && !context.SessionLine.Contains(check.QuestSessionCandidate.CurrentCandidateSha256.Substring(0, 12)), "old captures are explicitly stale");
            context.SetCollectionEnabled(false); Assert(!context.HasCurrentSession && context.MemberLine.IndexOf("Net_QuestUserInfo", StringComparison.Ordinal) < 0, "disabled collection clears previous session and member evidence");
            context.SetCollectionEnabled(true); ConfigHelper.Main.Values.Overlay.HideWhenGameWindowIsInactive = true; context.GameHasFocus = false;
            Assert(!vm.IsDebugVisible, "the user's inactive-game preference still applies");
            vm.IsPreview = true; Assert(vm.IsDebugVisible, "sample mode does not require a running game");
            vm.HideWidgetsRequested = true; Assert(!vm.IsDebugVisible, "F1 hides the debug panel together with other widgets");
            vm.IsPreview = false; vm.HideWidgetsRequested = false; context.Reset(); Assert(!vm.IsDebugVisible, "process exit clears the panel");
        }
        finally { context.Reset(); vm.HideWidgetsRequested = hidden; vm.IsPreview = preview; ConfigHelper.Main.Values.Overlay.HideWhenGameWindowIsInactive = hideInactive; }
    }
internal static void RenderSettings()
    {
        var vm = OverlayViewModel.Instance; var context = vm.DebugWidget.Context; var config = ConfigHelper.Main.Values.Overlay.DebugWidget;
        int size = config.FontSize; bool visible = config.IsVisible, oldPreview = vm.IsPreview; string language = ConfigHelper.Main.Values.LocalizationFileName;
        var trace = new StringWriter(); var listener = new TextWriterTraceListener(trace); PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
        DebugWidgetWindow window = null;
        try
        {
            SwitchLanguage("zh-TW.json"); config.FontSize = 16; context.UpdateFromConfig(); context.SetCollectionEnabled(true); context.HasGameProcess = true;
            vm.IsPreview = false; vm.HideWidgetsRequested = false; ConsoleViewModel.Instance.GameVersion = "1.42.0.2"; ConsoleViewModel.Instance.State = "Working";
            var data = Sample(); var live = new WildsSnapshot { IsReady = true, StageId = data.StageId };
            live.Players.AddRange(Enumerable.Range(0, data.ReportedHumanCount).Select(i => new WildsPlayerSnapshot { Slot = i }));
            live.Players.AddRange(Enumerable.Range(0, data.ReportedNpcCount).Select(i => new WildsPlayerSnapshot { Slot = i + data.ReportedHumanCount, IsNpc = true }));
            context.Observe(live); context.ApplyNetwork(data);
            window = new DebugWidgetWindow(); var root = (FrameworkElement)window.Content;
            Render(root, "wilds-debug-overlay-zh-TW.png");
            var card = Visuals<Border>(root).Single(b => b.Name == "DebugCard"); var texts = Visuals<TextBlock>(card).ToArray();
            Assert(card.ActualWidth == 420 && card.ActualHeight > 150 && card.ActualHeight < 560, "panel is compact and fully measured: " + card.ActualWidth + " x " + card.ActualHeight);
            Assert(texts.Any(t => t.Text.Contains("Net_QuestUserInfo")) && texts.Any(t => t.Text.Contains("1905647336")), "real report fields and short fingerprints are rendered");
            Assert(texts.Any(t => t.Text == context.LobbySessionLine) && texts.Any(t => t.Text == context.NativeRoleLine), "lobby and native role details render through the original debug text template");
            Assert(texts.Where(t => t.Text == context.MemberLine || t.Text == context.SessionLine).All(t => t.FontSize == 16 && t.ActualWidth > 0), "important diagnostic text keeps readable default size");
            Assert(((SolidColorBrush)card.Background).Color == Color.FromArgb(153, 0, 0, 0), "debug uses a 60 percent black background");
            Assert(((Color)Application.Current.Resources["A_Color_Widget_Background"]).A == 0 && Visuals<Border>(card).Where(b => b != card).All(b => b.BorderBrush == null && (b.Background == null || ((SolidColorBrush)b.Background).Color.A == 0)), "other widget backgrounds and debug row colors stay unchanged");
            Assert(texts.All(t => ((SolidColorBrush)t.Foreground).Color == Colors.White || ReferenceEquals(t.Foreground, Application.Current.Resources["A_Brush_DropShadow"])), "diagnostic text uses the original white and shared shadow palette");
            Assert(texts.Count(t => ReferenceEquals(t.Foreground, Application.Current.Resources["A_Brush_DropShadow"])) == texts.Length / 2, "each diagnostic line retains the original offset shadow");
            var settings = SettingsViewModel.Instance; var font = settings.Settings.Single(s => s.Key == "LOC_UI_DEBUG_FONT_SIZE");
            font.SelectedOption = font.Options.Single(o => o.Id == "22");
            Assert(context.FontSize == 22 && File.ReadAllText(ConfigHelper.Main.FullPathFileName).Contains("\"FontSize\": 22"), "text size applies and persists without editing JSON");
            Render(root, "wilds-debug-overlay-large.png"); Assert(texts.Where(t => t.Text == context.MemberLine).All(t => t.FontSize == 22), "display bindings update immediately");
            var toggle = settings.Settings.Single(s => s.Key == "LOC_UI_DEBUG_OVERLAY"); toggle.Value = false; Assert(!config.IsVisible, "settings can hide the panel"); toggle.Value = true;
            SwitchLanguage("en-US.json"); config.FontSize = 16; context.UpdateFromConfig(); Render(root, "wilds-debug-overlay-en-US.png");
            Assert(Visuals<TextBlock>(card).Any(t => t.Text.Contains("Diagnostics")), "language switching refreshes the panel immediately");
            listener.Flush(); Assert(trace.ToString().Length == 0, "diagnostic panel binding errors: " + trace);
        }
        finally
        {
            PresentationTraceSources.DataBindingSource.Listeners.Remove(listener); listener.Dispose(); window?.Close();
            config.FontSize = size; config.IsVisible = visible; context.Reset(); vm.IsPreview = oldPreview; SwitchLanguage(language); ConfigHelper.SaveSettings();
        }
    }
    static void SwitchLanguage(string file)
    {
        var settings = SettingsViewModel.Instance;
        settings.SelectedLanguage = settings.Languages.FirstOrDefault(l => l.FileName == LanguageCatalog.ProfileName(file)) ?? new LanguageOption(file, file);
    }
    static void Render(FrameworkElement root, string name)
    {
        root.Measure(new Size(480, 600)); root.Arrange(new Rect(0, 0, 480, 600)); root.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle); root.UpdateLayout();
        var image = new RenderTargetBitmap(480, 600, 96, 96, PixelFormats.Pbgra32); image.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using (var output = File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name))) encoder.Save(output);
    }
}

static class LiveDiagnosticsTests
{
internal static void LiveStatistics()
    {
        long now=0; var lines=new List<string>();
        var stats=new WildsRelayDiagnostics(()=>now, (key,args)=>lines.Add(key));
        stats.Configure(new Uri("http://live.invalid/wilds/v1/sync")); stats.State("Receiving");
        var model=new ConsoleViewModel();
        try
        {
            stats.Record(true,200,"",1024,2048,83); model.Flush();
            Assert(WildsRelayDiagnostics.Current.Requests==1 && WildsRelayDiagnostics.Current.LatestMs==83 && model.RelayStatistics.Contains("1.00 KiB"), "first response immediately updates visible totals and latency");
            stats.Match(new WildsRelayMatchReport { Applied=2, Skipped=1, Approximate=1 }); stats.State("Approximate"); model.Flush();
            Assert(WildsRelayDiagnostics.Current.Approximate==1 && model.RelayStatus.Contains(UiText.Instance["LOC_UI_RELAY_APPROXIMATE"]), "current pairing mode is visible separately from HTTP success");
            stats.Record(false,503,"HTTP",512,64,100); model.Flush();
            Assert(WildsRelayDiagnostics.Current.Requests==2 && WildsRelayDiagnostics.Current.Failures==1 && WildsRelayDiagnostics.Current.AverageMs==91 && model.RelayStatus.Contains(UiText.Instance["LOC_UI_RELAY_LINK_FAILED"]), "next response updates failure state and cumulative average without waiting thirty seconds");
            Assert(!lines.Contains("LOC_LOG_RELAY_SUMMARY"), "live display does not append summary lines for each request");
            stats.Record(true,200,"",1024,4096,80); model.Flush();
            var settings=SettingsViewModel.Instance; var section=settings.SelectedSection; var language=settings.SelectedLanguage;
            var window=new SmartHunter.Ui.Windows.ConsoleWindow();
            try
            {
                settings.SelectedSection=settings.Sections.Single(x=>x.Id=="Logs");
                settings.SelectedLanguage=settings.Languages.Single(x=>x.FileName=="zh-TW.Wilds.json");
                // The production window uses the singleton log model.
                ConsoleViewModel.Instance.Flush();
                TestSupport.Render((FrameworkElement)window.Content,"wilds-live-network-logs-zh-TW.png",1120,780);
            }
            finally { window.Close(); settings.SelectedSection=section; settings.SelectedLanguage=language; }
            stats.State("Disabled"); model.Flush();
            Assert(WildsRelayDiagnostics.Current.Applied==0 && model.RelayStatus.Contains(UiText.Instance["LOC_UI_RELAY_DISABLED"]), "disabled state clears stale applied data");
        }
        finally { model.Dispose(); }
    }
internal static void SnapshotState()
    {
        var s = new WildsSnapshot(); Assert(s.DisplayState == "Retrying", "unready state");
        s.IsReady = true; Assert(s.DisplayState == "SafeZone", "camp hidden by preference");
        s.AllowCampDisplay = true; Assert(s.DisplayState == "Camp", "camp display enabled");
        s.InHuntingZone = true; Assert(s.DisplayState == "Working", "hunting area");
        s.IsQuestDamageFrozen = true; Assert(s.DisplayState == "QuestEnded", "damage freeze is explicit");
        s.IsLoading = true; Assert(s.DisplayState == "Loading" && !s.CanDisplayData, "loading overrides hunting and camp");
    }
internal static void LanguageUpgrade()
    {
        foreach (string language in new[] { "zh-TW.json", "zh-CN.json", "en-US.json" })
        {
            var builtIn = LanguageCatalog.ReadBuiltIn(language);
            var old = new JObject { ["AdditionalMetadata"] = "preserve me", ["Strings"] = new JObject {
                ["LOC_UI_GENERAL"] = "My general", ["LOC_UI_STATE_WORKING"] = "My custom status", ["CUSTOM_KEY"] = "preserve me" } };
            string upgraded = LanguageCatalog.UpgradeBuiltInFile(language, old.ToString());
            var result = JObject.Parse(upgraded);
            Assert((string)result["AdditionalMetadata"] == "preserve me" && (string)result["Strings"]["LOC_UI_STATE_WORKING"] == "My custom status" && (string)result["Strings"]["LOC_CUSTOM_KEY"] == "preserve me", "custom strings and metadata survive");
            Assert(builtIn.All(p => result["Strings"][p.Key] != null), "all new editable keys are present");
            Assert(LanguageCatalog.UpgradeBuiltInFile(language, upgraded) == null, "no rewrite on subsequent loads");
            foreach (var pair in builtIn.Where(p => p.Key.StartsWith("LOC_LOG_RELAY_") || p.Key.StartsWith("LOC_UI_STATE_") || p.Key.StartsWith("LOC_UI_RELAY_") || p.Key.StartsWith("LOC_UI_DEBUG_")))
                String.Format(pair.Value, 0, 1, 2, 3, 4, 5, 6, 7, 8);
        }
        string oldText = "{\"Strings\":{\"LOC_UI_STATE_WORKING\":\"已連接遊戲 · 正在追蹤資訊\",\"LOC_UI_STATE_CAMP\":\"我的營地說明\"}}";
        var updated = JObject.Parse(LanguageCatalog.UpgradeBuiltInFile("zh-TW.json", oldText));
        Assert((string)updated["Strings"]["LOC_UI_STATE_WORKING"] == LanguageCatalog.ReadBuiltIn("zh-TW.json")["LOC_UI_STATE_WORKING"] && (string)updated["Strings"]["LOC_UI_STATE_CAMP"] == "我的營地說明", "exact old stock text changes while custom camp text stays");
        Assert(LanguageCatalog.UpgradeBuiltInFile("my-language.json", oldText) == null, "custom locale files are never rewritten");
    }
    sealed class Entry { public string Key; public object[] Args; }
internal static void Statistics()
    {
        long clock = 0; var lines = new List<Entry>();
        var stats = new WildsRelayDiagnostics(() => clock, (key, values) => lines.Add(new Entry { Key = key, Args = values }));
        stats.Configure(new Uri("http://unit.invalid/wilds/v1/sync"));
        stats.State("Connecting"); stats.State("Connecting");
        stats.Record(true, 200, "PROTOCOL", 2048, 4096, 20);
        stats.State("Receiving");
        for (int i = 0; i < 9; i++) { clock += 1000; stats.Record(true, 200, "PROTOCOL", 2048, 4096, 30); stats.State("Receiving"); }
        Assert(lines.Count(e => e.Key == "LOC_LOG_RELAY_CONNECTED") == 1 && lines.Count(e => e.Key == "LOC_LOG_RELAY_STATE") == 2 && !lines.Any(e => e.Key == "LOC_LOG_RELAY_SUMMARY"), "normal polls do not generate duplicate events");
        stats.Record(false, 503, "HTTP", 100, 0, 50); stats.Record(false, 503, "HTTP", 100, 0, 60);
        Assert(lines.Count(e => e.Key == "LOC_LOG_RELAY_FAILED") == 1, "repeated failures do not flood the log");
        clock = 30000; stats.Record(true, 200, "PROTOCOL", 300, 500, 40);
        Assert(lines.Count(e => e.Key == "LOC_LOG_RELAY_RECONNECTED") == 1, "recovery is distinct from initial connection");
        var summary = lines.Single(e => e.Key == "LOC_LOG_RELAY_SUMMARY").Args;
        Assert((long)summary[1] == 13 && (long)summary[2] == 11 && (long)summary[3] == 2 && (string)summary[4] == "20.49 KiB" && (long)summary[6] == 33 && (long)summary[7] == 40, "sizes, counts and response averages include completed failures");
        stats.Flush(); Assert(lines.Count(e => e.Key == "LOC_LOG_RELAY_SUMMARY") == 1, "no duplicate final summary");
        stats.Configure(null); stats.State("Disabled");
    }
    sealed class Handler : HttpMessageHandler
    {
        public bool Fail, Stall;
        public int RequestBytes, ResponseBytes;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            byte[] body = await request.Content.ReadAsByteArrayAsync(); RequestBytes = body.Length;
            await Task.Delay(30, token);
            if (Fail) return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            if (Stall) return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StalledBody()) };
            var sent = JObject.Parse(Encoding.UTF8.GetString(body));
            var response = new JObject { ["status"] = "ok", ["protocol"] = 1, ["session"] = sent["session"], ["role"] = "guest", ["peers"] = 1, ["hasHost"] = false, ["hasPlayer"] = true, ["monsters"] = null, ["ageMs"] = null, ["note"] = "繁體字測試" };
            byte[] data = Encoding.UTF8.GetBytes(response.ToString()); ResponseBytes = data.Length;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(data) };
        }
    }
    sealed class StalledBody : Stream
    {
        public override bool CanRead => true;
        public override bool CanWrite => false;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token) { await Task.Delay(Timeout.Infinite, token); return 0; }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
internal static void HttpDiagnostics()
    {
        long clock = 0; var lines = new List<Entry>(); var handler = new Handler();
        var config = new RelayConfig { Enabled = true, ServerUrl = "http://unit.invalid", Role = RelayRole.Guest };
        var snapshot = WildsSnapshotApplier.Preview(1); snapshot.QuestIdentity = 123; snapshot.QuestElapsed = 10000;
        Func<WildsSessionIdentity> resolve = () => new WildsSessionIdentity { Session = new string('a', 64), IsHost = false };
        using (var client = new WildsRelayClient(new HttpClient(handler), config, () => clock, (key, values) => lines.Add(new Entry { Key = key, Args = values })))
        {
            Action<string> pump = expected => {
                var watch = Stopwatch.StartNew();
                do { client.Update(snapshot, resolve); if (client.State == expected) return; Thread.Sleep(5); } while (watch.ElapsedMilliseconds < 5000);
                throw new Exception("expected " + expected + ", got " + client.State);
            };
            pump("NoHost");
            var success = lines.Single(e => e.Key == "LOC_LOG_RELAY_CONNECTED").Args;
            Assert((long)success[1] >= 15 && (string)success[2] == handler.RequestBytes + " B" && (string)success[3] == handler.ResponseBytes + " B", "logs measure response time and UTF-8 bytes rather than character count");
            clock += 1000; handler.Fail = true; pump("Offline");
            var failure = lines.Single(e => e.Key == "LOC_LOG_RELAY_FAILED").Args;
            Assert((string)failure[2] == "503" && (long)failure[3] >= 15, "failure records HTTP status and elapsed time");
            clock += 1000; handler.Fail = false; pump("NoHost");
            Assert(lines.Count(e => e.Key == "LOC_LOG_RELAY_RECONNECTED") == 1, "successful recovery is logged");
            clock += 1000; handler.Stall = true; pump("Offline");
            var timedOut = lines.Last(e => e.Key == "LOC_LOG_RELAY_FAILED").Args;
            Assert((string)timedOut[1] == LocalizationHelper.GetString("LOC_LOG_RELAY_REASON_TIMEOUT") && (long)timedOut[3] >= 2900, "a response body that stalls after headers times out and records the reason");
            config.Enabled = false; client.Update(snapshot, resolve);
            var summary = lines.Single(e => e.Key == "LOC_LOG_RELAY_SUMMARY").Args;
            Assert((long)summary[1] == 4 && (long)summary[2] == 2 && (long)summary[3] == 2 && client.State == "Disabled", "disable emits the final summary without another request");
        }
    }
internal static void OverlayFonts()
    {
        var overlay = OverlayViewModel.Instance;
        var config = ConfigHelper.Main.Values.Overlay;
        var settings = SettingsViewModel.Instance;
        var configs = new SmartHunter.Core.Config.WidgetConfig[] { config.MonsterWidget, config.TeamWidget, config.PlayerWidget, config.DebugWidget };
        int[] saved = configs.Select(c => c.FontSize).ToArray();
        float width = config.MonsterWidget.CardWidth, scale = config.MonsterWidget.Scale;
        bool parts = config.MonsterWidget.ShowUnchangedParts, ailments = config.MonsterWidget.ShowUnchangedStatusEffects;
        var applier = new WildsSnapshotApplier();
        var trace = new StringWriter(); var listener = new TextWriterTraceListener(trace);
        PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
        try
        {
            for (int i = 0; i < configs.Length; i++) configs[i].FontSize = 16;
            config.MonsterWidget.ShowUnchangedParts = config.MonsterWidget.ShowUnchangedStatusEffects = true;
            overlay.MonsterWidget.Context.UpdateFromConfig(); overlay.TeamWidget.Context.UpdateFromConfig(); overlay.PlayerWidget.Context.UpdateFromConfig();
            applier.Apply(WildsSnapshotApplier.Preview(2));
            var monster = new ContentControl { DataContext = overlay.MonsterWidget.Context, Content = overlay.MonsterWidget.Context };
            var team = new ContentControl { DataContext = overlay.TeamWidget.Context, Content = overlay.TeamWidget.Context };
            var player = new ContentControl { DataContext = overlay.PlayerWidget.Context, Content = overlay.PlayerWidget.Context };
            var root = new StackPanel { Background = new SolidColorBrush(Color.FromRgb(45, 54, 53)), Width = 1000 };
            root.Children.Add(monster); root.Children.Add(team); root.Children.Add(player);
            Action layout = () => { root.Measure(new Size(1000, Double.PositiveInfinity)); root.Arrange(new Rect(0, 0, 1000, root.DesiredSize.Height)); root.UpdateLayout(); Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle); root.UpdateLayout(); };
            layout();
            var health = Visuals<TextBlock>(monster).First(t => t.Name == "HealthBlock" && t.DataContext is SmartHunter.Game.Data.Monster);
            Assert(health.FontSize == 16 && Visuals<TextBlock>(team).Any(t => t.FontSize == 16) && Visuals<TextBlock>(player).Any(t => t.FontSize == 16), "original default sizes are unchanged");
            Action<string, string> choose = (key, value) => { var setting = settings.Settings.Single(s => s.Key == key); setting.SelectedOption = setting.Options.Single(o => o.Id == value); layout(); };
            choose("LOC_UI_MONSTER_FONT_SIZE", "24");
            Assert(health.FontSize == 24 && Visuals<TextBlock>(monster).Any(t => t.FontSize == 30) && overlay.TeamWidget.Context.FontSize == 16 && overlay.PlayerWidget.Context.FontSize == 16, "monster names and detail text change without changing other widgets");
            Assert(Visuals<TextBlock>(monster).Any(t => t.Name == "NameBlock" && t.DataContext is SmartHunter.Game.Data.MonsterPart && t.FontSize == 24) && Visuals<TextBlock>(monster).Any(t => t.Name == "NameBlock" && t.DataContext is SmartHunter.Game.Data.MonsterStatusEffect && t.FontSize == 24), "part and ailment labels also use the selected size");
            Assert(config.MonsterWidget.CardWidth == width && config.MonsterWidget.Scale == scale && Visuals<StackPanel>(monster).Where(p => p.Name == "Root" && p.DataContext is SmartHunter.Game.Data.Monster).All(p => p.ActualWidth == width), "font adjustment never changes card width or widget scale; before=" + width + "/" + scale + ", after=" + config.MonsterWidget.CardWidth + "/" + config.MonsterWidget.Scale + ", context=" + overlay.MonsterWidget.Context.CardWidth + ", rendered=" + String.Join(",", Visuals<StackPanel>(monster).Where(p => p.Name == "Root" && p.DataContext is SmartHunter.Game.Data.Monster).Select(p => p.ActualWidth + "/Width=" + p.Width + "/Min=" + p.MinWidth)));
            choose("LOC_UI_TEAM_FONT_SIZE", "20"); choose("LOC_UI_PLAYER_FONT_SIZE", "18"); choose("LOC_UI_DEBUG_FONT_SIZE", "24");
            Assert(Visuals<TextBlock>(team).Where(t => !String.IsNullOrWhiteSpace(t.Text)).All(t => t.FontSize == 20) && Visuals<TextBlock>(player).Where(t => !String.IsNullOrWhiteSpace(t.Text)).All(t => t.FontSize == 18) && overlay.DebugWidget.Context.FontSize == 24, "every panel has its independent live setting");
            var persisted = JObject.Parse(File.ReadAllText(ConfigHelper.Main.FullPathFileName))["Overlay"];
            Assert((int)persisted["MonsterWidget"]["FontSize"] == 24 && (int)persisted["TeamWidget"]["FontSize"] == 20 && (int)persisted["PlayerWidget"]["FontSize"] == 18 && (int)persisted["DebugWidget"]["FontSize"] == 24, "all four sizes persist in config");
            TestSupport.Render(root, "wilds-overlay-fonts-large.png", 1000, Math.Max(1, (int)Math.Ceiling(root.ActualHeight)));
            listener.Flush(); Assert(trace.ToString().Length == 0, "font bindings must be valid: " + trace);
        }
        finally
        {
            PresentationTraceSources.DataBindingSource.Listeners.Remove(listener); listener.Dispose(); applier.Clear();
            for (int i = 0; i < configs.Length; i++) configs[i].FontSize = saved[i];
            config.MonsterWidget.ShowUnchangedParts = parts; config.MonsterWidget.ShowUnchangedStatusEffects = ailments;
            overlay.MonsterWidget.Context.UpdateFromConfig(); overlay.TeamWidget.Context.UpdateFromConfig(); overlay.PlayerWidget.Context.UpdateFromConfig(); overlay.DebugWidget.Context.UpdateFromConfig();
            ConfigHelper.SaveSettings();
        }
    }
}

static class WorldSessionDisplayTests
{
internal static void WorldDebug()
    {
        var active=GameProfile.Active;var cfg=ConfigHelper.Main.Values;bool relay=cfg.Relay.Enabled;
        try { GameProfile.Active=GameChoice.World;cfg.Relay.Enabled=false;ConfigHelper.SwitchGameData(GameChoice.World);var ctx=OverlayViewModel.Instance.DebugWidget.Context;
            ctx.UpdateCurrentGame("Hunter","wp11","session-123","RoomHost","quest-456","Hunter",false);ctx.UpdateWorldParty(new[]{"Hunter","Guest"});ctx.RefreshWorld();
            Assert(ctx.WorldDetails.Length==6 && ctx.WorldDetails.Any(s=>s.Contains("session-123")) && ctx.WorldDetails.Any(s=>s.Contains("quest-456")) && ctx.WorldDetails.Any(s=>s.Contains("RoomHost")) && ctx.WorldDetails.Any(s=>s.Contains("Hunter")),"old debug details restored");
            Assert(ctx.WorldDetails.Last().Contains(UiText.Instance["LOC_UI_WEAPON_BOW"]) && ctx.PartyLine.Contains("2") && ctx.MemberLine==UiText.Instance["LOC_UI_DEBUG_WORLD_HOST"],"localized weapon and host");
            ctx.UpdateCurrentGame("Hunter","wp11","session-123","RoomHost","quest-456","",false);Assert(ctx.MemberLine==UiText.Instance["LOC_UI_DEBUG_ROLE_UNKNOWN"],"empty host is unknown");
            var host=new ContentControl{Content=ctx,DataContext=ctx};TestSupport.Render(host,"unified-world-debug.png",480,700);
            Assert(Visuals<TextBlock>(host).Any(t=>t.Text.Contains("session-123")),"detail templates render");
            ctx.ResetWorldSession();Assert(ctx.WorldDetails.Length==0 && ctx.PartyLine.Contains("0"),"detach removes stale session details");
        }finally {cfg.Relay.Enabled=relay;GameProfile.Active=active;ConfigHelper.SwitchGameData(active);}
    }
}
