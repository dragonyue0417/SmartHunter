using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SmartHunter.Core;
using SmartHunter.Game.Config;
using SmartHunter.Game.Data.ViewModels;
using SmartHunter.Game.Helpers;
using SmartHunter.Wilds;
using SmartHunter.World;
using static TestSupport;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

static class RelayTests
{
    static readonly string Session = new string('a', 64);
    static WildsSnapshot Snapshot()
    {
        var snapshot = WildsSnapshotApplier.Preview(2); snapshot.QuestIdentity = 99; snapshot.QuestElapsed = 10000;
        for (int i = 0; i < snapshot.Monsters.Count; i++) snapshot.Monsters[i].TargetKey = 100 + i;
        return snapshot;
    }
internal static void Merge()
    {
        var source = Snapshot(); source.Monsters[0].Parts[0].Health = 100; source.Monsters[0].Ailments[0].Buildup = 250;
        var request = WildsRelayData.Create(source, Session, new string('b', 32), true, true);
        var local = Snapshot(); local.Monsters[0].Health = 1234; local.Monsters[0].Ailments[0].Remaining = 12; local.Monsters[0].Ailments[0].MaxDuration = 30;
        int damage = local.Players[0].Damage;
        Assert(WildsRelayData.Merge(local, request.Monsters, false, out int skipped) == 2 && skipped == 0, "unique monsters can share parts");
        Assert(local.Monsters[0].Parts[0].Health == 100 && local.Monsters[0].Health == 1234 && local.Players[0].Damage == damage, "only part values changed");
        int key = local.Monsters[0].TargetKey; local.Monsters[0].TargetKey = 0; local.Monsters[0].Parts[0].Health = 888;
        WildsRelayData.Merge(local, request.Monsters, false, out skipped);
        Assert(skipped == 1 && local.Monsters[0].Parts[0].Health == 888, "missing instance keys never fall back to matching by species and size");
        local.Monsters[0].TargetKey = key; request.Monsters[0].TargetKey += 5000;
        WildsRelayData.Merge(local, request.Monsters, false, out skipped);
        Assert(skipped == 0 && local.Monsters[0].Parts[0].Health == 100, "process-local target keys can differ between paired clients"); request.Monsters[0].TargetKey -= 5000;
        Assert(local.Monsters[0].Ailments[0].Buildup == 180 && local.Monsters[0].Ailments[0].Remaining == 12, "ailment sync defaults off and timers never change");
        WildsRelayData.Merge(local, request.Monsters, true, out skipped);
        Assert(local.Monsters[0].Ailments[0].Buildup == 250 && local.Monsters[0].Ailments[0].Remaining == 12, "optional buildup does not overwrite active duration fields");
        local.Monsters[0].Ailments[0].Active = true; local.Monsters[0].Ailments[0].Buildup = 10;
        WildsRelayData.Merge(local, request.Monsters, true, out skipped); Assert(local.Monsters[0].Ailments[0].Buildup == 10, "active local state is preserved");
        source.Monsters[0].Ailments[0].Active = true;
        Assert(!WildsRelayData.Create(source, Session, "id", true, true).Monsters[0].Ailments.Any(a => a.Active), "active states are not published");
        var clone = new WildsMonsterSnapshot { Address = 8888, TargetKey = local.Monsters[0].TargetKey + 10000, Id = local.Monsters[0].Id, Size = local.Monsters[0].Size, Role = local.Monsters[0].Role, Legendary = local.Monsters[0].Legendary, MaxHealth = local.Monsters[0].MaxHealth };
        local.Monsters.Add(clone); local.Monsters[0].Parts[0].Health = 500;
        WildsRelayData.Merge(local, request.Monsters, true, out skipped);
        Assert(skipped == 1 && local.Monsters[0].Parts[0].Health == 100, "duplicate descriptors try one-to-one pairing without reusing a source");
        local.Monsters.Remove(clone); request.Monsters.Add(request.Monsters[0]);
        WildsRelayData.Merge(local, request.Monsters, true, out skipped); Assert(skipped == 1, "ambiguous source instances are also rejected");
        request.Monsters.RemoveAt(request.Monsters.Count - 1); request.Monsters[0].Parts[0].Health = float.NaN;
        Assert(WildsRelayData.Merge(local, request.Monsters, true, out skipped) == 0 && local.Monsters[0].Health == 1234, "invalid received floats cannot alter the projection");
        var wire = JObject.FromObject(WildsRelayData.Create(Snapshot(), Session, "id", true, true));
        Assert(wire["Monsters"][0]["Health"] == null && wire["Players"] == null && wire["Monsters"][0]["Address"] == null && wire["Monsters"][0]["Ailments"][0]["Remaining"] == null, "wire excludes health, players, pointers and active timers");
    }
internal static void Approximate()
    {
        var local = Snapshot(); var first = local.Monsters[0];
        var second = new WildsMonsterSnapshot { Id = first.Id, Size = first.Size, Role = first.Role, Legendary = first.Legendary, MaxHealth = first.MaxHealth, TargetKey = 777, Address = 88888 };
        local.Monsters.Clear(); local.Monsters.Add(first); local.Monsters.Add(second);
        var source = WildsRelayData.Create(local, Session, "id", true, false).Monsters;
        source[0].TargetKey = 2055; source[1].TargetKey = 3191;
        source[1].Parts = source[0].Parts.Select(p => new WildsRelayPart { Slot=p.Slot, NameId=p.NameId, Severable=p.Severable, Health=42, MaxHealth=p.MaxHealth, Count=3 }).ToList();
        source[0].Parts[0].Health = 123;
        var pairing = new WildsRelayPairing();
        Assert(WildsRelayData.Merge(local, source, false, out int skipped, out var report, pairing) == 2 && report.Approximate == 2 && skipped == 0, "identical descriptors attempt both pairings and mark them approximate");
        local.Monsters.Reverse(); source.Reverse();
        WildsRelayData.Merge(local, source, false, out skipped, out report, pairing);
        Assert(first.Parts[0].Health == 123 && second.Parts[0].Health == 42 && report.Pairs.Select(p => p.SourceKey).Distinct().Count() == 2, "refresh order cannot exchange established pairs or reuse source");
        second.TargetKey++; second.Address++;
        WildsRelayData.Merge(local, source, false, out skipped, out report, pairing);
        Assert(skipped == 1 && report.BindingConflict == 1, "old source cannot rebind to a new wave");
        pairing.Clear();
        Assert(WildsRelayData.Merge(local, source, false, out skipped, out report, pairing) == 2, "new session clears old bindings");
    }
    sealed class Handler : HttpMessageHandler
    {
        public int Calls;
        public JObject Last;
        public Func<JObject, JObject> Respond;
        public TaskCompletionSource<bool> Gate;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage message, CancellationToken token)
        {
            Interlocked.Increment(ref Calls); Last = JObject.Parse(await message.Content.ReadAsStringAsync());
            if (Gate != null) await Gate.Task;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Respond(Last).ToString(Formatting.None), Encoding.UTF8, "application/json") };
        }
    }
    static JObject Response(JObject request, string role = "guest") => new JObject { ["status"] = "ok", ["protocol"] = 1, ["session"] = request["session"], ["role"] = role, ["peers"] = 2, ["hasHost"] = true, ["hasPlayer"] = true, ["ageMs"] = 0, ["monsters"] = role == "host" ? (bool?)request["ackOnly"] == true ? null : request["monsters"] : JArray.FromObject(WildsRelayData.Create(Snapshot(), Session, "id", true, false).Monsters) };
    static void WaitExchange(WildsRelayClient client, WildsSnapshot snapshot, Func<WildsSessionIdentity> resolve, int expectedCalls, Handler handler)
    {
        var watch = Stopwatch.StartNew();
        do
        {
            client.Update(snapshot, resolve);
            if (handler.Calls == expectedCalls && client.Peers == expectedCalls && client.ConnectionReport.HttpStatus == 200) return;
            Thread.Sleep(1);
        } while (watch.ElapsedMilliseconds < 2000);
        throw new Exception("HTTP exchange did not complete");
    }
internal static void FastPolling()
    {
        long clock = 0; int reads = 0;
        var config = new RelayConfig { Enabled = true, ServerUrl = "http://unit.invalid", Role = RelayRole.Auto };
        var handler = new Handler(); int replies = 0; var snapshot = Snapshot();
        handler.Respond = r => { var result = Response(r, "host"); result["peers"] = ++replies; return result; };
        Func<WildsSessionIdentity> resolve = () => { reads++; return new WildsSessionIdentity { Session = Session, IsHost = true, HostIndex = 0 }; };
        using (var client = new WildsRelayClient(new HttpClient(handler), config, () => clock))
        {
            WaitExchange(client, snapshot, resolve, 1, handler);
            Assert((bool)handler.Last["ackOnly"] && client.State == "Providing" && client.ConnectionReport.SourceMonsters == 0, "publisher requests ACK and accepts an empty monster response");
            for (int i = 1; i <= 4; i++)
            {
                clock = i * 250 - 1; client.Update(snapshot, resolve);
                Assert(handler.Calls == i, "polling never exceeds its rate cap");
                clock++;
                var expected = i + 1;
                WaitExchange(client, snapshot, resolve, expected, handler);
                Assert(client.State == "Providing", "publisher stays ready after each acknowledgement");
            }
            Assert(reads == 2 && handler.Calls == 5, "five transfers in one second use two boundary identity reads");
            snapshot.IsLoading = true; client.Update(snapshot, resolve);
            snapshot.IsLoading = false;
            WaitExchange(client, snapshot, resolve, 6, handler);
            Assert(reads == 3, "a loading transition invalidates the cached identity immediately");
        }
    }
internal static void RetryPolling()
    {
        long clock = 0;
        var config = new RelayConfig { Enabled = true, ServerUrl = "http://unit.invalid" };
        var handler = new Handler { Respond = r => throw new HttpRequestException("test outage") }; var snapshot = Snapshot();
        Func<WildsSessionIdentity> resolve = () => new WildsSessionIdentity { Session = Session, IsHost = false };
        using (var client = new WildsRelayClient(new HttpClient(handler), config, () => clock))
        {
            Pump(client, snapshot, resolve, "Offline");
            clock = 999; client.Update(snapshot, resolve); Assert(handler.Calls == 1, "first failure waits one second");
            clock = 1000; client.Update(snapshot, resolve);
            Assert(SpinWait.SpinUntil(() => handler.Calls == 2, 2000), "retry starts after its delay");
            var watch = Stopwatch.StartNew();
            while (WildsRelayDiagnostics.Current.Requests < 2 && watch.ElapsedMilliseconds < 2000) { client.Update(snapshot, resolve); Thread.Sleep(1); }
            Assert(WildsRelayDiagnostics.Current.Requests == 2, "second failure was recorded");
            clock = 2999; client.Update(snapshot, resolve); Assert(handler.Calls == 2, "second failure waits two seconds");
            handler.Respond = r => Response(r);
            clock = 3000; Pump(client, snapshot, resolve, "Receiving");
            Assert(handler.Calls == 3 && client.Applied == 2, "a restored server resumes sync without resetting local values");
            clock = 3250; client.Update(snapshot, resolve);
            Assert(SpinWait.SpinUntil(() => handler.Calls == 4, 2000), "success restores the normal polling interval");
        }
    }
    static void Pump(WildsRelayClient client, WildsSnapshot snapshot, Func<WildsSessionIdentity> resolve, string expected)
    {
        var watch = Stopwatch.StartNew();
        do { client.Update(snapshot, resolve); if (client.State == expected) return; Thread.Sleep(5); } while (watch.ElapsedMilliseconds < 8000);
        throw new Exception("relay expected " + expected + ", got " + client.State);
    }
internal static void Lifecycle()
    {
        long clock = 0; var config = new RelayConfig { Enabled = true, ServerUrl = "http://unit.invalid/index.php", Role = RelayRole.Auto };
        var handler = new Handler { Respond = r => Response(r) }; var snapshot = Snapshot();
        using (var client = new WildsRelayClient(new HttpClient(handler), config, () => clock))
        {
            Pump(client, snapshot, () => new WildsSessionIdentity { Session = Session }, "Receiving");
            Assert((string)handler.Last["role"] == "guest" && !handler.Last["monsters"].Any(), "unknown automatic role never uploads guest values");
            config.Role = RelayRole.Host;
            Pump(client, snapshot, () => new WildsSessionIdentity { Session = Session, IsHost = false }, "Receiving");
            Assert((string)handler.Last["role"] == "guest", "verified guest role cannot be overridden into publishing");
            config.Role = RelayRole.Guest;
            Pump(client, snapshot, () => new WildsSessionIdentity { Session = Session, IsHost = true }, "Receiving");
            Assert((string)handler.Last["role"] == "guest", "receive-only preference takes precedence over memory host state");
            handler.Respond = r => Response(r, "host");
            config.Role = RelayRole.Host;
            Pump(client, snapshot, () => new WildsSessionIdentity { Session = Session }, "Providing");
            Assert(handler.Last["monsters"].Count() == 2, "an explicitly selected host can publish when automatic identity is unavailable");
            handler.Respond = r => Response(r, "guest"); config.ServerUrl += "/switch";
            Pump(client, snapshot, () => new WildsSessionIdentity { Session = Session, IsHost = true }, "HostConflict");
            Assert(client.Applied == 0, "a local host waiting for an old server lease never consumes another publisher's values");
            config.Role = RelayRole.Guest;
            handler.Respond = r => { var result = Response(r); result["ageMs"] = 4000; result["monsters"] = JArray.FromObject(WildsRelayData.Create(Snapshot(), Session, "id", true, false).Monsters); return result; };
            snapshot.Monsters[0].Parts[0].Health = 777;
            Pump(client, snapshot, () => new WildsSessionIdentity { Session = Session }, "WaitingForData");
            Assert(client.Applied == 0 && snapshot.Monsters[0].Parts[0].Health == 777, "stale server data is explicitly waiting and never applied even after a successful HTTP response");
            int calls = handler.Calls; config.Enabled = false; client.Update(snapshot, () => throw new Exception("must not resolve"));
            Assert(client.State == "Disabled" && handler.Calls == calls, "disabled sync performs no game or network reads");
            config.Enabled = true; snapshot.IsQuestDamageFrozen = true; client.Update(snapshot, () => throw new Exception("must not resolve"));
            Assert(client.State == "WaitingForSession" && handler.Calls == calls && client.Applied == 0, "quest end clears remote state immediately");
            snapshot.IsQuestDamageFrozen = false; handler.Respond = r => { var result = Response(r); result["session"] = new string('f', 64); return result; };
            Pump(client, snapshot, () => new WildsSessionIdentity { Session = Session }, "InvalidResponse");
            Assert(client.Applied == 0, "a mismatched session response is rejected");
            config.ServerUrl = "http://user:pass@unit.invalid"; client.Update(snapshot, () => throw new Exception("must not resolve")); Assert(client.State == "InvalidAddress", "URLs containing credentials are rejected");
            Assert(WildsRelayClient.Endpoint("http://127.0.0.1:80/index.php").AbsolutePath == "/wilds/v1/sync", "legacy endpoint URL normalizes to an isolated versioned route");
            var defaults = new MainConfig();
            Assert(defaults.Servers.World.ServerUrl == RelayConfig.DefaultServerUrl && defaults.Servers.Wilds.ServerUrl == RelayConfig.DefaultWildsServerUrl && !defaults.Servers.World.Enabled && !defaults.Servers.Wilds.Enabled, "both game defaults use the chosen relay without changing the sync switch");
            Assert(SmartHunter.Core.Helpers.ServerManager.LegacyEndpoint(defaults.Servers.World.ServerUrl) == "http://140.238.55.121/index.php" && WildsRelayClient.Endpoint(defaults.Servers.Wilds.ServerUrl).AbsoluteUri == "http://140.238.55.121/wilds/v1/sync", "default address resolves to each game's own HTTP IP endpoint");
            Assert(JsonConvert.DeserializeObject<RelayConfig>("{\"ServerUrl\":\"  \"}").ServerUrl == RelayConfig.DefaultServerUrl && JsonConvert.DeserializeObject<RelayConfig>("{\"ServerUrl\":\"http://custom.invalid/\",\"Enabled\":true}").ServerUrl == "http://custom.invalid/", "empty old addresses upgrade while custom servers survive");
            config.ServerUrl = "http://unit.invalid"; handler.Gate = new TaskCompletionSource<bool>(); int previous = handler.Calls;
            client.Update(snapshot, () => new WildsSessionIdentity { Session = Session });
            Assert(SpinWait.SpinUntil(() => handler.Calls > previous, 2000), "the background request started");
            for (int i = 0; i < 100; i++) client.Update(snapshot, () => throw new Exception("overlapping read"));
            Assert(handler.Calls == previous + 1, "UI refreshes cannot overlap network requests or session reads");
            config.Enabled = false; client.Update(snapshot, () => throw new Exception("disabled read")); handler.Gate.SetResult(true);
            Assert(client.State == "Disabled" && client.Applied == 0, "disabling during an in-flight request discards its result");
        }
        var settings = SettingsViewModel.Instance; var saved = ConfigHelper.Main.Values.Relay;
        string url = saved.ServerUrl; var role = saved.Role;
        try
        {
            var text = settings.Settings.Single(s => s.Key == "LOC_UI_RELAY_URL"); Assert(text.IsText && !text.IsToggle && !text.IsChoice, "server address is editable in the existing desktop design");
            text.Text = "http://127.0.0.1:18080"; settings.Settings.Single(s => s.Key == "LOC_UI_RELAY_ROLE").SelectedOption = settings.Settings.Single(s => s.Key == "LOC_UI_RELAY_ROLE").Options.Single(o => o.Id == "Guest");
            var savedFile = JObject.Parse(File.ReadAllText(ConfigHelper.Main.FullPathFileName))["Server"];
            Assert((string)savedFile["ServerUrl"] == text.Text && (string)savedFile["Role"] == "Guest", "address and role persist through the settings UI");
            var section = settings.SelectedSection; var language = settings.SelectedLanguage;
            var window = new SmartHunter.Ui.Windows.ConsoleWindow();
            try
            {
                settings.SelectedSection = settings.Sections.Single(s => s.Id == "Network");
                settings.SelectedLanguage = settings.Languages.Single(l => l.FileName == "zh-TW.Wilds.json");
                TestSupport.Render((System.Windows.FrameworkElement)window.Content, "wilds-network-settings-zh-TW.png", 1120, 780);
                Assert(settings.FilteredSettings.Cast<SmartHunter.Core.Setting>().Count() == 6 && settings.FilteredSettings.Cast<SmartHunter.Core.Setting>().All(s => !s.Name.StartsWith("LOC_UI_")), "network settings render with localized web sharing controls in the unchanged desktop design");
            }
            finally { window.Close(); settings.SelectedSection = section; settings.SelectedLanguage = language; }
        }
        finally { saved.ServerUrl = url; saved.Role = role; ConfigHelper.SaveSettings(); }
    }
internal static void RuntimeIdentity()
    {
        string copied = new string('c', 32);
        var config = JsonConvert.DeserializeObject<RelayConfig>("{\"Enabled\":true,\"ServerUrl\":\"http://unit.invalid\",\"ClientId\":\"" + copied + "\"}");
        var handler = new Handler { Respond = r => Response(r) };
        var secondHandler = new Handler { Respond = r => Response(r) };
        var snapshot = Snapshot();
        using (var first = new WildsRelayClient(new HttpClient(handler), config))
        using (var second = new WildsRelayClient(new HttpClient(secondHandler), config.Copy()))
        {
            Func<WildsSessionIdentity> resolve = () => new WildsSessionIdentity { Session = Session, IsHost = false, HostIndex = 0 };
            Pump(first, snapshot, resolve, "Receiving"); Pump(second, snapshot, resolve, "Receiving");
            string a = (string)handler.Last["client"], b = (string)secondHandler.Last["client"];
            Assert(a != b && a != copied && b != copied && Guid.TryParseExact(a, "N", out _) && Guid.TryParseExact(b, "N", out _), "copied settings cannot collide at the server");
            Assert(config.ClientId == copied && !JObject.FromObject(config).ContainsKey("ClientId"), "legacy field loads without being rewritten into portable settings");
            var report = first.ConnectionReport;
            Assert(report.ClientId == a && report.Session == Session && report.SentRole == "guest" && report.ResponseRole == "guest" && report.ResolvedIsHost == false && report.HttpStatus == 200 && report.HasHost && report.Peers == 2 && report.SourceMonsters == 2 && report.RequestBytes > 0 && report.StageId == snapshot.StageId && report.PayloadAgeMilliseconds == 0, "diagnostics distinguish HTTP success, source availability and the actual runtime identity");
            first.Reset(); Pump(first, snapshot, resolve, "Receiving");
            Assert((string)handler.Last["client"] == a, "settings/lifecycle resets do not churn a live instance's identity");
        }
    }
internal static void RealSnapshots()
    {
        var assembly = typeof(RelayTests).Assembly; JObject evidence;
        using (var reader = new StreamReader(assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(n => n.EndsWith("UserNetworkSchema8Monsters.json")))))
            evidence = JObject.Parse(reader.ReadToEnd());
        Assert((string)evidence["HostState"] == "Providing" && (string)evidence["GuestState"] == "NoHost", "actual reports had no remote payload, not a reported pairing failure");
        var host = new WildsSnapshot { IsReady = true, InHuntingZone = true, StageId = 1 };
        var guest = new WildsSnapshot { IsReady = true, InHuntingZone = true, StageId = 1 };
        host.Monsters.AddRange(evidence["HostMonsters"].ToObject<System.Collections.Generic.List<WildsMonsterSnapshot>>());
        guest.Monsters.AddRange(evidence["GuestMonsters"].ToObject<System.Collections.Generic.List<WildsMonsterSnapshot>>());
        // Diagnostics deliberately omit addresses. Supply distinct local-only
        // addresses solely to exercise the real WPF view-model update path.
        for (int i = 0; i < guest.Monsters.Count; i++)
        {
            var m = guest.Monsters[i]; m.Address = 0x100000 + (ulong)i * 0x1000;
            foreach (var p in m.Parts) p.Address = m.Address + 0x100 + (ulong)p.Slot * 8;
        }
        var source = WildsRelayData.Create(host, Session, "id", true, false).Monsters;
        var target = guest.Monsters.Single(m => m.Id == 21); var part = target.Parts.Single(p => p.Slot == 8);
        float before = part.Health, health = target.Health, expected = source.Single(m => m.Id == 21).Parts.Single(p => p.Slot == 8).Health;
        Assert(before != expected && source.Count == 5, "the real host snapshot contains a changed part and five valid source monsters");
        var applier = new WildsSnapshotApplier();
        try
        {
            applier.Apply(guest);
            var displayed = OverlayViewModel.Instance.MonsterWidget.Context.Monsters.Single(m => m.Address == target.Address).Parts.Single(p => p.Address == part.Address);
            int changes = 0; displayed.Health.PropertyChanged += (s, e) => { if (e.PropertyName == "Current") changes++; };
            int applied = WildsRelayData.Merge(guest, source, false, out int skipped, out var report, new WildsRelayPairing());
            applier.Apply(guest);
            Assert(applied == 5 && skipped == 0 && report.Approximate == 0 && report.Pairs.All(p => p.SourceKey != p.LocalKey), "real descriptors pair all five without assuming shared TargetKeys");
            Assert(part.Health == expected && displayed.Health.Current == expected && changes > 0 && target.Health == health, "remote part changes reach the correct overlay binding while main health stays local");
        }
        finally { applier.Clear(); }
    }
}

static class WebShareTests
{
internal static void Routing()
    {
        var defaults = new MainConfig();
        Assert(defaults.Servers.World.ServerUrl == RelayConfig.DefaultServerUrl && defaults.Servers.Wilds.ServerUrl == RelayConfig.DefaultWildsServerUrl, "game defaults are separate");
        Assert((string)GameSettingsStore.Document(defaults, GameChoice.World)["Server"]["ServerUrl"] == "http://140.238.55.121/" &&
            (string)GameSettingsStore.Document(defaults, GameChoice.Wilds)["Server"]["ServerUrl"] == "http://140.238.55.121/", "fresh JSON for both games uses the HTTP IP relay");
        foreach (string old in new[] { "https://smarthunter.dragonyue.net/", "https://smarthunter.dragonyue.net/index.php", "https://smarthunter.dragonyue.net/wilds/v1/sync", "http://smarthunter.dragonyue.net/" })
        {
            var values = new MainConfig();
            GameSettingsStore.Apply(values, GameChoice.Wilds, JObject.FromObject(new { Server = new { ServerUrl = old, Enabled = true } }));
            Assert(values.Servers.Wilds.ServerUrl == "http://140.238.55.121/" && values.Servers.Wilds.Enabled && values.Servers.World.ServerUrl == RelayConfig.DefaultServerUrl, "previous domain default corrects to HTTP without touching World or the enabled flag");
            Assert(WebShareIdentity.Link(GameChoice.Wilds, old).StartsWith("https://smarthunter.dragonyue.net/watch?share=") && WebShareIdentity.Link(GameChoice.Wilds, old).EndsWith("&game=wilds"), "existing default links use the same public domain");
        }
        var custom = new RelayConfig { ServerUrl = "http://127.0.0.1:18080/service/" }; custom.ApplyGameDefaults(GameChoice.Wilds);
        Assert(custom.ServerUrl == "http://127.0.0.1:18080/service/", "custom relays remain configurable");
        foreach (string url in new[] { "http://140.238.55.121/", "http://140.238.55.121/index.php", "http://140.238.55.121/wilds/v1/sync" })
        {
            var ip = new RelayConfig { ServerUrl = url }; ip.ApplyGameDefaults(GameChoice.Wilds);
            Assert(ip.ServerUrl == url && WebShareIdentity.Link(GameChoice.Wilds, url).StartsWith(RelayConfig.DefaultWatchBaseUrl + "watch?share="), "IP relay stays HTTP while its viewing link uses the domain");
        }
        foreach (string url in new[] { "https://custom.invalid/", "https://smarthunter.dragonyue.net:444/", "https://smarthunter.dragonyue.net/service/", "https://smarthunter.dragonyue.net/?custom=1" })
        {
            var config = new RelayConfig { ServerUrl = url }; config.ApplyGameDefaults(GameChoice.Wilds);
            Assert(config.ServerUrl == url, "custom hosts, ports, prefixes and parameters remain untouched");
        }
        string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "routing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            defaults.Servers.World.ServerUrl = "http://world.invalid/index.php";
            GameSettingsStore.Write(root, defaults);
            string worldPath = GameSettingsStore.PathFor(root, GameChoice.World), wildsPath = GameSettingsStore.PathFor(root, GameChoice.Wilds), worldBefore = File.ReadAllText(worldPath);
            var document = JObject.Parse(File.ReadAllText(wildsPath));
            document["Server"]["ServerUrl"] = "https://smarthunter.dragonyue.net/";
            document["Server"]["Enabled"] = true; document["Server"]["Role"] = "Guest";
            document["Server"]["ShareWebView"] = true; document["Server"]["CustomField"] = "keep";
            document["CustomSetting"] = new JObject { ["Value"] = 37 };
            File.WriteAllText(wildsPath, document.ToString());
            DateTime modified = File.GetLastWriteTimeUtc(wildsPath);
            var loaded = GameSettingsStore.Read(root);
            var saved = JObject.Parse(File.ReadAllText(wildsPath));
            document["Server"]["ServerUrl"] = "http://140.238.55.121/";
            Assert(JToken.DeepEquals(saved, document) && File.ReadAllText(worldPath) == worldBefore, "startup corrects only the stored Wilds URL, preserving all other fields and the World file");
            Assert(File.GetLastWriteTimeUtc(wildsPath) == modified, "URL migration must not change which profile supplies startup selection");
            Assert(loaded.Servers.Wilds.ServerUrl == "http://140.238.55.121/" && loaded.Servers.Wilds.Enabled && loaded.Servers.Wilds.Role == RelayRole.Guest && loaded.Servers.Wilds.ShareWebView, "migration keeps role and sharing switches");
            Assert(!WebShareIdentity.Link(GameChoice.Wilds, loaded.Servers.Wilds.ServerUrl).Contains("140.238.55.121"), "saved transport URL does not leak into the public viewing link");
        }
        finally { Directory.Delete(root, true); }
        Assert(WebShareIdentity.Link(GameChoice.World, RelayConfig.DefaultServerUrl) == "", "World has no public sharing link");
        var vm = SmartHunter.Game.Data.ViewModels.SettingsViewModel.Instance;
        var main = SmartHunter.Game.Helpers.ConfigHelper.Main.Values; var editing = main.SettingsGame; var section = vm.SelectedSection;
        try
        {
            main.SettingsGame = GameChoice.World; vm.RefreshGameProfile(); vm.SelectedSection = vm.Sections.Single(x => x.Id == "Network");
            Assert(vm.FilteredSettings.Cast<Setting>().All(x => x.Key != "LOC_UI_WEB_SHARE_ENABLED" && x.Key != "LOC_UI_WEB_SHARE_SESSION_LINK"), "World hides web sharing controls");
        }
        finally { main.SettingsGame = editing; vm.RefreshGameProfile(); vm.SelectedSection = section; }
    }
internal static void Preferences()
    {
        var original = new RelayConfig(); Assert(!original.ShareWebView, "old settings cannot silently enable public viewing");
        original.ShareWebView = true; var copy = new RelayConfig(); copy.CopyFrom(original);
        Assert(copy.ShareWebView && JsonConvert.DeserializeObject<RelayConfig>(JsonConvert.SerializeObject(copy)).ShareWebView, "preference survives copying and storage");
        var profiles = new GameServerProfiles(); profiles.World.ShareWebView = true;
        Assert(!profiles.Wilds.ShareWebView, "game settings are independent");
    }
internal static void Identity()
    {
        string key = WebShareIdentity.GetKey(GameChoice.Wilds), again = WebShareIdentity.GetKey(GameChoice.Wilds);
        string other = WebShareIdentity.GetKey(GameChoice.World);
        Assert(key == again && key != other && key.Length == 64, "per-run keys are separate for each game and stable in this process");
        string first = WebShareIdentity.Link(GameChoice.Wilds, "http://127.0.0.1:8080/index.php");
        Assert(first == WebShareIdentity.Link(GameChoice.Wilds, "http://127.0.0.1:8080/") && first.Contains("/watch?share=" + WebShareIdentity.Token(key)) && !first.Contains(key), "public link contains the derived hash, never the private key or quest/session");
        Assert(WebShareIdentity.Control(GameChoice.Wilds, false).Enabled == false && WebShareIdentity.Control(GameChoice.Wilds, true).Key == key, "disabling and re-enabling retains the same owned identity");
        Assert(WebShareIdentity.Link(GameChoice.Wilds, "http://127.0.0.1:8080/service/index.php").Contains("/service/watch?share="), "path prefixes and ports survive URL construction");
    }
    static string[] Probe()
    {
        using (var process = Process.Start(new ProcessStartInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SmartHunter.Tests.exe"), "--web-share-probe")
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true }))
        {
            if (!process.WaitForExit(10000)) { process.Kill(); throw new Exception("Sharing identity probe timed out"); }
            string output = process.StandardOutput.ReadToEnd(), error = process.StandardError.ReadToEnd();
            Assert(process.ExitCode == 0, error);
            var tokens = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            Assert(tokens.Length == 2 && tokens.All(t => t.Length == 64 && t.All(c => "0123456789abcdef".Contains(c))), "each game emits one valid public token");
            return tokens;
        }
    }
internal static void FreshProcessIdentity()
    {
        var paths = new[] { "World", "Wilds" }.Select(game => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", game, "WebShare.key")).ToArray();
        var previous = paths.Select(path => File.Exists(path) ? File.ReadAllBytes(path) : null).ToArray();
        string copiedKey = new string('1', 64);
        try
        {
            for (int i = 0; i < paths.Length; i++) { Directory.CreateDirectory(Path.GetDirectoryName(paths[i])); File.WriteAllText(paths[i], copiedKey); }
            var first = Probe(); var second = Probe();
            Assert(first.Concat(second).Distinct().Count() == 4 && !first.Concat(second).Contains(WebShareIdentity.Token(copiedKey)), "copied legacy files cannot duplicate identities across launches or games");
            Assert(paths.All(path => File.ReadAllText(path) == copiedKey), "legacy files are ignored and never rewritten");
            foreach (string path in paths) File.Delete(path);
            Probe();
            Assert(paths.All(path => !File.Exists(path)), "fresh processes never persist sharing keys in the program folder");
        }
        finally
        {
            for (int i = 0; i < paths.Length; i++)
                if (previous[i] != null) File.WriteAllBytes(paths[i], previous[i]); else if (File.Exists(paths[i])) File.Delete(paths[i]);
        }
    }
internal static void Snapshots()
    {
        var snapshot = WildsSnapshotApplier.Preview(2);
        for (int i = 0; i < snapshot.Monsters.Count; i++) snapshot.Monsters[i].TargetKey = 100 + i;
        var status = snapshot.Monsters[0].Ailments[0]; status.Active = true; status.Remaining = 12; status.MaxDuration = 20;
        var relay = WildsRelayData.Create(snapshot, "session", "client", true, true);
        var view = WebViewData.Wilds(snapshot, relay.Monsters);
        Assert(view.Monsters.Count == 2 && view.Monsters[0].Health == snapshot.Monsters[0].Health && view.Monsters[0].Ailments[0].Remaining == 12 && view.Monsters[0].Ailments[0].Active, "web display has current HP and all active statuses/timers");
        Assert(!relay.Monsters[0].Ailments.Any(a => a.Active), "ordinary relay still publishes only permitted inactive buildup");
        Assert(view.Monsters[0].Parts == null && view.Monsters[0].PartNames.Count == relay.Monsters[0].Parts.Count, "Wilds reuses the matching relay parts instead of uploading duplicates");
        string json = JsonConvert.SerializeObject(view);
        Assert(!json.Contains("Address") && !json.Contains("Context") && !json.Contains("QuestIdentity"), "display DTO has no process pointers or raw network objects");
        snapshot.Monsters[0].Health = float.NaN;
        Assert(WebViewData.Wilds(snapshot, relay.Monsters).Monsters[0].Health == 0, "invalid optional display floats never poison ordinary synchronization");
    }
}
