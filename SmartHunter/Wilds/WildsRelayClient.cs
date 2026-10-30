using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using SmartHunter.Game.Config;
using SmartHunter.Game.Helpers;

namespace SmartHunter.Wilds
{
    public sealed class WildsRelayConnectionReport
    {
        public string Endpoint, ClientId, SelectedRole, SentRole, ResponseRole, Session, State;
        public bool? ResolvedIsHost;
        public int? HostIndex, PayloadAgeMilliseconds;
        public bool HttpAttempted, HasHost, HasPlayer;
        public int HttpStatus, Peers, SourceMonsters, StageId;
        public long RequestBytes, ResponseBytes, LatencyMilliseconds;
    }
    // One bounded background exchange at a time. No network await blocks the
    // memory reader/UI loop; local data remains the fallback on every frame.
    public sealed class WildsRelayClient : IDisposable
    {
        sealed class Exchange
        {
            public int Generation, Stage;
            public ulong Quest;
            public float Elapsed;
            public long Started;
            public string State;
            public string SentRole, Session;
            public bool? ResolvedIsHost;
            public bool HostUnknown;
            public bool IsLocalHost;
            public bool HttpAttempted;
            public int HttpStatus;
            public long RequestBytes, ResponseBytes, Latency;
            public string FailureReason = "PROTOCOL";
            public int? HostIndex;
            public WildsRelayResponse Response;
            public WildsSessionIdentity Identity;
            public long IdentityReadAt;
        }
        const int MaxBytes = 524288;
        readonly RelayConfig config;
        readonly string clientId = Guid.NewGuid().ToString("N");
        readonly HttpClient http;
        readonly Func<long> now;
        readonly WildsRelayDiagnostics diagnostics;
        readonly WildsRelayPairing pairing = new WildsRelayPairing();
        int? sourceHostIndex;
        readonly JsonSerializerSettings json = new JsonSerializerSettings { ContractResolver = new CamelCasePropertyNamesContractResolver(), MaxDepth = 24 };
        Task<Exchange> pending;
        CancellationTokenSource cancellation;
        Exchange cached;
        WildsSessionIdentity identity;
        long identityReadAt;
        int failures;
        const int ExchangeIntervalMs = 250;
        const int IdentityIntervalMs = 1000;
        int generation;
        long next;
        ulong quest;
        int stage = -1;
        float elapsed;
        string settings;
        bool disposed;
        public string State { get; private set; } = "Disabled";
        public int Peers { get; private set; }
        public int Applied { get; private set; }
        public int Skipped { get; private set; }
        public WildsRelayMatchReport MatchReport { get; private set; }
        public WildsRelayConnectionReport ConnectionReport => new WildsRelayConnectionReport {
            Endpoint = Endpoint(config.ServerUrl)?.AbsoluteUri, ClientId = clientId, SelectedRole = config.Role.ToString(),
            State = State, SentRole = cached?.SentRole, ResponseRole = cached?.Response?.Role, Session = cached?.Session, ResolvedIsHost = cached?.ResolvedIsHost,
            HostIndex = cached?.HostIndex, PayloadAgeMilliseconds = cached?.Response?.AgeMs, StageId = cached?.Stage ?? stage,
            HttpAttempted = cached?.HttpAttempted ?? false, HttpStatus = cached?.HttpStatus ?? 0,
            HasHost = cached?.Response?.HasHost ?? false, HasPlayer = cached?.Response?.HasPlayer ?? false,
            Peers = cached?.Response?.Peers ?? 0, SourceMonsters = cached?.Response?.Monsters?.Count ?? 0,
            RequestBytes = cached?.RequestBytes ?? 0, ResponseBytes = cached?.ResponseBytes ?? 0, LatencyMilliseconds = cached?.Latency ?? 0 };
        public WildsRelayClient() : this(new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(3) }, ConfigHelper.Main.Values.Relay) { }
        public WildsRelayClient(HttpClient http, RelayConfig config, Func<long> clock = null, Action<string, object[]> log = null)
        {
            this.http = http; this.config = config;
            var watch = Stopwatch.StartNew(); now = clock ?? (() => watch.ElapsedMilliseconds);
            diagnostics = new WildsRelayDiagnostics(now, log);
        }
        public static Uri Endpoint(string value)
        {
            if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https") || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0) return null;
            string path = uri.AbsolutePath.TrimEnd('/');
            if (path.EndsWith("/index.php", StringComparison.OrdinalIgnoreCase)) path = path.Substring(0, path.Length - 10);
            if (!path.EndsWith("/wilds/v1/sync", StringComparison.Ordinal)) path += "/wilds/v1/sync";
            return new UriBuilder(uri) { Path = path }.Uri;
        }
        public void Reset()
        {
            generation++; cancellation?.Cancel(); cancellation?.Dispose(); cancellation = null;
            pending = null; cached = null; next = 0; quest = 0; stage = -1; elapsed = 0; Peers = Applied = Skipped = 0;
            pairing.Clear(); sourceHostIndex = null; MatchReport = null; diagnostics.ResetMatch();
            identity = null; identityReadAt = 0; failures = 0;
        }
        public void Update(WildsSnapshot snapshot, Func<WildsSessionIdentity> resolve)
        {
            if (disposed) return;
            Uri endpoint = Endpoint(config.ServerUrl);
            diagnostics.Configure(config.Enabled ? endpoint : null);
            string signature = config.Enabled + "/" + config.ServerUrl + "/" + config.Role + "/" + config.SyncAilmentBuildup + "/" + config.ShareWebView;
            if (settings != signature) { Reset(); settings = signature; }
            if (!config.Enabled) { SetState("Disabled"); return; }
            if (String.IsNullOrWhiteSpace(config.ServerUrl)) { SetState("MissingAddress"); return; }
            if (endpoint == null) { SetState("InvalidAddress"); return; }
            if (!snapshot.IsReady || snapshot.IsLoading || !snapshot.CanDisplayData || snapshot.IsQuestDamageFrozen || snapshot.QuestSuccessState != 0 || snapshot.QuestFailureState != 0)
            { if (stage != -1 || pending != null || cached != null) Reset(); SetState("WaitingForSession"); return; }
            if (stage != snapshot.StageId || quest != snapshot.QuestIdentity || snapshot.QuestElapsed < elapsed - 5000)
            { Reset(); stage = snapshot.StageId; quest = snapshot.QuestIdentity; }
            elapsed = snapshot.QuestElapsed;
            Applied = Skipped = 0;
            if (pending?.IsCompleted == true)
            {
                var result = pending.GetAwaiter().GetResult(); pending = null;
                if (result.Generation == generation)
                {
                    cached = result; Peers = result.Response?.Peers ?? 0;
                    identity = result.Identity; identityReadAt = result.IdentityReadAt;
                    if (result.HostIndex.HasValue)
                    {
                        if (sourceHostIndex.HasValue && sourceHostIndex != result.HostIndex) pairing.Clear();
                        sourceHostIndex = result.HostIndex;
                    }
                    if (result.HttpAttempted)
                    {
                        diagnostics.Record(result.Response != null, result.HttpStatus, result.FailureReason, result.RequestBytes, result.ResponseBytes, result.Latency);
                        if (result.Response != null) failures = 0;
                        else next = now() + Math.Min(15000, 1000 * (1 << Math.Min(4, failures++)));
                    }
                    else next = now() + IdentityIntervalMs;
                }
            }
            if (pending == null && now() >= next)
            {
                var local = WildsRelayData.Create(snapshot, null, clientId, true, config.SyncAilmentBuildup);
                if (config.ShareWebView && config.Role != RelayRole.Guest && identity?.IsHost != false) local.View = SmartHunter.Core.WebViewData.Wilds(snapshot, local.Monsters);
                var work = new Exchange { Generation = generation, Stage = snapshot.StageId, Quest = snapshot.QuestIdentity, Elapsed = snapshot.QuestElapsed, Started = now() };
                var role = config.Role;
                cancellation?.Dispose(); cancellation = new CancellationTokenSource(); var token = cancellation.Token;
                // Cache the fully validated identity for at most one second;
                // raising HTTP frequency must not multiply game-memory reads.
                var reusableIdentity = identity?.Session != null && now() - identityReadAt < IdentityIntervalMs ? identity : null;
                long readAt = identityReadAt;
                pending = Synchronize(local, resolve, reusableIdentity, readAt, role, endpoint, work, token);
                next = now() + ExchangeIntervalMs;
                if (cached == null) SetState("Connecting");
            }
            string state = cached?.State;
            if (cached?.Response?.Role == "guest" && !cached.IsLocalHost && cached.Response.HasHost && cached.Response.Monsters != null && cached.Response.Monsters.Count > 0 && cached.Response.AgeMs.HasValue &&
                cached.Generation == generation && cached.Stage == snapshot.StageId && cached.Quest == snapshot.QuestIdentity &&
                snapshot.QuestElapsed >= cached.Elapsed - 1000 && cached.Response.AgeMs.Value + now() - cached.Started <= 3000)
            {
                Applied = WildsRelayData.Merge(snapshot, cached.Response.Monsters, config.SyncAilmentBuildup, out int skipped, out var report, pairing); Skipped = skipped;
                MatchReport = report; diagnostics.Match(report);
                state = report.Approximate > 0 ? "Approximate" : skipped == 0 ? "Receiving" : Applied == 0 ? "Unmatched" : "PartiallyApplied";
            }
            else { MatchReport = null; if (state == "Receiving") state = "WaitingForData"; }
            if (state != null) SetState(state);
        }
        void SetState(string value) { State = value; diagnostics.State(value); }
        async Task<Exchange> Synchronize(WildsRelayRequest request, Func<WildsSessionIdentity> resolve, WildsSessionIdentity reusableIdentity, long readAt, RelayRole role, Uri endpoint, Exchange work, CancellationToken token)
        {
            Stopwatch transfer = null;
            try
            {
                var identity = reusableIdentity ?? await Task.Run(resolve, token).ConfigureAwait(false);
                work.Identity = identity;
                work.IdentityReadAt = reusableIdentity != null ? readAt : work.Started;
                if (identity?.Session == null) { work.State = "WaitingForSession"; return work; }
                work.ResolvedIsHost = identity.IsHost;
                bool host = role != RelayRole.Guest && (identity.IsHost == true || identity.IsHost == null && role == RelayRole.Host);
                work.IsLocalHost = host;
                work.HostUnknown = role == RelayRole.Auto && identity.IsHost == null;
                work.HostIndex = identity.HostIndex;
                request.Session = identity.Session; request.Role = host ? "host" : "guest";
                request.AckOnly = host;
                if (host) request.WebShare = SmartHunter.Core.WebShareIdentity.Control(SmartHunter.World.GameChoice.Wilds, config.ShareWebView);
                work.Session = request.Session; work.SentRole = request.Role;
                if (!host) { request.Monsters.Clear(); request.View = null; request.WebShare = null; }
                string body = JsonConvert.SerializeObject(request, json);
                int bytes = Encoding.UTF8.GetByteCount(body);
                if (bytes > MaxBytes) { work.State = "InvalidResponse"; return work; }
                using (var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(token))
                using (var message = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = new StringContent(body, Encoding.UTF8, "application/json") })
                {
                    // HttpClient's headers-only timeout does not cover streaming
                    // the body. Bound both phases so a stalled peer cannot hang.
                    requestTimeout.CancelAfter(TimeSpan.FromSeconds(3));
                    work.HttpAttempted = true; work.RequestBytes = bytes; transfer = Stopwatch.StartNew();
                    using (var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, requestTimeout.Token).ConfigureAwait(false))
                    {
                        work.HttpStatus = (int)response.StatusCode;
                        if (!response.IsSuccessStatusCode) { work.FailureReason = "HTTP"; work.State = work.HttpStatus == 404 ? "InvalidResponse" : "Offline"; return work; }
                        if (response.Content.Headers.ContentLength > MaxBytes) { work.FailureReason = "LARGE"; work.State = "InvalidResponse"; return work; }
                        using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (var output = new MemoryStream())
                        {
                            var buffer = new byte[8192]; int size;
                            while ((size = await input.ReadAsync(buffer, 0, buffer.Length, requestTimeout.Token).ConfigureAwait(false)) > 0)
                            {
                                work.ResponseBytes += size;
                                if (output.Length + size > MaxBytes) { work.FailureReason = "LARGE"; work.State = "InvalidResponse"; return work; }
                                output.Write(buffer, 0, size);
                            }
                            var data = JsonConvert.DeserializeObject<WildsRelayResponse>(Encoding.UTF8.GetString(output.ToArray()), json);
                            if (data == null || data.Status != "ok" || data.Protocol != 1 || data.Session != request.Session || (data.Role != "host" && data.Role != "guest") || data.Role == "host" && request.Role != "host" || data.Peers < 1 || data.Peers > 64 || data.AgeMs < 0 || data.Monsters != null && (data.Monsters.Count > 64 || data.Monsters.Any(m => !WildsRelayData.Valid(m))))
                            { work.State = "InvalidResponse"; return work; }
                            work.Response = data;
                            work.State = data.Role == "host" ? "Providing" : host ? "HostConflict" : !data.HasHost ? work.HostUnknown ? "HostUnknown" : "NoHost" :
                                !data.AgeMs.HasValue || data.AgeMs.Value + now() - work.Started > 3000 || data.Monsters == null ? "WaitingForData" : "Receiving";
                        }
                    }
                }
            }
            catch (OperationCanceledException) { work.FailureReason = "TIMEOUT"; work.State = "Offline"; }
            catch (JsonException) { work.State = "InvalidResponse"; }
            catch (Exception) { work.FailureReason = "NETWORK"; work.State = work.HttpAttempted ? "Offline" : "WaitingForSession"; }
            finally { if (transfer != null) work.Latency = transfer.ElapsedMilliseconds; }
            return work;
        }
        public void Dispose() { if (disposed) return; disposed = true; Reset(); diagnostics.Flush(); http.Dispose(); }
    }
}
