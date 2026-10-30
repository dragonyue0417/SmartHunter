using System;
using System.Globalization;
using System.Linq;
using SmartHunter.Core;
using SmartHunter.Game.Helpers;

namespace SmartHunter.Wilds
{
    public sealed class WildsRelayLive
    {
        public string Server, State;
        public bool? Connected;
        public long Requests, Successes, Failures, Uploaded, Downloaded, AverageMs, LatestMs;
        public DateTime? LastRequestUtc;
        public int Applied, Skipped, Approximate;
    }
    // Called on the update/UI thread after a bounded request completes. Log only
    // connection/state transitions and summaries, never one line per poll.
    public sealed class WildsRelayDiagnostics
    {
        public static event Action<WildsRelayLive> LiveChanged;
        public static WildsRelayLive Current { get; private set; } = new WildsRelayLive { State = "Disabled" };
        WildsRelayMatchReport match;
        DateTime? lastRequest;
        void Publish()
        {
            long count = successes + failures;
            Current = new WildsRelayLive { Server = server, State = state, Connected = connected, Requests = count,
                Successes = successes, Failures = failures, Uploaded = uploaded, Downloaded = downloaded,
                AverageMs = count == 0 ? 0 : latency / count, LatestMs = latest, LastRequestUtc = lastRequest,
                Applied = match?.Applied ?? 0, Skipped = match?.Skipped ?? 0, Approximate = match?.Approximate ?? 0 };
            LiveChanged?.Invoke(Current);
        }
        readonly Func<long> now;
        readonly Action<string, object[]> output;
        string server = "", state = "Disabled";
        bool? connected;
        long successes, failures, uploaded, downloaded, latency, latest, nextSummary, summarized;
        string matchSignature;
        long nextMatchLog;
        public WildsRelayDiagnostics(Func<long> clock, Action<string, object[]> log = null)
        { now = clock; output = log ?? WriteLocalized; }
        public void Configure(Uri endpoint)
        {
            string value = endpoint?.AbsoluteUri ?? "";
            if (server == value) return;
            Flush(); server = value; connected = null;
            successes = failures = uploaded = downloaded = latency = latest = summarized = 0;
            lastRequest = null; match = null;
            nextSummary = now() + 30000;
            Publish();
        }
        public void State(string value)
        {
            if (state == value) return;
            state = value;
            if (value != "Receiving" && value != "Approximate" && value != "PartiallyApplied") match = null;
            Publish();
            output("LOC_LOG_RELAY_STATE", new object[] { Text("LOC_UI_RELAY_" + value) });
        }
        public void ResetMatch() { matchSignature = null; nextMatchLog = 0; match = null; Publish(); }
        public void Match(WildsRelayMatchReport report)
        {
            bool changed = match == null || match.Applied != report.Applied || match.Skipped != report.Skipped || match.Approximate != report.Approximate;
            match = report;
            if (changed) Publish();
            string pairs = String.Join(", ", report.Pairs.Take(8).Select(p => p.Id + ":" + p.SourceKey + "→" + p.LocalKey + (p.Approximate ? "~" : "")));
            string signature = report.LocalMonsters + "/" + report.SourceMonsters + "/" + report.Applied + "/" + report.Skipped + "/" + report.MissingDescriptor + "/" + report.AmbiguousDescriptor + "/" + report.BindingConflict + "/" + report.NoUsableFields + "/" + pairs;
            if (signature == matchSignature || now() < nextMatchLog) return;
            matchSignature = signature; nextMatchLog = now() + 5000;
            output("LOC_LOG_RELAY_MATCH", new object[] { report.LocalMonsters, report.SourceMonsters, report.Applied, report.Skipped,
                report.MissingDescriptor, report.AmbiguousDescriptor, report.BindingConflict, report.NoUsableFields, pairs.Length == 0 ? "—" : pairs });
        }
        public void Record(bool success, int status, string reason, long requestBytes, long responseBytes, long elapsed)
        {
            if (success) successes++; else failures++;
            uploaded += Math.Max(0, requestBytes); downloaded += Math.Max(0, responseBytes);
            latest = Math.Max(0, elapsed); latency += latest;
            if (success && connected != true)
                output(connected == false ? "LOC_LOG_RELAY_RECONNECTED" : "LOC_LOG_RELAY_CONNECTED",
                    new object[] { server, latest, Size(requestBytes), Size(responseBytes) });
            else if (!success && connected != false)
                output("LOC_LOG_RELAY_FAILED", new object[] { server, Text("LOC_LOG_RELAY_REASON_" + reason), status == 0 ? "—" : status.ToString(CultureInfo.InvariantCulture), latest });
            connected = success;
            lastRequest = DateTime.UtcNow;
            Publish();
            if (now() >= nextSummary) Flush();
        }
        public void Flush()
        {
            long count = successes + failures;
            if (count == summarized) return;
            output("LOC_LOG_RELAY_SUMMARY", new object[] { server, count, successes, failures, Size(uploaded), Size(downloaded), latency / count, latest });
            summarized = count; nextSummary = now() + 30000;
        }
        internal static string Size(long bytes)
        {
            if (bytes < 1024) return Math.Max(0, bytes).ToString(CultureInfo.InvariantCulture) + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024d).ToString("0.00", CultureInfo.InvariantCulture) + " KiB";
            return (bytes / (1024d * 1024)).ToString("0.00", CultureInfo.InvariantCulture) + " MiB";
        }
        static string Text(string key) => LocalizationHelper.GetString(key);
        static void WriteLocalized(string key, object[] values)
        {
            string message;
            try { message = String.Format(Text(key), values); }
            catch (FormatException) { message = String.Format(LanguageCatalog.ReadBuiltIn("en-US.json")[key], values); }
            Log.WriteLine(message);
        }
    }
}
