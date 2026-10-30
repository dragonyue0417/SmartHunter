using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SmartHunter.Game.Config;
using SmartHunter.Game.Data.ViewModels;
using SmartHunter.Game.Helpers;
using SmartHunter.Ui.Windows;
using SmartHunter.Wilds;
using static TestSupport;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

static class NativeSessionTests
{
    static WildsAddresses UnknownLayout(Fixture f)
    {
        var roots = new[] { "PlayerManager", "EnemyManager", "SaveManager", "QuestManager",
            "CameraManager", "PauseManager", "HealthEncryptionKey", "HealthEncryptionRound", "NpcManager" };
        return new WildsAddresses("1.42.0.4", roots.ToDictionary(n => n, n => f.Game.Addresses.Address(n, 0)), WildsMemoryLayout.CopyOffsets());
    }
internal static void UnknownVersionDiagnostics()
    {
        var member = new MemberFixture(); var f = member.F;
        ulong matchmake = f.Object("app.net_quest_session.QuestMatchmakeSystem", 0x20);
        f.M.Ptr(f.Quest + 0x1E0, matchmake); f.M.Ptr(matchmake + 0x10, f.Value); f.M.Ptr(f.Quest + 0x1D8, f.Value);
        Assert(f.Probe().ResolveSession(f.Snapshot).Session?.Length == 64, "known layout can resolve the fixture's matching session paths");
        ulong session = f.Object("via.network.utility.SessionId", 96), buffer = f.M.Alloc();
        byte[] secret = Encoding.ASCII.GetBytes("private-native-session-buffer"); f.M.Put(buffer, secret);
        f.M.Ptr(session + 0x30, buffer); f.M.Int(session + 0x38, secret.Length); f.M.Ptr(f.Quest + 0x60, session);
        ulong context = f.Object("app.cEnemyContext", 0x600), holder = f.Object("app.cEnemyContextHolder", 0x48);
        f.M.Ptr(holder + 0x40, context); f.M.Int(context + 0x80, 1234);
        f.Snapshot.Monsters.Add(new WildsMonsterSnapshot { Id = 21, TargetKey = 3191, Context = holder, Health = 500, MaxHealth = 6272 });
        var probe = new WildsNetworkProbe(f.M, UnknownLayout(f), f.Game.Base, 4096, 0x5000);
        var check = probe.Capture(f.Snapshot);
        Assert(check.FieldLayoutStatus == "UnverifiedGameVersion" && check.RootAddressSource == "PlayerManagerRelativeCandidate", "relative hypothesis is labeled separately from AOB and a verified field layout");
        Assert(check.RootValidationReason == "MetadataValidated" && check.MemoryReadAttempts > 0 && check.MemoryReadAttempts <= 4096, "unknown game version no longer skips live type validation");
        Assert(check.Nodes.Any(n => n.Type == "app.net_quest_session.cQuestSession") && check.QuestMembers.NativeTableStatus == "Completed" && check.QuestMembers.NativeMembers.Count == 1, "typed quest and native member collection runs on unknown builds");
        Assert(check.NativeSessionCandidates.Any(c => c.Status == "BufferFingerprintCandidate" && c.Sha256?.Length == 64) && check.NativeSessionCandidates.All(c => !c.ValidatedForSharing), "native session fingerprints remain diagnostic candidates");
        var identity = check.MonsterIdentityCandidates.Single();
        Assert(identity.ContextPath == "ContextHolder+0x40" && identity.BlockCandidates.Any() && f.Snapshot.Monsters[0].Context == holder, "unknown builds also collect typed monster candidates without changing production identities");
        Assert(check.QuestSessionCandidate.CurrentCandidateSha256?.Length == 64 && !check.QuestSessionCandidate.ValidatedForSharing, "matching local paths are recorded without claiming cross-client validation");
        var identityForRelay = probe.ResolveSession(f.Snapshot);
        Assert(identityForRelay.Session == check.QuestSessionCandidate.CurrentCandidateSha256 && !identityForRelay.IsHost.HasValue, "live agreeing GUIDs can group clients; a missing self getter still cannot identify the host");
        f.M.Ptr(f.Quest + 0x1D8, 0);
        Assert(probe.ResolveSession(f.Snapshot).Session == null, "native diagnostic buffer fingerprints cannot replace missing live GUID paths");
        string json = JsonConvert.SerializeObject(check);
        Assert(!json.Contains(Fixture.SessionId) && !json.Contains("private-native-session-buffer"), "raw native session values remain excluded");
        Assert(new WildsNetworkReport().Schema == 9, "relay connection details are emitted in schema 9");
    }
    static JObject PairedEvidence()
    {
        var assembly = typeof(NativeSessionTests).Assembly;
        using (var reader = new StreamReader(assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(n => n.EndsWith("UserNetworkSchema7Evidence.json")))))
            return JObject.Parse(reader.ReadToEnd());
    }
    sealed class PairedFixture
    {
        public readonly Fixture F = new Fixture();
        public readonly ulong FirstMember;
        public readonly WildsNetworkProbe Probe;
        public PairedFixture(bool host)
        {
            var sample = PairedEvidence()["Samples"].Single(s => (string)s["Role"] == (host ? "Host" : "Guest"));
            ulong matchmake = F.Object("app.net_quest_session.QuestMatchmakeSystem", 0x20);
            F.M.Ptr(F.Quest + 0x1E0, matchmake); F.M.Ptr(matchmake + 0x10, F.Value); F.M.Ptr(F.Quest + 0x1D8, F.Value);
            ulong table = F.Object("via.network.session.MemberInfoTbl", 528); F.M.Ptr(F.Quest + 0xA8, table);
            int propertyIndex = 0;
            foreach (var entry in sample["NativeMembers"])
            {
                ulong member = F.Object("via.network.session.MemberInfo", 160);
                if (FirstMember == 0) FirstMember = member;
                F.M.Ptr(table + 0x10 + (ulong)(int)entry["TableSlot"] * 8, member);
                F.M.Int(member + 0x14, (int)entry["NativeIndex"]); F.M.Byte(member + 0x11, (bool)entry["IsHost"] ? (byte)1 : (byte)0);
                F.M.Byte(member + 0x12, (bool)entry["IsSelf"] ? (byte)1 : (byte)0);
                // Copy only independently captured getter instructions, never
                // actual IDs or names. The fixture's GUID is synthetic.
                if (propertyIndex != 0) continue;
                foreach (var property in sample["MemberMetadata"]["Properties"])
                {
                    ulong getter = F.Game.Base + 0x1100 + (ulong)propertyIndex * 0x100;
                    F.M.Put(getter, ((string)property["GetterBytes"]).Split(' ').Select(s => Convert.ToByte(s, 16)).ToArray());
                    F.DefineProperty(member, (string)property["Name"], (string)property["Type"], getter, propertyIndex++ != 0);
                }
            }
            Probe = new WildsNetworkProbe(F.M, UnknownLayout(F), F.Game.Base, 4096, 0x5000);
        }
    }
    public static Func<WildsSessionIdentity> PairedSessionResolver(bool host, WildsSnapshot snapshot)
    {
        var fixture = new PairedFixture(host);
        return () => fixture.Probe.ResolveSession(snapshot);
    }
internal static void PairedSchema7Roles()
    {
        var samples = PairedEvidence()["Samples"];
        Assert((string)samples[0]["QuestCandidate"]["CurrentCandidateSha256"] == (string)samples[1]["QuestCandidate"]["CurrentCandidateSha256"] &&
            (string)samples[0]["NativeSession"]["Sha256"] == (string)samples[1]["NativeSession"]["Sha256"], "independent paired-client GUID and native buffer evidence agree");
        var host = new PairedFixture(true); var guest = new PairedFixture(false);
        var h = host.Probe.ResolveSession(host.F.Snapshot); var g = guest.Probe.ResolveSession(guest.F.Snapshot);
        Assert(h.Session?.Length == 64 && h.Session == g.Session && h.IsHost == true && g.IsHost == false && h.HostIndex == 0 && g.HostIndex == 0, "same synthetic session resolves with actual .3 getters under the production 512-read budget");
        guest.F.M.Ptr(guest.F.Quest + 0x1D8, 0); Assert(guest.Probe.ResolveSession(guest.F.Snapshot).Session == null, "a retained secondary path cannot reconnect a departed quest");
    }
    sealed class SessionReadRace : IWildsMemory
    {
        readonly FakeMemory memory; readonly ulong trigger, quest, replacement;
        public SessionReadRace(FakeMemory memory, ulong trigger, ulong quest, ulong replacement)
        { this.memory = memory; this.trigger = trigger; this.quest = quest; this.replacement = replacement; }
        public bool Read(ulong address, byte[] buffer, int size)
        {
            bool ok = memory.Read(address, buffer, size);
            if (address == trigger && size == 160) memory.Ptr(quest + 0x1D8, replacement);
            return ok;
        }
    }
internal static void SessionTransition()
    {
        var fixture = new PairedFixture(true); var f = fixture.F;
        var memory = new SessionReadRace(f.M, fixture.FirstMember, f.Quest, f.String("f8111c01-2694-4bb1-bf07-6c3cf437c338"));
        var probe = new WildsNetworkProbe(memory, UnknownLayout(f), f.Game.Base, 4096, 0x5000);
        var changed = probe.ResolveSession(f.Snapshot);
        Assert(changed.Session == null && !changed.IsHost.HasValue, "a changed GUID invalidates the session and roles together");
        var absent = new WildsAddresses("1.42.0.3", new Dictionary<string, ulong>(), WildsMemoryLayout.CopyOffsets());
        Assert(new WildsNetworkProbe(f.M, absent, f.Game.Base).ResolveSession(f.Snapshot).Session == null, "missing scanned player roots cannot begin synchronization");
    }
internal static void UnknownVersionLimits()
    {
        var f = new Fixture(); var layout = UnknownLayout(f);
        var bounded = new WildsNetworkProbe(f.M, layout, f.Game.Base, 32, 0x5000).Capture(f.Snapshot);
        Assert(bounded.MemoryReadAttempts == 32 && !bounded.TraversalCompleted, "unknown-version traversal shares the existing hard read budget");
        f.M.Ptr(f.Game.A("PlayerManager") - 0x46010, f.Object("app.Player", 464));
        var rejected = new WildsNetworkProbe(f.M, layout, f.Game.Base, 4096, 0x5000).Capture(f.Snapshot);
        Assert(rejected.RootValidationReason == "UnexpectedType: app.Player" && rejected.Nodes.Count == 0 && rejected.NativeSessionCandidates.Count == 0 && rejected.QuestMembers.NativeMembers.Count == 0, "unrelated root objects never become network data");
        var missing = new WildsAddresses("1.42.0.3", new Dictionary<string, ulong>(), WildsMemoryLayout.CopyOffsets());
        var absent = new WildsNetworkProbe(f.M, missing, f.Game.Base).Capture(f.Snapshot);
        Assert(absent.RootValidationReason == "MissingPlayerManager" && absent.MemoryReadAttempts == 0, "missing scanned player root is explained without reading memory");
        var nearBase = new WildsAddresses("1.42.0.3", new Dictionary<string, ulong> { { "PlayerManager", 0x1000 } }, WildsMemoryLayout.CopyOffsets());
        var outside = new WildsNetworkProbe(f.M, nearBase, f.Game.Base).Capture(f.Snapshot);
        Assert(outside.RootValidationReason == "RelativeRootOutsideModule" && outside.MemoryReadAttempts == 0, "relative subtraction cannot inspect memory before the module");
    }
internal static void MonsterIdentity()
    {
        var f = new Fixture();
        var monster = new WildsMonsterSnapshot { Id=21, TargetKey=3191, Context=f.Object("app.cEnemyContext", 0x600), Health=500, MaxHealth=6272 };
        for (int i=0; i<20; i++) monster.Parts.Add(new WildsPartSnapshot { Slot=i, NameId="part", Health=100, MaxHealth=200 });
        for (int i=0; i<40; i++) monster.Ailments.Add(new WildsAilmentSnapshot { Id=i });
        var secret = Encoding.ASCII.GetBytes("Not a raw memory dump"); f.M.Put(monster.Context+0x80, secret);
        f.M.Ptr(monster.Context+0x40, 0x123456780);
        ulong getter=f.Game.Base+0x1800;
        f.M.Put(getter, new byte[] { 0x8B, 0x42, 0x20, 0x41, 0x89, 0x00, 0xC3 }.Concat(new byte[57]).ToArray());
        f.DefineProperty(monster.Context, "UniqueIndex", "System.Int32", getter);
        f.Snapshot.Monsters.Add(monster);
        var probe=f.Probe(); var check=probe.Capture(f.Snapshot);
        Assert(check.MonsterStates[0].Parts.Count==20 && check.MonsterStates[0].Ailments.Count==40 && check.MonsterStates[0].Health==500, "full bounded parts, ailments and local health retained");
        var identity=check.MonsterIdentityCandidates.Single();
        Assert(identity.Properties.Any(p=>p.Name=="UniqueIndex" && p.GetterBytes!=null) && identity.BlockCandidates.Any(), "identity getter code and block fingerprints retained");
        Assert(!identity.BlockCandidates.Any(v=>v.Path=="Context+0x40" || v.Path=="Context+0x38") && identity.BlockCandidates.All(v=>v.Length==16 && v.Sha256.Length==64), "aligned pointers excluded and candidate bytes only exported as SHA256");
        string before=identity.BlockCandidates.First(v=>v.Path=="Context+0x80").Sha256;
        f.M.Byte(monster.Context+0x80, 99);
        Assert(probe.Capture(f.Snapshot).MonsterIdentityCandidates.Single().BlockCandidates.First(v=>v.Path=="Context+0x80").Sha256 != before, "capture reflects current data without retaining stale object bytes");
        Assert(!JsonConvert.SerializeObject(check).Contains("Not a raw memory dump") && check.MemoryReadAttempts<=4096, "diagnostic budget remains bounded without exporting raw object strings");
        ulong originalContext=monster.Context,holder=f.Object("app.cEnemyContextHolder",0x48);f.M.Ptr(holder+0x40,originalContext);monster.Context=holder;
        identity=probe.Capture(f.Snapshot).MonsterIdentityCandidates.Single();
        Assert(identity.Type=="app.cEnemyContext" && identity.HolderType=="app.cEnemyContextHolder" && identity.ContextPath=="ContextHolder+0x40" && identity.BlockCandidates.Any(),"real holder path reaches the typed context without altering snapshot context");
        Assert(monster.Context==holder,"diagnostics cannot change production monster identity or selection pointers");
        f.M.Ptr(holder+0x40,f.Object("app.Player",0x600));Assert(probe.Capture(f.Snapshot).MonsterIdentityCandidates.Single().Status=="ContextUnverified","unrelated holder targets remain rejected");
        monster.Context=f.Object("app.Player", 0x600);
        Assert(probe.Capture(f.Snapshot).MonsterIdentityCandidates.Single().Status=="ContextUnverified", "non-enemy objects never inspected as monster identity bodies");
    }
internal static void ScalarCandidates()
    {
        var f = new Fixture(); ulong context = f.Object("app.cEnemyContext", 0x1000);
        var monster = new WildsMonsterSnapshot { Id = 21, TargetKey = 3191, Context = context, Health = 500, MaxHealth = 6272 };
        f.Snapshot.Monsters.Add(monster);
        var first = f.Capture(); int baselineReads = first.MemoryReadAttempts;
        for (int offset = 0x10; offset < 0x400; offset += 8) f.M.Int(context + (ulong)offset, offset);
        f.M.Ptr(context + 0x40, 0x123456780); f.M.Int(context + 0x80, -1); f.M.Int(context + 0x88, 0);
        var check = f.Capture(); var identity = check.MonsterIdentityCandidates.Single();
        Assert(identity.ScalarCandidates.Count == 96 && check.MemoryReadAttempts == baselineReads, "candidate cap does not require any extra memory reads");
        Assert(identity.ScalarCandidates.All(c => c.Value != 0 && c.Value != -1 && c.Path != "Context+0x40" && c.Path != "Context+0x44"), "both halves of process pointers, zero and sentinels are omitted");
        Assert(identity.ScalarCandidates.Single(c => c.Path == "Context+0x18").Value == 0x18 && monster.TargetKey == 3191 && monster.Context == context, "offset labels record candidates without changing production identities");
        f.M.Ptr(context, 0); Assert(f.Capture().MonsterIdentityCandidates.Single().ScalarCandidates.Count == 0, "an unverified enemy body never exports scalar fields");
    }
internal static void RelayIdentity()
    {
        var member = new MemberFixture(); var f = member.F;
        ulong matchmake = f.Object("app.net_quest_session.QuestMatchmakeSystem", 0x20);
        f.M.Ptr(f.Quest + 0x1E0, matchmake); f.M.Ptr(matchmake + 0x10, f.Value); f.M.Ptr(f.Quest + 0x1D8, f.Value);
        var probe = f.Probe(); var unknown = probe.ResolveSession(f.Snapshot);
        Assert(unknown.Session?.Length == 64 && !unknown.IsHost.HasValue, "host flag alone does not identify the local player");
        ulong getter = f.Game.Base + 0x1500;
        f.M.Put(getter, new byte[] { 0x8A, 0x42, 0x10, 0x41, 0x88, 0x00, 0xC3 }.Concat(new byte[57]).ToArray());
        f.DefineProperty(member.Member, "IsSelf", "bool", getter, true); f.M.Byte(member.Member + 0x10, 1);
        Assert(probe.ResolveSession(f.Snapshot).IsHost == true, "matching live self and host getters identify the local host");
        f.M.Byte(member.Member + 0x11, 0); Assert(probe.ResolveSession(f.Snapshot).IsHost == false, "a verified local guest stays receive-only");
        f.M.Byte(getter, 0x90); Assert(!probe.ResolveSession(f.Snapshot).IsHost.HasValue, "unknown getter instructions never reuse an older role");
        f.M.Ptr(f.Quest + 0x1D8, f.String("f8111c01-2694-4bb1-bf07-6c3cf437c338"));
        Assert(probe.ResolveSession(f.Snapshot).Session == null, "conflicting paths stop relay grouping");
        f.Snapshot.IsLoading = true; Assert(probe.ResolveSession(f.Snapshot).Session == null, "loading suppresses the current relay identity");
    }
internal static void UserGraph()
    {
        var fixture = new Fixture(); var check = fixture.Capture();
        var assembly = typeof(NativeSessionTests).Assembly;
        using (var reader = new StreamReader(assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(n => n.EndsWith("UserNetworkEvidence.json")))))
        {
            var evidence = JObject.Parse(reader.ReadToEnd());
            Assert((bool)evidence["AllChecksHitReadLimit"] && (int)evidence["CheckCount"] == 24, "independent user evidence retained in tests only");
            foreach (var node in evidence["Nodes"])
                Assert(check.Nodes.Any(n => n.Path == (string)node["Path"] && n.Type == (string)node["Type"] && n.Size == (int)node["Size"]), "actual user path/type/size " + node["Path"]);
            Assert(check.RootCandidateRva == (string)evidence["RootCandidateRva"], "relative root reproduces evidence");
        }
        Assert(check.TraversalCompleted && check.TraversalStopReason == "Completed", "every selected branch completed");
        Assert(check.Nodes[0].Path == "NetworkManager+0x90+0x20", "quest branch gets the first budget");
        Assert(check.MemoryReadAttempts < 512 && check.TypeCacheHits >= 20, "bulk names and repeated type cache: reads=" + check.MemoryReadAttempts + ", hits=" + check.TypeCacheHits);
        Assert(check.SessionStringCandidates.Any(v => v.Path == "NetworkManager+0x90+0x20+0x20" && v.Length == 36), "quest string found despite broad manager noise");
        Assert(check.SessionStringCandidates.Any(v => v.Path.EndsWith("+0x28+0x20")), "native network session wrapper followed");
        Assert(check.ShortSessionStringFields.Single().Length == 8 && check.ShortSessionStringFields.Single().Sha256 == null, "short fields record length only");
        Assert(check.UnfollowedReferences.Any(r => r.Type == "other.network.UnknownSession"), "unsupported network types are visible for the next investigation");
        string json = JsonConvert.SerializeObject(check);
        Assert(!json.Contains(Fixture.SessionId) && !json.Contains(Fixture.PrivateName) && !json.Contains("ABCD1234"), "raw identifiers and player names are excluded");
        Console.WriteLine("NETWORK fixture: " + check.MemoryReadAttempts + " reads, " + check.TypeCacheHits + " cache hits, " + check.Nodes.Count + " nodes");
    }
internal static void Limits()
    {
        var fixture = new Fixture(); var check = fixture.Capture(32);
        Assert(!check.TraversalCompleted && check.TraversalStopReason == "ReadBudget" && check.MemoryReadAttempts == 32, "a partial traversal never appears complete");
        Assert(check.Nodes.All(n => n.ScanStatus != null), "per-object completion status");
        ulong array = fixture.Object("app.net_quest_session.cSessionInfo[]", 0x20);
        fixture.M.Int(array + 0x1C, int.MaxValue); fixture.M.Ptr(fixture.Quest + 0x48, array);
        check = fixture.Capture();
        Assert(!check.TraversalCompleted && check.TraversalStopReason == "BranchLimit" && check.Nodes.Any(n => n.ScanStatus == "ArrayLimit"), "hostile array length is reported and bounded");
        fixture.M.Ptr(fixture.Quest + 0x48, 0); ulong parent = fixture.Quest;
        for (int i = 0; i < 12; i++) { ulong next = fixture.Object("app.net_quest_session.cNestedSession", 0x60); fixture.M.Ptr(parent + 0x50, next); parent = next; }
        check = fixture.Capture();
        Assert(check.Nodes.Any(n => n.ScanStatus == "DepthLimit") && check.MemoryReadAttempts <= 4096 && check.Nodes.Count <= 64, "cycles and nested objects retain hard limits");
        fixture.M.Ptr(fixture.Quest + 0x50, fixture.Object("app.net_quest_session.cLargeSession", 0x800));
        check = fixture.Capture();
        Assert(!check.TraversalCompleted && check.Nodes.Any(n => n.ScanStatus == "FieldSpanLimit"), "a truncated object field span is not reported as complete");
    }
internal static void Properties()
    {
        var fixture = new Fixture();
        ulong fields = fixture.M.Alloc(), list = fixture.M.Alloc(), data = fixture.M.Alloc();
        ulong info = fixture.M.Get(fixture.Quest), definition = fixture.M.Get(info), type = fixture.M.Get(definition + 0x38);
        fixture.M.Ptr(type + 0x58, fields); fixture.M.Ptr(fields + 0x20, list); fixture.M.Ptr(list + 8, data); fixture.M.Int(list + 0x10, 2);
        for (int i = 0; i < 2; i++)
        {
            ulong descriptor = fixture.M.Alloc(); fixture.M.Ptr(data + (ulong)i * 8, descriptor);
            fixture.M.Ptr(descriptor, fixture.Chars(i == 0 ? "SessionId" : "Password"));
            fixture.M.Ptr(descriptor + 0x20, fixture.Chars("System.String"));
            fixture.M.Ptr(descriptor + 0x10, fixture.Game.Base + 0x1000);
            fixture.M.Int(descriptor + 0x28, 1234); fixture.M.Int(descriptor + 0x2C, 3); fixture.M.Int(descriptor + 0x18, 7);
        }
        fixture.M.Put(fixture.Game.Base + 0x1000, Enumerable.Repeat((byte)0x90, 64).ToArray());
        var check = fixture.Capture();
        var metadata = check.TypeMetadata.Single(t => t.Type == "app.net_quest_session.cQuestSession");
        Assert(metadata.Status == "Completed" && metadata.PropertyCount == 2 && metadata.Properties.Single().Name == "SessionId", "only relevant, non-secret descriptors exported");
        Assert(metadata.Properties[0].GetterRva == "0x1000" && metadata.Properties[0].GetterBytes.Split(' ').Length == 64, "getter bytes captured inside verified module bounds");
        Assert(metadata.Properties[0].GetterParameter == 1234 && metadata.Properties[0].Flags == 7 && metadata.Properties[0].VariableType == 3, "generic reflection dispatch parameters are preserved as metadata, not assumed offsets");
        Assert(!JsonConvert.SerializeObject(check).Contains("Password"), "private property excluded");
        fixture.M.Ptr(data, 0); fixture.M.Int(list + 0x10, int.MaxValue);
        Assert(fixture.Capture().TypeMetadata.Single(t => t.Type == metadata.Type).Status == "InvalidCount", "hostile reflection count rejected");
    }
internal static void Freshness()
    {
        var fixture = new Fixture(); var probe = fixture.Probe();
        string first = probe.Capture(fixture.Snapshot).SessionStringCandidates.Single(v => v.Path.EndsWith("+0x20+0x20")).Sha256;
        fixture.M.Put(fixture.Value + 0x14, Encoding.Unicode.GetBytes("f8111c01-2694-4bb1-bf07-6c3cf437c338"));
        string next = probe.Capture(fixture.Snapshot).SessionStringCandidates.Single(v => v.Path.EndsWith("+0x20+0x20")).Sha256;
        Assert(first != next, "values are read again on every capture");
        ulong definition = fixture.M.Get(fixture.M.Get(fixture.Manager)); fixture.M.Int(definition + 0x14, int.MaxValue);
        Assert(probe.Capture(fixture.Snapshot).Status == "RootUnverified", "cached metadata is discarded before another capture");
    }
internal static void NameBoundary()
    {
        var fixture = new Fixture();
        ulong definition = fixture.M.Get(fixture.M.Get(fixture.Manager)), type = fixture.M.Get(definition + 0x38);
        ulong boundaryName = 0x800000 - 4;
        fixture.M.Put(boundaryName, Encoding.ASCII.GetBytes("app.NetworkManager\0")); fixture.M.Ptr(type + 0x20, boundaryName);
        var check = fixture.Capture();
        Assert(check.RootValidationReason == "MetadataValidated" && check.TraversalCompleted && check.MemoryReadAttempts < 600, "partial readable names work without byte-by-byte scanning of every normal name");
    }
internal static void Schema2Graph()
    {
        var fixture = new Fixture(); JObject evidence;
        var assembly = typeof(NativeSessionTests).Assembly;
        using (var input = new StreamReader(assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(n => n.EndsWith("UserNetworkSchema2Evidence.json")))))
            evidence = JObject.Parse(input.ReadToEnd());
        Assert((bool)evidence["AllChecksHitObjectLimit"] && (int)evidence["Samples"] == 16 && (int)evidence["RemainingObjects"] == 60, "independent capture reproduces actual limit");
        Assert((int)evidence["MaximumReads"] < 4096, "the second report was object limited, not read limited");
        var objects = new Dictionary<string, ulong> { { "NetworkManager", fixture.Manager }, { "NetworkManager+0x90", fixture.Service },
            { "NetworkManager+0x90+0x20", fixture.Quest }, { "NetworkManager+0x90+0x18", fixture.Lobby }, { "NetworkManager+0xB8", fixture.Users } };
        foreach (var node in evidence["Nodes"].OrderBy(n => ((string)n["Path"]).Length))
        {
            string path = (string)node["Path"]; if (objects.ContainsKey(path)) continue;
            int last = path.LastIndexOf('+'); string parent = path.Substring(0, last);
            ulong value = fixture.Object((string)node["Type"], (int)node["Size"]);
            fixture.M.Ptr(objects[parent] + Convert.ToUInt64(path.Substring(last + 3), 16), value); objects.Add(path, value);
        }
        // Descendants were starved in the real capture. The existing reference reader
        // NetworkParty chain provides this independent member-list shape.
        ulong users = fixture.Object("app.Net_UserInfoList", 0x20), array = fixture.Object("app.Net_UserInfo[]", 0x20);
        ulong user = fixture.Object("app.Net_UserInfo", 0x20), parameter = fixture.Object("app.Net_UserInfo.cSyntheticParameter", 0xA0);
        fixture.M.Ptr(fixture.Users + 0x18, users); fixture.M.Ptr(users + 0x10, array); fixture.M.Int(array + 0x1C, 4);
        fixture.M.Ptr(array + 0x20, user); fixture.M.Ptr(user + 0x10, parameter);
        fixture.M.Ptr(fixture.Quest + 0x1D8, fixture.Value);
        fixture.M.Ptr(objects["NetworkManager+0x90+0x20+0x1E0"] + 0x10, fixture.Value);
        ulong link = fixture.Object("app.cLinkSession", 472); fixture.M.Ptr(fixture.Service + 0x28, link);
        var check = fixture.Capture();
        Assert(check.SkippedCallbacks >= 25, "empty event handlers do not consume object slots");
        Assert(check.Nodes.Any(n => n.Type == "app.Net_UserInfo.cSyntheticParameter") && check.Nodes.Any(n => n.Type == "app.cLinkSession"), "synthetic members and link-session data are reached");
        Assert(check.TypeMetadata.Any(m => m.Type == "via.network.utility.SessionId"), "SessionId metadata receives priority over unrelated generic accessors");
        Assert(check.QuestSessionCandidate.Status == "MatchingPathsCandidate" && !check.QuestSessionCandidate.ValidatedForSharing, "observed paths are captured independently of traversal");
        Assert(check.Nodes.Count <= 64 && check.MemoryReadAttempts <= 4096, "the original production limits remain unchanged");
    }
internal static void QuestIdentity()
    {
        var fixture = new Fixture(); ulong matchmake = fixture.Object("app.net_quest_session.QuestMatchmakeSystem", 0x20);
        fixture.M.Ptr(fixture.Quest + 0x1E0, matchmake); fixture.M.Ptr(matchmake + 0x10, fixture.Value);
        fixture.M.Ptr(fixture.Quest + 0x1D8, fixture.String(Fixture.SessionId.ToUpperInvariant()));
        var probe = fixture.Probe(); var first = probe.Capture(fixture.Snapshot).QuestSessionCandidate;
        Assert(first.Status == "MatchingPathsCandidate" && first.Sha256?.Length == 64 && !first.ValidatedForSharing, "normalized GUID agreement is local evidence only");
        fixture.M.Ptr(fixture.Quest + 0x1D8, 0);
        Assert(probe.Capture(fixture.Snapshot).QuestSessionCandidate.Status == "SecondaryOnlyCandidate", "one path is explicitly uncorroborated");
        fixture.M.Ptr(fixture.Quest + 0x1D8, fixture.String("f8111c01-2694-4bb1-bf07-6c3cf437c338"));
        var conflict = probe.Capture(fixture.Snapshot).QuestSessionCandidate;
        Assert(conflict.Status == "ConflictingPaths" && conflict.Sha256 == null, "mismatched transition IDs do not produce a grouping fingerprint");
        fixture.M.Ptr(matchmake + 0x10, fixture.String(Guid.Empty.ToString("D")));
        fixture.M.Ptr(fixture.Quest + 0x1D8, fixture.String("this-is-a-player-name-not-a-valid-uuid"));
        var empty = probe.Capture(fixture.Snapshot).QuestSessionCandidate;
        Assert(empty.Status == "Unavailable" && empty.PrimaryState == "InvalidFormat" && empty.SecondaryState == "EmptyGuid" && empty.Sha256 == null, "invalid and all-zero IDs are rejected");
        string json = JsonConvert.SerializeObject(probe.Capture(fixture.Snapshot));
        Assert(!json.Contains("this-is-a-player-name") && !json.Contains(Fixture.SessionId), "raw values remain excluded");
        fixture.M.Ptr(fixture.Quest + 0x1D8, 0); fixture.M.Ptr(fixture.Quest + 0x1E0, fixture.Object("app.net_quest_session.cUnrelated", 0x20));
        Assert(probe.Capture(fixture.Snapshot).QuestSessionCandidate.Status == "Unavailable", "candidate caches never reuse an old key after a session disappears");
    }
internal static void QuestLifecycle()
    {
        var fixture = new Fixture(); ulong matchmake = fixture.Object("app.net_quest_session.QuestMatchmakeSystem", 0x20);
        fixture.M.Ptr(fixture.Quest + 0x1E0, matchmake); fixture.M.Ptr(matchmake + 0x10, fixture.Value); fixture.M.Ptr(fixture.Quest + 0x1D8, fixture.Value);
        var probe = fixture.Probe(); var snapshot = fixture.Snapshot;
        var active = probe.Capture(snapshot).QuestSessionCandidate;
        Assert(active.CurrentCandidateSha256 == active.PrimarySha256 && active.CurrentState == "CurrentFieldsAgree", "live matching fields have a current candidate");
        snapshot.QuestSuccessState = 1; snapshot.IsQuestDamageFrozen = true; fixture.M.Ptr(fixture.Quest + 0x1D8, 0);
        var ended = probe.Capture(snapshot).QuestSessionCandidate;
        Assert(ended.SecondarySha256 == active.PrimarySha256 && ended.CurrentCandidateSha256 == null && ended.CurrentState == "QuestEndedOrLoading", "the observed retained matchmake value is historical after quest completion");
        snapshot.QuestSuccessState = 0; snapshot.IsQuestDamageFrozen = false;
        var idle = probe.Capture(snapshot).QuestSessionCandidate;
        Assert(idle.CurrentCandidateSha256 == null && idle.CurrentState == "NoPrimaryField", "cleared flags cannot reactivate a retained old ID");
        ulong next = fixture.String("f8111c01-2694-4bb1-bf07-6c3cf437c338"); fixture.M.Ptr(matchmake + 0x10, next); fixture.M.Ptr(fixture.Quest + 0x1D8, next);
        var changed = probe.Capture(snapshot).QuestSessionCandidate;
        Assert(changed.CurrentCandidateSha256 != active.CurrentCandidateSha256 && changed.CurrentCandidateSha256 != null, "a new quest candidate does not reuse the previous key");
        snapshot.IsLoading = true;
        Assert(probe.Capture(snapshot).QuestSessionCandidate.CurrentCandidateSha256 == null, "even matching fields are not current during loading");
        // Replay the independent 24-sample user trace with synthetic GUIDs;
        // raw user IDs were never present in the supplied report.
        var assembly = typeof(NativeSessionTests).Assembly; JObject evidence;
        using (var input = new StreamReader(assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(n => n.EndsWith("UserNetworkSchema3Evidence.json")))))
            evidence = JObject.Parse(input.ReadToEnd());
        var ids = new Dictionary<string, string>(); var currentKeys = new HashSet<string>(); var observedKeys = new HashSet<string>();
        snapshot.IsLoading = false;
        foreach (var sample in evidence["Lifecycle"])
        {
            var original = sample["QuestSessionCandidate"]; string hash = (string)original["Sha256"];
            if (!ids.ContainsKey(hash)) ids[hash] = "00000000-0000-4000-8000-" + (ids.Count + 1).ToString("x12");
            ulong value = fixture.String(ids[hash]); fixture.M.Ptr(matchmake + 0x10, value);
            fixture.M.Ptr(fixture.Quest + 0x1D8, (string)original["PrimaryState"] == "GuidCandidate" ? value : 0);
            snapshot.QuestSuccessState = (int)sample["SuccessState"]; snapshot.QuestFailureState = (int)sample["FailureState"]; snapshot.IsQuestDamageFrozen = (bool)sample["DamageFrozen"];
            var replay = probe.Capture(snapshot).QuestSessionCandidate;
            bool expected = (string)original["Status"] == "MatchingPathsCandidate" && !snapshot.IsQuestDamageFrozen && snapshot.QuestSuccessState == 0 && snapshot.QuestFailureState == 0;
            Assert((replay.CurrentCandidateSha256 != null) == expected, "real trace current/retained distinction");
            if (replay.CurrentCandidateSha256 != null) currentKeys.Add(replay.CurrentCandidateSha256);
            if (replay.Sha256 != null) observedKeys.Add(replay.Sha256);
        }
        Assert(currentKeys.Count == 2 && observedKeys.Count == 3, "the trace contains two current quest candidates and one matchmaking-only candidate");
    }
internal static void TargetedMembers()
    {
        var fixture = new MemberFixture(); var check = fixture.F.Capture();
        Assert(check.QuestMembers.UserListStatus == "Completed" && check.QuestMembers.UserArrayLength == 4 && check.QuestMembers.Users.Count == 1, "trusted typed owner and array bounds work without array type metadata");
        Assert(check.QuestMembers.Users[0].Type == "app.Net_UserInfo" && check.QuestMembers.Users[0].ParameterType == "app.Net_UserInfo.cSyntheticParameter", "parameter type is discovered rather than assuming an enum is a class");
        var member = check.QuestMembers.NativeMembers.Single();
        Assert(check.QuestMembers.NativeTableStatus == "Completed" && member.NativeIndex == 2 && member.IsHost == true && member.Status == "GetterVerifiedFields", "actual user-provided getter instruction shapes yield index +0x14 and host +0x11");
        Assert(member.UniqueIdType == "NativeOrUnverified" && member.UniqueIdVtableRva == "0x4000", "native identity pointer is type evidence only, with no guessed identifier");
        fixture.F.Snapshot.Players.Add(new WildsPlayerSnapshot { Slot = 0 }); fixture.F.Snapshot.Players.Add(new WildsPlayerSnapshot { Slot = 1, IsNpc = true });
        check = fixture.F.Capture();
        Assert(check.ReportedHumanCount == 1 && check.ReportedNpcCount == 1 && !check.QuestMembers.ValidatedForSharing, "NPC projections and raw member objects are not human identity proof");
        string json = JsonConvert.SerializeObject(check);
        Assert(!json.Contains(Fixture.PrivateName) && !json.Contains(Fixture.SessionId), "focused diagnostics retain no raw names or IDs");
    }
internal static void TargetedLimits()
    {
        var fixture = new MemberFixture(); fixture.F.M.Int(fixture.Array + 0x1C, int.MaxValue);
        Assert(fixture.F.Capture().QuestMembers.UserListStatus == "UnexpectedArrayCapacity", "hostile array length never triggers a broad scan");
        fixture.F.M.Int(fixture.Array + 0x1C, 4); fixture.F.M.Byte(fixture.Member + 0x11, 2);
        Assert(fixture.F.Capture().QuestMembers.NativeTableStatus == "Partial", "nonboolean host flags are rejected");
        fixture.F.M.Byte(fixture.Member + 0x11, 1); fixture.F.M.Byte(fixture.F.Game.Base + 0x1100, 0x90);
        Assert(fixture.F.Capture().QuestMembers.NativeMembers.Single().Status == "GetterNotRecognized", "changed getter code cannot silently reuse offsets");
        var racing = new ReadRace(fixture.F.M, fixture.Array + 0x1C);
        var check = new WildsNetworkProbe(racing, fixture.F.Game.Addresses, fixture.F.Game.Base, moduleSize: 0x5000).Capture(fixture.F.Snapshot);
        Assert(check.QuestMembers.UserListStatus == "ChangedDuringRead" && check.QuestMembers.Users.Count == 0, "a replaced array does not export old member rows");
        var racingTable = new TableReadRace(fixture.F.M, fixture.Table + 0x10);
        check = new WildsNetworkProbe(racingTable, fixture.F.Game.Addresses, fixture.F.Game.Base, moduleSize: 0x5000).Capture(fixture.F.Snapshot);
        Assert(check.QuestMembers.NativeTableStatus == "ChangedDuringRead" && check.QuestMembers.NativeMembers.Count == 0, "a replaced native table discards member rows");
        for (int i = 1; i < 5; i++) fixture.F.M.Ptr(fixture.Table + 0x10 + (ulong)i * 8, fixture.Member);
        Assert(fixture.F.Capture().QuestMembers.NativeTableStatus == "UnexpectedObjectCount", "unexpected native roster size is explicit and bounded");
        Assert(fixture.F.Capture(32).MemoryReadAttempts == 32, "focused reads share the unchanged global read cap");
    }
    static JObject Evidence4()
    {
        var assembly = typeof(NativeSessionTests).Assembly;
        using (var input = new StreamReader(assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(n => n.EndsWith("UserNetworkSchema4Evidence.json"))))) return JObject.Parse(input.ReadToEnd());
    }
internal static void DerivedUsers()
    {
        var fixture = new MemberFixture(); var f = fixture.F; var kind = Evidence4()["UserKinds"].Single();
        ulong oldUser = f.M.Get(fixture.Array + 0x20), parameter = f.M.Get(oldUser + 0x10);
        ulong derived = f.Object((string)kind["Type"], (int)kind["Size"]); f.M.Ptr(fixture.Array + 0x20, derived); f.M.Ptr(derived + 0x10, parameter);
        var unverified = f.Capture().QuestMembers;
        Assert(unverified.UserListStatus == "Completed" && unverified.Users.Single().Type == "app.Net_QuestUserInfo", "actual 56-byte quest-user objects are not rejected as an unexpected base name");
        Assert(!unverified.Users[0].BaseLayoutVerified && unverified.Users[0].ParameterStatus == "BaseLayoutUnverified" && unverified.Users[0].ParameterType == null, "a recognized subtype alone cannot authorize reading base offsets");
        f.SetBase(derived, oldUser); var probe = f.Probe(); var verified = probe.Capture(f.Snapshot).QuestMembers.Users.Single();
        Assert(verified.BaseLayoutVerified && verified.Hierarchy.BaseTypes.Single().Name == "app.Net_UserInfo" && verified.ParameterType == "app.Net_UserInfo.cSyntheticParameter", "live inheritance permits the existing base-chain field");
        ulong root=f.Object("via.clr.ManagedObject",16);f.SetBase(oldUser,root);f.M.Ptr(f.M.Get(f.M.Get(f.M.Get(root))+0x38)+0x40,ulong.MaxValue);
        var terminal=f.Capture().QuestMembers.Users.Single();Assert(terminal.BaseLayoutVerified && terminal.ParameterType!=null,"known 16-byte ManagedObject root terminates before unrelated metadata");
        f.SetBase(derived, derived); var cycle = probe.Capture(f.Snapshot).QuestMembers.Users.Single();
        Assert(cycle.Hierarchy.Status == "InvalidBaseChain" && !cycle.BaseLayoutVerified && cycle.ParameterType == null, "cycles are rejected and type evidence is refreshed for each capture");
        ulong largeBase = f.Object("app.Net_UserInfo.cOversizedBase", 128); f.SetBase(derived, largeBase);
        Assert(f.Capture().QuestMembers.Users[0].Hierarchy.Status == "InvalidBaseType", "a base larger than the derived object is invalid");
        f.M.Ptr(fixture.Array + 0x20, f.Object("app.Net_UnrelatedUser", 56));
        Assert(f.Capture().QuestMembers.UserListStatus == "Partial", "unobserved unrelated class names remain rejected");
    }
internal static void IdentityMetadata()
    {
        var fixture = new MemberFixture(); var f = fixture.F;
        ulong identity = f.Object((string)Evidence4()["NativeKinds"][0], 96); f.M.Ptr(fixture.Member + 0x98, identity);
        byte[] code = ((string)Evidence4()["IdentityWrapper"]["GetterBytes"]).Split(' ').Select(s => Convert.ToByte(s, 16)).ToArray();
        ulong getter = f.Game.Base + 0x1800, helper = f.Game.Base + 0x3000;
        Array.Copy(BitConverter.GetBytes(checked((int)(helper - getter - 16))), 0, code, 12, 4);
        f.DefineProperty(identity, "Valid", "bool", getter); f.M.Put(getter, code); f.M.Put(helper, Enumerable.Repeat((byte)0xC3, 128).ToArray());
        var check = f.Capture(); var metadata = check.TypeMetadata.Single(m => m.Type == "via.network.utility.UniqueId");
        Assert(check.QuestMembers.NativeMembers.Single().UniqueIdSize == 96 && metadata.Properties.Single().GetterHelperRva == "0x3000", "the actual identity type is queued for reflection and its wrapper is relocated correctly");
        Assert(metadata.Properties[0].GetterHelperBytes.Split(' ').Length == 128 && check.MemoryReadAttempts <= 4096, "one bounded helper window shares the global read cap");
        Array.Copy(BitConverter.GetBytes(int.MaxValue), 0, code, 12, 4); f.M.Put(getter, code);
        Assert(f.Capture().TypeMetadata.Single(m => m.Type == metadata.Type).Properties[0].GetterHelperBytes == null, "an out-of-module helper is never read");
        code[11] = 0x90; f.M.Put(getter, code);
        Assert(f.Capture().TypeMetadata.Single(m => m.Type == metadata.Type).Properties[0].GetterHelperRva == null, "arbitrary instructions are not treated as calls");
    }
    sealed class ReadRace : IWildsMemory
    {
        readonly FakeMemory source; readonly ulong length;
        public ReadRace(FakeMemory source, ulong length) { this.source = source; this.length = length; }
        public bool Read(ulong address, byte[] buffer, int size)
        { bool result = source.Read(address, buffer, size); if (address == length && size == 4) Array.Copy(BitConverter.GetBytes(3), buffer, 4); return result; }
    }
    sealed class TableReadRace : IWildsMemory
    {
        readonly FakeMemory source; readonly ulong table; int reads;
        public TableReadRace(FakeMemory source, ulong table) { this.source = source; this.table = table; }
        public bool Read(ulong address, byte[] buffer, int size)
        { bool result = source.Read(address, buffer, size); if (address == table && size == 512 && ++reads > 1) System.Array.Clear(buffer, 0, size); return result; }
    }
    sealed class MemberFixture
    {
        public readonly Fixture F = new Fixture();
        public readonly ulong Array, Table, Member;
        public MemberFixture()
        {
            ulong list = F.Object("app.Net_UserInfoList", 40), user = F.Object("app.Net_UserInfo", 32), parameter = F.Object("app.Net_UserInfo.cSyntheticParameter", 0xA0);
            Array = F.M.Array(user, 0, 0, 0); F.M.Ptr(F.Users + 0x18, list); F.M.Ptr(list + 0x10, Array); F.M.Ptr(user + 0x10, parameter);
            Table = F.Object("via.network.session.MemberInfoTbl", 528); Member = F.Object("via.network.session.MemberInfo", 160);
            F.M.Ptr(F.Quest + 0xA8, Table); F.M.Ptr(Table + 0x10, Member); F.M.Int(Member + 0x14, 2); F.M.Byte(Member + 0x11, 1);
            ulong native = F.M.Alloc(); F.M.Ptr(native, F.Game.Base + 0x4000); F.M.Ptr(Member + 0x98, native);
            var assembly = typeof(NativeSessionTests).Assembly; JObject evidence;
            using (var input = new StreamReader(assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(n => n.EndsWith("UserNetworkSchema3Evidence.json")))))
                evidence = JObject.Parse(input.ReadToEnd());
            var properties = evidence["MemberProperties"];
            ulong fields = F.M.Alloc(), listData = F.M.Alloc(), data = F.M.Alloc();
            ulong definition = F.M.Get(F.M.Get(Member)), type = F.M.Get(definition + 0x38);
            F.M.Ptr(type + 0x58, fields); F.M.Ptr(fields + 0x20, listData); F.M.Ptr(listData + 8, data); F.M.Int(listData + 0x10, properties.Count());
            int i = 0;
            foreach (var prop in properties)
            {
                ulong desc = F.M.Alloc(), code = F.Game.Base + 0x1100 + (ulong)i * 0x100;
                F.M.Ptr(data + (ulong)i * 8, desc); F.M.Ptr(desc, F.Chars((string)prop["Name"])); F.M.Ptr(desc + 0x20, F.Chars((string)prop["Type"]));
                F.M.Ptr(desc + 0x10, code); F.M.Put(code, ((string)prop["GetterBytes"]).Split(' ').Select(s => Convert.ToByte(s, 16)).ToArray()); i++;
            }
        }
    }
    sealed class Fixture
    {
        public const string SessionId = "e8111c01-2694-4bb1-bf07-6c3cf437c338", PrivateName = "Never export this player's name";
        public readonly FakeGame Game = new FakeGame();
        public FakeMemory M => Game.M;
        readonly Dictionary<string, ulong> types = new Dictionary<string, ulong>();
        public readonly ulong Manager, Service, Quest, Lobby, Users, Value;
        public readonly WildsSnapshot Snapshot = new WildsSnapshot { IsReady = true, AllowCampDisplay = true, StageId = 3 };
        public Fixture()
        {
            Manager = Object("app.NetworkManager", 464); Service = Object("app.Net_SessionService", 104);
            Quest = Object("app.net_quest_session.cQuestSession", 600); Lobby = Object("app.net_lobby_session.cLobbySession", 496); Users = Object("app.Net_UserInfoManager", 96);
            M.Ptr(Game.A("PlayerManager") - 0x46010, Manager); M.Ptr(Manager + 0x90, Service); M.Ptr(Manager + 0xB8, Users);
            M.Ptr(Service + 0x20, Quest); M.Ptr(Service + 0x18, Lobby);
            Value = String(SessionId); M.Ptr(Quest + 0x20, Value); M.Ptr(Quest + 0x30, String("ABCD1234"));
            ulong wrapper = Object("ace_network.cNetworkSession", 0x40); M.Ptr(Quest + 0x28, wrapper); M.Ptr(wrapper + 0x20, String(SessionId));
            M.Ptr(Quest + 0x38, Object("other.network.UnknownSession", 0x20)); M.Ptr(Users + 0x20, String(PrivateName));
            // Many manager fields share a long type name in real RE Engine
            // graphs. Previously its text was read one byte at a time per object.
            string noise = "app." + new string('X', 160);
            for (int offset = 0x10; offset < 0x110; offset += 8)
                if (offset != 0x90 && offset != 0xB8) M.Ptr(Manager + (ulong)offset, Object(noise, 0x20));
        }
        public ulong Chars(string value) { ulong p = M.Alloc(); M.Put(p, Encoding.ASCII.GetBytes(value + "\0")); return p; }
        public ulong Object(string name, int size) { ulong p = M.Alloc(); Mark(p, name, size); return p; }
        ulong Type(ulong obj) => M.Get(M.Get(M.Get(obj)) + 0x38);
        public void SetBase(ulong obj, ulong baseObject) => M.Ptr(Type(obj) + 0x40, Type(baseObject));
        public void DefineProperty(ulong obj, string name, string kind, ulong getter, bool append = false)
        {
            ulong previous = append ? M.Get(M.Get(Type(obj) + 0x58) + 0x20) : 0;
            int count = previous == 0 ? 0 : (int)M.Get(previous + 0x10);
            ulong oldData = previous == 0 ? 0 : M.Get(previous + 8);
            ulong fields = M.Alloc(), list = M.Alloc(), data = M.Alloc(), descriptor = M.Alloc();
            M.Ptr(Type(obj) + 0x58, fields); M.Ptr(fields + 0x20, list); M.Ptr(list + 8, data); M.Int(list + 0x10, count + 1);
            for (int i = 0; i < count; i++) M.Ptr(data + (ulong)i * 8, M.Get(oldData + (ulong)i * 8));
            M.Ptr(data + (ulong)count * 8, descriptor);
            M.Ptr(descriptor, Chars(name)); M.Ptr(descriptor + 0x20, Chars(kind)); M.Ptr(descriptor + 0x10, getter);
        }
        void Mark(ulong obj, string name, int size)
        {
            if (!types.TryGetValue(name, out ulong info))
            {
                info = M.Alloc(); ulong definition = M.Alloc(), type = M.Alloc();
                M.Ptr(info, definition); M.Ptr(definition + 0x40, info); M.Ptr(definition + 0x38, type);
                M.Int(definition + 0x14, size); M.Int(type + 0x30, size); M.Ptr(type + 0x20, Chars(name)); types.Add(name, info);
            }
            M.Ptr(obj, info);
        }
        public ulong String(string value) { ulong p = M.String(value); Mark(p, "System.String", 0x14); return p; }
        public WildsNetworkProbe Probe(int limit = 4096) => new WildsNetworkProbe(M, Game.Addresses, Game.Base, limit, 0x5000);
        public WildsNetworkCheck Capture(int limit = 4096) => Probe(limit).Capture(Snapshot);
    }
}

static class SessionMetadataTests
{
    static void Managed(FakeMemory m, ulong obj, string name, int size)
    {
        ulong info = m.Alloc(), definition = m.Alloc(), type = m.Alloc(), chars = m.Alloc();
        m.Ptr(obj, info); m.Ptr(info, definition); m.Ptr(definition + 0x40, info); m.Ptr(definition + 0x38, type);
        m.Int(definition + 0x14, size); m.Int(type + 0x30, size); m.Ptr(type + 0x20, chars);
        m.Put(chars, Encoding.ASCII.GetBytes(name + "\0"));
    }
internal static void NetworkProbe()
    {
        var game = new FakeGame(); var probe = new WildsNetworkProbe(game.M, game.Addresses, game.Base);
        ulong root = game.A("PlayerManager") - 0x46010;
        Assert(probe.Capture(game.Read()).Status == "RootUnverified", "unreadable candidate cannot become a session identity");
        ulong network = game.M.Alloc(), session = game.M.Alloc(), quest = game.M.Alloc(), users = game.M.Alloc();
        Managed(game.M, network, "app.NetworkManager", 0x100);
        Managed(game.M, session, "app.net_session_manager.SessionManager", 0x80);
        Managed(game.M, quest, "app.net_quest_session.cQuestSession", 0x80);
        Managed(game.M, users, "app.Net_UserInfoManager", 0x50);
        game.M.Ptr(root, network); game.M.Ptr(network + 0xA0, session); game.M.Ptr(network + 0xB8, users);
        game.M.Ptr(session + 0x28, quest); game.M.Ptr(quest + 0x10, session); // deliberate cycle
        string sessionValue = "e8111c01-2694-4bb1-bf07-6c3cf437c338", privateName = "Do not include this player name";
        ulong text = game.M.String(sessionValue), name = game.M.String(privateName);
        Managed(game.M, text, "System.String", 0x14); Managed(game.M, name, "System.String", 0x14);
        game.M.Ptr(quest + 0x40, text); game.M.Ptr(users + 0x20, name);
        var check = probe.Capture(game.Read()); string json = JsonConvert.SerializeObject(check);
        Assert(check.Status == "MetadataVerifiedCandidatesOnly" && check.Nodes.Count == 4, "validated metadata graph with cycle suppression");
        Assert(check.Nodes.Any(n => n.Path == "NetworkManager+0xA0+0x28" && n.Type == "app.net_quest_session.cQuestSession"), "actual relative pointer path");
        Assert(check.SessionStringCandidates.Count == 1 && check.SessionStringCandidates[0].Sha256.Length == 64, "session fingerprint only");
        Assert(!json.Contains(sessionValue) && !json.Contains(privateName) && !json.Contains(game.Read().PlayerName), "raw IDs and names excluded");
        Assert(check.MemoryReadAttempts <= 4096 && check.Nodes.Count <= 64, "strict memory and object budgets");
        game.M.Ptr(root, users);
        Assert(probe.Capture(game.Read()).Nodes.Count == 0, "wrong root type rejected");
        game.M.Ptr(root, network);
        ulong info = game.M.Get(network), definition = game.M.Get(info); game.M.Int(definition + 0x14, int.MaxValue);
        Assert(probe.Capture(game.Read()).Nodes.Count == 0, "corrupt metadata rejected");
    }
}
