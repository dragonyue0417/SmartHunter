// Read-only diagnostic metadata layouts researched from praydog/REFramework.
// These candidates are never used to group players or change displayed game data.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Diagnostics;
using TypeInfo = SmartHunter.Wilds.WildsNetworkTypeInfo;

namespace SmartHunter.Wilds
{
    public sealed class WildsNetworkNode
    {
        public string Path, Type, ScanStatus;
        public int Size;
    }
    public sealed class WildsNetworkValue
    {
        public string Path, Sha256;
        public int Length;
    }
    public sealed class WildsNetworkReference
    {
        public string Path, Type, Reason;
        public int Size;
    }
    public sealed class WildsQuestSessionCandidate
    {
        public string Status = "Unavailable", PrimaryState = "Unavailable", SecondaryState = "Unavailable", Sha256;
        public string PrimaryPath = "NetworkManager+0x90+0x20+0x1D8";
        public string SecondaryPath = "NetworkManager+0x90+0x20+0x1E0+0x10";
        public string CurrentCandidateSha256, PrimarySha256, SecondarySha256, CurrentState = "Unavailable";
        public bool ValidatedForSharing = false;
    }
    public sealed class WildsNativeSessionCandidate
    {
        public string Path,Type,Status="Unavailable",Sha256;
        public int ObjectSize,BufferLength;
        public bool ValidatedForSharing=false;
    }
    public sealed class WildsNetworkCheck
    {
        public DateTime TimestampUtc = DateTime.UtcNow;
        public string Status, RootCandidateRva, RootValidationReason, TraversalStopReason;
        public string FieldLayoutStatus, RootAddressSource;
        public int StageId, PartyCount, SuccessState, FailureState;
        public int ReportedHumanCount, ReportedNpcCount;
        public bool InHuntingZone, IsLoading, CanDisplayData, DamageFrozen, TraversalCompleted;
        public float QuestElapsed;
        public List<WildsNetworkNode> Nodes = new List<WildsNetworkNode>();
        public List<WildsNativeSessionCandidate> NativeSessionCandidates = new List<WildsNativeSessionCandidate>();
        public List<WildsNetworkValue> SessionStringCandidates = new List<WildsNetworkValue>();
        public List<WildsNetworkValue> ShortSessionStringFields = new List<WildsNetworkValue>();
        public List<WildsNetworkReference> UnfollowedReferences = new List<WildsNetworkReference>();
        public List<WildsNetworkTypeMetadata> TypeMetadata = new List<WildsNetworkTypeMetadata>();
        public WildsQuestSessionCandidate QuestSessionCandidate = new WildsQuestSessionCandidate();
        public WildsNetworkMembersReport QuestMembers = new WildsNetworkMembersReport();
        public List<WildsNetworkMonsterCheck> MonsterStates = new List<WildsNetworkMonsterCheck>();
        public List<WildsMonsterIdentityCheck> MonsterIdentityCandidates = new List<WildsMonsterIdentityCheck>();
        public int MonsterCount, IdentityStartIndex;
        public string IdentityPurpose = "Unverified object-block hashes and getter code for paired-client comparison only; never used as automatic identity.";
        public int[] DamageBySlot;
        public string RelayState;
        public WildsRelayMatchReport RelayMatch;
        public WildsRelayConnectionReport RelayConnection;
        public WildsReadStatistics ReadStatistics;
        public int MemoryReadAttempts, TypeCacheHits, RemainingObjects, SkippedCallbacks;
        public long MemoryBytesRead, ElapsedMilliseconds;
    }
    public sealed class WildsNetworkMonsterCheck
    {
        public int Id, TargetKey, Size, Role, Legendary;
        public float Health, MaxHealth, Distance;
        public bool Selected;
        public List<WildsRelayPart> Parts;
        public List<WildsNetworkAilmentCheck> Ailments;
    }
    public sealed class WildsNetworkAilmentCheck
    {
        public int Id;
        public bool Active;
        public float Buildup, MaxBuildup, Remaining, MaxDuration;
    }
    public sealed class WildsNetworkReport
    {
        public int Schema = 9;
        public string AppVersion = typeof(WildsNetworkReport).Assembly.GetName().Version.ToString();
        public string GameVersion;
        public string Purpose = "DiagnosticOnly: candidate paths and fingerprints; optional host-only part relay is configured separately and cross-client identity requires live validation.";
        public List<WildsNetworkCheck> Checks = new List<WildsNetworkCheck>();
    }
    public sealed class WildsNetworkProbe
    {
        sealed class BudgetMemory : IWildsMemory
        {
            readonly IWildsMemory source;
            readonly int limit;
            public int Attempts;
            public long BytesRead;
            public bool Exhausted => Attempts >= limit;
            public BudgetMemory(IWildsMemory source, int limit) { this.source = source; this.limit = limit; }
            public bool Read(ulong address, byte[] buffer, int size)
            {
                if (Exhausted) return false;
                Attempts++;
                if (!Pointer(address) || size < 0 || size > 4096 || !source.Read(address, buffer, size)) return false;
                BytesRead += size; return true;
            }
        }
        sealed class Pending { public ulong Address; public string Path; public int Depth, Priority; public TypeInfo Type; }
        readonly IWildsMemory source;
        readonly WildsAddresses layout;
        readonly ulong module;
        readonly int readLimit, moduleSize;
        int identityCursor;
        static bool Pointer(ulong p) => p >= 0x10000 && p <= 0x00007FFFFFFFF000;
        public WildsNetworkProbe(IWildsMemory memory, WildsAddresses addresses, ulong moduleBase, int readLimit = 4096, int moduleSize = 0)
        { source = memory; layout = addresses; module = moduleBase; this.readLimit = Math.Max(32, Math.Min(4096, readLimit)); this.moduleSize = Math.Max(0, moduleSize); }
        static bool NetworkType(string name) => name.IndexOf("network", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("session", StringComparison.OrdinalIgnoreCase) >= 0 || name.Contains("app.Net_");
        static bool Follow(string name) => name == "app.cLinkSession" || NetworkType(name) && (name.StartsWith("app.net_", StringComparison.Ordinal) || name.StartsWith("app.Net_", StringComparison.Ordinal) ||
            name.StartsWith("ace_network.", StringComparison.Ordinal) || name.StartsWith("via.network.", StringComparison.Ordinal) ||
            name.StartsWith("System.Collections.Generic.List", StringComparison.Ordinal) || name.StartsWith("ace.cLimitedArray", StringComparison.Ordinal));
        static bool Callback(TypeInfo type) => type.Size <= 16 && type.Name.StartsWith("app.net_session_manager.on", StringComparison.Ordinal);
        static int Priority(string name)
        {
            if (name == "app.net_quest_session.cQuestSession") return -100;
            if (name == "app.Net_UserInfoManager") return -90;
            if (name == "app.Net_SessionService") return -80;
            if (name == "app.net_lobby_session.cLobbySession") return -70;
            if (name == "app.cLinkSession") return -65;
            if (name == "app.NetworkManager") return -60;
            if (name.Contains("SessionId") || name.Contains("UniqueId") || name.Contains("Net_UserInfo") || name.Contains("Net_QuestUserInfo") || name.Contains("MemberInfo")) return -50;
            if (name == "app.net_quest_session.cSessionInfo" || name.Contains("QuestMatchmakeSystem")) return -40;
            if (name.Contains("SearchResult") || name.Contains("CloudSessionRequest")) return 100;
            return 0;
        }
        public WildsSessionIdentity ResolveSession(WildsSnapshot snapshot)
        {
            var result = new WildsSessionIdentity();
            if (!snapshot.IsReady || snapshot.IsLoading || snapshot.IsQuestDamageFrozen || snapshot.QuestSuccessState != 0 || snapshot.QuestFailureState != 0 || !layout.HasAddress("PlayerManager")) return result;
            var limited = new BudgetMemory(source, 512); var m = new WildsMemoryView(limited); var inspect = new WildsNetworkMetadata(m, () => limited.Exhausted);
            ulong playerRoot = layout.Address("PlayerManager", module);
            // A scanned game version need not be whitelisted. The relative
            // network slot is accepted only after live type and session checks.
            if (playerRoot < module || playerRoot - module < 0x46010) return result;
            ulong manager = m.Ptr(playerRoot - 0x46010);
            if (inspect.Inspect(manager)?.Name != "app.NetworkManager") return result;
            ulong service = m.Ptr(manager + 0x90);
            if (inspect.Inspect(service)?.Name != "app.Net_SessionService") return result;
            ulong quest = m.Ptr(service + 0x20);
            if (inspect.Inspect(quest)?.Name != "app.net_quest_session.cQuestSession") return result;
            result.Session = ReadQuestCandidate(m, inspect, quest, snapshot).CurrentCandidateSha256;
            if (result.Session == null) return result;
            var members = new WildsNetworkMembers(m, inspect, () => limited.Exhausted, module, moduleSize); members.ReadNative(quest);
            // Do not combine a session captured before a transition with roles
            // from the next party. Re-read live pointers and both GUID paths.
            if (limited.Exhausted || m.Ptr(playerRoot - 0x46010) != manager || m.Ptr(manager + 0x90) != service ||
                m.Ptr(service + 0x20) != quest || ReadQuestCandidate(m, inspect, quest, snapshot).CurrentCandidateSha256 != result.Session)
                return new WildsSessionIdentity();
            var self = members.Report.NativeMembers.Where(p => p.IsSelf == true).ToArray();
            if (members.Report.NativeTableStatus == "Completed" && self.Length == 1)
            {
                result.IsHost = self[0].IsHost;
                var hosts = members.Report.NativeMembers.Where(p => p.IsHost == true).ToArray();
                if (hosts.Length == 1) result.HostIndex = hosts[0].NativeIndex;
            }
            return result;
        }
        public WildsNetworkCheck Capture(WildsSnapshot snapshot)
        {
            var watch = Stopwatch.StartNew();
            var limited = new BudgetMemory(source, readLimit); var m = new WildsMemoryView(limited);
            var inspect = new WildsNetworkMetadata(m, () => limited.Exhausted);
            var result = new WildsNetworkCheck
            {
                Status = "RootUnverified", TraversalStopReason = "RootUnverified", StageId = snapshot.StageId, InHuntingZone = snapshot.InHuntingZone,
                FieldLayoutStatus = WildsMemoryLayout.Supports(layout.Version) ? "KnownGameVersion" : "UnverifiedGameVersion",
                RootAddressSource = "PlayerManagerRelativeCandidate",
                IsLoading = snapshot.IsLoading, CanDisplayData = snapshot.CanDisplayData,
                PartyCount = snapshot.Players.Count, SuccessState = snapshot.QuestSuccessState, FailureState = snapshot.QuestFailureState,
                ReportedHumanCount = snapshot.Players.Count(p => !p.IsNpc), ReportedNpcCount = snapshot.Players.Count(p => p.IsNpc),
                DamageFrozen = snapshot.IsQuestDamageFrozen, QuestElapsed = snapshot.QuestElapsed,
                DamageBySlot = Enumerable.Range(0, 4).Select(i => snapshot.Players.FirstOrDefault(p => p.Slot == i)?.Damage ?? 0).ToArray()
            };
            result.MonsterStates = snapshot.Monsters.Take(64).Select(monster => new WildsNetworkMonsterCheck {
                Id = monster.Id, TargetKey = monster.TargetKey, Size = monster.Size, Role = monster.Role, Legendary = monster.Legendary, Health = monster.Health, MaxHealth = monster.MaxHealth, Distance = monster.Distance, Selected = monster.Selected,
                Parts = monster.Parts.Take(128).Select(p => new WildsRelayPart { Slot = p.Slot, NameId = p.NameId, Severable = p.Severable, Health = p.Health, MaxHealth = p.MaxHealth, Count = p.Count }).ToList(),
                Ailments = monster.Ailments.Take(64).Select(a => new WildsNetworkAilmentCheck { Id = a.Id, Active = a.Active, Buildup = a.Buildup, MaxBuildup = a.MaxBuildup, Remaining = a.Remaining, MaxDuration = a.MaxDuration }).ToList()
            }).ToList();
            result.MonsterCount = snapshot.Monsters.Count;
            try
            {
                if (!layout.HasAddress("PlayerManager")) { result.RootValidationReason = "MissingPlayerManager"; return result; }
                // Diagnostic hypothesis from an existing local offset, not an
                // independent NetworkManager AOB. Verify the live object type
                // on every capture before following any network fields.
                ulong playerRoot = layout.Address("PlayerManager", module);
                if (playerRoot < module || playerRoot - module < 0x46010) { result.RootValidationReason = "RelativeRootOutsideModule"; return result; }
                ulong root = playerRoot - 0x46010;
                result.RootCandidateRva = "0x" + (root - module).ToString("X");
                ulong manager = m.Ptr(root);
                TypeInfo managerType = inspect.Inspect(manager, out string rootReason);
                result.RootValidationReason = managerType != null && managerType.Name != "app.NetworkManager" ? "UnexpectedType: " + managerType.Name : rootReason;
                if (managerType?.Name != "app.NetworkManager") return result;
                result.Status = "MetadataVerifiedCandidatesOnly";
                var queue = new List<Pending>(); var scheduled = new HashSet<ulong>(); var metadata = new List<TypeInfo>();
                bool branchLimit = false;
                ulong questObject = 0;
                Action<ulong, string, int, TypeInfo> add = (address, path, depth, type) =>
                {
                    if (type == null || scheduled.Contains(address)) return;
                    if (Callback(type)) { result.SkippedCallbacks++; return; }
                    if (scheduled.Count >= 128) { branchLimit = true; return; }
                    scheduled.Add(address); queue.Add(new Pending { Address = address, Path = path, Depth = depth, Type = type, Priority = Priority(type.Name) });
                };
                // Real capture confirms these paths. Types are checked again on
                // every capture; the quest branch gets the first read budget.
                ulong service = m.Ptr(manager + 0x90); var serviceType = inspect.Inspect(service);
                if (serviceType?.Name == "app.Net_SessionService")
                {
                    ulong quest = m.Ptr(service + 0x20), lobby = m.Ptr(service + 0x18);
                    var questType = inspect.Inspect(quest); var lobbyType = inspect.Inspect(lobby);
                    if (questType?.Name == "app.net_quest_session.cQuestSession")
                    {
                        // Both paths and the matchmake type were observed in the
                        // user's schema-2 capture. Matching is local evidence only.
                        result.QuestSessionCandidate = ReadQuestCandidate(m, inspect, quest, snapshot);
                        questObject = quest;
                        add(quest, "NetworkManager+0x90+0x20", 2, questType);
                    }
                    if (lobbyType?.Name == "app.net_lobby_session.cLobbySession") add(lobby, "NetworkManager+0x90+0x18", 2, lobbyType);
                    if(questType?.Name=="app.net_quest_session.cQuestSession")result.NativeSessionCandidates.Add(ReadNativeSessionCandidate(m,inspect,quest,"NetworkManager+0x90+0x20+0x60"));
                    if(lobbyType?.Name=="app.net_lobby_session.cLobbySession")result.NativeSessionCandidates.Add(ReadNativeSessionCandidate(m,inspect,lobby,"NetworkManager+0x90+0x18+0x60"));
                    ulong link=m.Ptr(service+0x28);
                    if(inspect.Inspect(link)?.Name=="app.cLinkSession")result.NativeSessionCandidates.Add(ReadNativeSessionCandidate(m,inspect,link,"NetworkManager+0x90+0x28+0x60"));
                    add(service, "NetworkManager+0x90", 1, serviceType);
                }
                ulong users = m.Ptr(manager + 0xB8); var usersType = inspect.Inspect(users);
                if (usersType?.Name == "app.Net_UserInfoManager") add(users, "NetworkManager+0xB8", 1, usersType);
                var members = new WildsNetworkMembers(m, inspect, () => limited.Exhausted, module, moduleSize);
                if (usersType?.Name == "app.Net_UserInfoManager") members.ReadUsers(users);
                if (questObject != 0) members.ReadNative(questObject);
                result.QuestMembers = members.Report;
                foreach (var obj in members.Objects)
                {
                    add(obj.Address, obj.Path, obj.Depth, obj.Type);
                    if (!metadata.Any(t => t.TypeAddress == obj.Type.TypeAddress)) metadata.Add(obj.Type);
                }
                add(manager, "NetworkManager", 0, managerType);
                while (queue.Count > 0 && result.Nodes.Count < 64 && !limited.Exhausted)
                {
                    var current = queue.OrderBy(p => p.Priority).First(); queue.Remove(current);
                    var node = new WildsNetworkNode { Path = current.Path, Type = current.Type.Name, Size = current.Type.Size, ScanStatus = "Completed" }; result.Nodes.Add(node);
                    if (!metadata.Any(t => t.TypeAddress == current.Type.TypeAddress)) metadata.Add(current.Type);
                    if (current.Depth >= 7) { node.ScanStatus = "DepthLimit"; branchLimit = true; continue; }
                    bool array = current.Type.Name.EndsWith("[]", StringComparison.Ordinal);
                    int count = array ? m.Int(current.Address + 0x1C) : 0;
                    if (array && (count < 0 || count > 16)) { node.ScanStatus = "ArrayLimit"; branchLimit = true; continue; }
                    if (!array && current.Type.Size > 0x400) { node.ScanStatus = "FieldSpanLimit"; branchLimit = true; }
                    int start = array ? 0x20 : 0x10, end = array ? 0x20 + count * 8 : Math.Min(0x400, current.Type.Size);
                    byte[] fields = m.Bytes(current.Address + (ulong)start, end - start);
                    if (fields == null) { node.ScanStatus = "UnreadableFields"; branchLimit = true; continue; }
                    for (int offset = start; offset + 8 <= end && !limited.Exhausted; offset += 8)
                    {
                        ulong child = BitConverter.ToUInt64(fields, offset - start);
                        if (!Pointer(child) || (child & 7) != 0 || scheduled.Contains(child)) continue;
                        var type = inspect.Inspect(child); if (type == null) continue;
                        string path = current.Path + "+0x" + offset.ToString("X");
                        if (Follow(type.Name)) add(child, path, current.Depth + 1, type);
                        else if (type.Name == "System.String" && current.Type.Name.IndexOf("session", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            string value = m.String(child);
                            if (value.Length >= 16 && result.SessionStringCandidates.Count < 32)
                            {
                                using (var sha = SHA256.Create())
                                    result.SessionStringCandidates.Add(new WildsNetworkValue { Path = path, Length = value.Length,
                                        Sha256 = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "").ToLowerInvariant() });
                            }
                            else if (value.Length > 0 && value.Length < 16 && result.ShortSessionStringFields.Count < 32)
                                result.ShortSessionStringFields.Add(new WildsNetworkValue { Path = path, Length = value.Length });
                        }
                        else if (NetworkType(type.Name) && result.UnfollowedReferences.Count < 32)
                            result.UnfollowedReferences.Add(new WildsNetworkReference { Path = path, Type = type.Name, Size = type.Size, Reason = "TypeOutsideTraversalRules" });
                    }
                    if (limited.Exhausted) node.ScanStatus = "ReadBudget";
                }
                result.RemainingObjects = queue.Count;
                result.TraversalStopReason = limited.Exhausted ? "ReadBudget" : queue.Count > 0 ? "ObjectLimit" : branchLimit ? "BranchLimit" : "Completed";
                result.TraversalCompleted = result.TraversalStopReason == "Completed";
                // Metadata is optional, after all prioritized game-data branches.
                foreach (var type in metadata.Where(t => NetworkType(t.Name) || t.Name == "app.cLinkSession")
                    .OrderBy(t => t.Name.Contains("SessionId") ? -200 : t.Name == "via.network.utility.UniqueId" ? -190 : t.Name.Contains("Net_UserInfo") || t.Name.Contains("Net_QuestUserInfo") ? -150 :
                        t.Name.Contains("QuestMatchmakeSystem") || t.Name == "app.net_quest_session.cSessionInfo" ? -140 : Priority(t.Name)).Take(16))
                {
                    if (limited.Exhausted) break;
                    result.TypeMetadata.Add(inspect.ReadProperties(type, module, moduleSize));
                }
                if (limited.Exhausted && result.TraversalStopReason == "Completed") { result.TraversalStopReason = "MetadataReadBudget"; result.TraversalCompleted = false; }
                return result;
            }
            finally
            {
                if (snapshot.IsReady && !snapshot.IsLoading && snapshot.Monsters.Count > 0)
                {
                    var cache = new Dictionary<string, WildsNetworkTypeMetadata>();
                    result.IdentityStartIndex = identityCursor % snapshot.Monsters.Count;
                    int count = Math.Min(16, snapshot.Monsters.Count);
                    for (int i = 0; i < count && !limited.Exhausted; i++)
                    {
                        int index = (result.IdentityStartIndex + i) % snapshot.Monsters.Count;
                        result.MonsterIdentityCandidates.Add(WildsMonsterIdentityProbe.Capture(snapshot.Monsters[index], m, inspect, module, moduleSize, () => limited.Exhausted, cache));
                        identityCursor = index + 1;
                    }
                }
                result.MemoryReadAttempts = limited.Attempts; result.MemoryBytesRead = limited.BytesRead;
                result.TypeCacheHits = inspect.CacheHits; result.ElapsedMilliseconds = watch.ElapsedMilliseconds;
            }
        }
        internal static WildsNativeSessionCandidate ReadNativeSessionCandidate(WildsMemoryView m,WildsNetworkMetadata inspect,ulong owner,string path)
        {
            var result=new WildsNativeSessionCandidate {Path=path};
            ulong session=m.Ptr(owner+0x60);var type=inspect.Inspect(session);result.Type=type?.Name;result.ObjectSize=type?.Size??0;
            if(type?.Name!="via.network.utility.SessionId" || type.Size!=96){result.Status="TypeUnverified";return result;}
            // Candidate offsets observed in the captured Valid getter's helper.
            // A fingerprint is evidence for paired-client comparison, never a
            // verified session identity and never consumed by ResolveSession.
            ulong buffer=m.Ptr(session+0x30);int length=m.Int(session+0x38);result.BufferLength=length;
            if(length<=0 || length>256 || buffer<0x10000){result.Status="EmptyOrInvalidBuffer";return result;}
            byte[] bytes=m.Bytes(buffer,length);
            if(bytes==null){result.Status="UnreadableBuffer";return result;}
            if(m.Ptr(session+0x30)!=buffer || m.Int(session+0x38)!=length){result.Status="ChangedDuringRead";return result;}
            if(bytes.All(b=>b==0)){result.Status="EmptyBuffer";return result;}
            using(var sha=SHA256.Create())result.Sha256=BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();
            result.Status="BufferFingerprintCandidate";return result;
        }
        static WildsQuestSessionCandidate ReadQuestCandidate(WildsMemoryView m, WildsNetworkMetadata inspect, ulong quest, WildsSnapshot snapshot)
        {
            var result = new WildsQuestSessionCandidate();
            Guid? primary = ReadGuid(m, inspect, m.Ptr(quest + 0x1D8), out result.PrimaryState);
            ulong matchmake = m.Ptr(quest + 0x1E0);
            Guid? secondary = null;
            if (inspect.Inspect(matchmake)?.Name == "app.net_quest_session.QuestMatchmakeSystem")
                secondary = ReadGuid(m, inspect, m.Ptr(matchmake + 0x10), out result.SecondaryState);
            result.PrimarySha256 = primary.HasValue ? Fingerprint(primary.Value) : null;
            result.SecondarySha256 = secondary.HasValue ? Fingerprint(secondary.Value) : null;
            if (primary.HasValue && secondary.HasValue && primary != secondary) { result.Status = "ConflictingPaths"; result.CurrentState = "ConflictingPaths"; return result; }
            Guid? candidate = primary ?? secondary;
            if (!candidate.HasValue) return result;
            result.Status = primary.HasValue && secondary.HasValue ? "MatchingPathsCandidate" : primary.HasValue ? "PrimaryOnlyCandidate" : "SecondaryOnlyCandidate";
            result.Sha256 = Fingerprint(candidate.Value); // observation only; can be retained after the quest
            result.CurrentState = snapshot.IsLoading || snapshot.IsQuestDamageFrozen || snapshot.QuestSuccessState != 0 || snapshot.QuestFailureState != 0 ? "QuestEndedOrLoading" :
                !primary.HasValue ? "NoPrimaryField" : !secondary.HasValue ? "UncorroboratedPrimary" : "CurrentFieldsAgree";
            if (result.CurrentState == "CurrentFieldsAgree") result.CurrentCandidateSha256 = result.PrimarySha256;
            return result;
        }
        static string Fingerprint(Guid value)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value.ToString("D")))).Replace("-", "").ToLowerInvariant();
        }
        static Guid? ReadGuid(WildsMemoryView m, WildsNetworkMetadata inspect, ulong value, out string state)
        {
            state = "Unavailable";
            if (inspect.Inspect(value)?.Name != "System.String") return null;
            string text = m.String(value);
            if (text.Length == 0) { state = "EmptyOrUnreadable"; return null; }
            if (text.Length != 36 || !Guid.TryParseExact(text, "D", out Guid guid)) { state = "InvalidFormat"; return null; }
            if (guid == Guid.Empty) { state = "EmptyGuid"; return null; }
            state = "GuidCandidate"; return guid;
        }
    }
}
