using System;
using System.Linq;
using SmartHunter.Core.Data;
using SmartHunter.Game.Helpers;
using SmartHunter.Wilds;
namespace SmartHunter.Game.Data.WidgetContexts
{
    public sealed partial class DebugWidgetContext : WidgetContext
    {
        public Game CurrentGame { get; } = new Game();
        public string Version => ViewModels.ConsoleViewModel.Instance.GameVersion;
        public string State => ViewModels.ConsoleViewModel.Instance.Status;
        WildsNetworkCheck check;
        WildsSnapshot snapshot;
        bool attached, focused = true, collectionEnabled = true, preview;
        string error;
        public bool HasGameProcess { get => attached; set { if (SetProperty(ref attached, value)) NotifyPropertyChanged(nameof(AllowVisibility)); } }
        public bool GameHasFocus { get => focused; set { if (SetProperty(ref focused, value)) NotifyPropertyChanged(nameof(AllowVisibility)); } }
        public bool AllowVisibility => HasGameProcess && (!ConfigHelper.Main.Values.Overlay.HideWhenGameWindowIsInactive || GameHasFocus);
        protected override SmartHunter.Core.Config.WidgetConfig FontConfig => ConfigHelper.Main.Values.Overlay.DebugWidget;
        public int HeadingFontSize => FontSize + 2;
        public string Heading => L("LOC_UI_DEBUG_HEADING") + (preview ? " · " + L("LOC_UI_PREVIEW_BADGE") : "");
        bool IsWorld => SmartHunter.World.GameProfile.Active == SmartHunter.World.GameChoice.World;
        public string GameLine => IsWorld ? F("LOC_UI_DEBUG_WORLD_GAME", State) : F("LOC_UI_DEBUG_GAME_LINE", Version, StageId, State);
        public void RefreshWorld() => Refresh();
        public bool HasCurrentSession => collectionEnabled && check?.QuestSessionCandidate?.CurrentCandidateSha256 != null && Age <= 20 &&
            (snapshot == null || snapshot.IsReady && !snapshot.IsLoading && !snapshot.IsQuestDamageFrozen && snapshot.QuestSuccessState == 0 && snapshot.QuestFailureState == 0);
        public string SessionLine
        {
            get
            {
                if (IsWorld) return CurrentGame.IsValid && !String.IsNullOrEmpty(CurrentGame.key) ? F("LOC_UI_DEBUG_WORLD_SESSION", CurrentGame.key.Substring(0,Math.Min(12,CurrentGame.key.Length))) : L("LOC_UI_DEBUG_WORLD_LOCAL");
                if (!collectionEnabled) return L("LOC_UI_DEBUG_DISABLED");
                if (check == null) return L("LOC_UI_DEBUG_WAITING");
                bool ended = snapshot != null && (snapshot.IsLoading || !snapshot.IsReady || snapshot.IsQuestDamageFrozen || snapshot.QuestSuccessState != 0 || snapshot.QuestFailureState != 0);
                if (ended) return Status("QuestEndedOrLoading") + " · —";
                if (Age > 20) return L("LOC_UI_DEBUG_STALE");
                string hash = check.QuestSessionCandidate?.CurrentCandidateSha256;
                return Status(check.QuestSessionCandidate?.CurrentState) + " · " + (hash == null ? "—" : hash.Substring(0, Math.Min(12, hash.Length)));
            }
        }
        public string[] WorldDetails => !IsWorld || !CurrentGame.IsValid ? new string[0] : new[] {
            F("LOC_UI_DEBUG_PLAYER", Present(CurrentGame.CurrentPlayerName)),
            F("LOC_UI_DEBUG_SESSION_ID", Present(CurrentGame.SessionID)),
            F("LOC_UI_DEBUG_SESSION_HOST", Present(CurrentGame.SessionHostPlayerName)),
            F("LOC_UI_DEBUG_LOBBY_ID", Present(CurrentGame.LobbyID=="Not Online" ? L("LOC_UI_WORLD_NO_SESSION_ID") : CurrentGame.LobbyID)),
            F("LOC_UI_DEBUG_LOBBY_HOST", CurrentGame.LobbyHostWasFallback ? L("LOC_UI_WORLD_HOST_UNREAD") : Present(CurrentGame.LobbyHostPlayerName)),
            F("LOC_UI_DEBUG_WEAPON", L("LOC_UI_WEAPON_"+CurrentGame.EquippedWeaponType)) };
        static string Present(string value) => String.IsNullOrWhiteSpace(value) ? "—" : value;
        string NetworkDetailsUnavailable => !collectionEnabled ? L("LOC_UI_DEBUG_DISABLED") : check == null ? L("LOC_UI_DEBUG_WAITING") :
            Age > 20 ? L("LOC_UI_DEBUG_STALE") : snapshot != null && (!snapshot.IsReady || snapshot.IsLoading) ? Status("QuestEndedOrLoading") : null;
        public string LobbySessionLine
        {
            get
            {
                string value = NetworkDetailsUnavailable;
                if (value == null)
                {
                    var lobby = check.NativeSessionCandidates.FirstOrDefault(s => s.Path == "NetworkManager+0x90+0x18+0x60");
                    value = lobby?.Status == "BufferFingerprintCandidate" && !String.IsNullOrEmpty(lobby.Sha256)
                        ? lobby.Sha256.Substring(0, Math.Min(12, lobby.Sha256.Length))
                        : lobby?.Status == "EmptyBuffer" || lobby?.Status == "EmptyOrInvalidBuffer" ? L("LOC_UI_DEBUG_SESSION_EMPTY") : L("LOC_UI_DEBUG_SESSION_UNVERIFIED");
                }
                return F("LOC_UI_DEBUG_WILDS_LOBBY_SESSION", value);
            }
        }
        public string NativeRoleLine
        {
            get
            {
                string unavailable = NetworkDetailsUnavailable;
                if (unavailable != null) return unavailable;
                if (snapshot != null && (snapshot.IsQuestDamageFrozen || snapshot.QuestSuccessState != 0 || snapshot.QuestFailureState != 0)) return Status("QuestEndedOrLoading");
                var members = check.QuestMembers;
                var self = members.NativeMembers.Where(m => m.IsSelf == true && m.NativeIndex.HasValue && m.IsHost.HasValue).ToArray();
                var hosts = members.NativeMembers.Where(m => m.IsHost == true && m.NativeIndex.HasValue).ToArray();
                if (members.NativeTableStatus != "Completed" || self.Length != 1 || hosts.Length != 1) return L("LOC_UI_DEBUG_ROLE_UNKNOWN");
                return F("LOC_UI_DEBUG_WILDS_MEMBER_ROLE", self[0].NativeIndex.Value + 1,
                    L(self[0].IsHost == true ? "LOC_UI_DEBUG_ROLE_HOST" : "LOC_UI_DEBUG_ROLE_GUEST"), hosts[0].NativeIndex.Value + 1);
            }
        }
        public string[] WildsDetails => IsWorld ? new string[0] : new[] { LobbySessionLine, NativeRoleLine };
        string[] worldPartyNames = new string[0];
        public void UpdateWorldParty(string[] names) { if(!worldPartyNames.SequenceEqual(names)) { worldPartyNames=names; Refresh(); } }
        public string PartyLine => IsWorld ? F("LOC_UI_DEBUG_WORLD_PARTY", worldPartyNames.Length) : F("LOC_UI_DEBUG_PARTY_LINE", snapshot?.Players.Count(p => !p.IsNpc) ?? check?.ReportedHumanCount ?? 0,
            snapshot?.Players.Count(p => p.IsNpc) ?? check?.ReportedNpcCount ?? 0,
            check == null ? "—" : String.Join("  ", check.QuestMembers.NativeMembers.Where(m => m.NativeIndex.HasValue).Select(m => m.NativeIndex + (m.IsHost == true ? "★" : ""))));
        public string MemberLine
        {
            get
            {
                if (IsWorld) return L(!CurrentGame.IsValid ? "LOC_UI_DEBUG_WAITING" : CurrentGame.LobbyHostWasFallback || String.IsNullOrWhiteSpace(CurrentGame.LobbyHostPlayerName) || String.IsNullOrWhiteSpace(CurrentGame.CurrentPlayerName) ? "LOC_UI_DEBUG_ROLE_UNKNOWN" : CurrentGame.IsCurrentPlayerLobbyHost() ? "LOC_UI_DEBUG_WORLD_HOST" : "LOC_UI_DEBUG_WORLD_GUEST");
                if (check == null) return L("LOC_UI_DEBUG_WAITING");
                var members = check.QuestMembers;
                string type = members.Users.Select(u => u.Type).FirstOrDefault(t => t != null)?.Split('.').Last() ?? "—";
                return F("LOC_UI_DEBUG_MEMBER_LINE", Status(members.UserListStatus), members.Users.Count, members.UserArrayLength?.ToString() ?? "?", type);
            }
        }
        public string ScanLine => IsWorld ? State : check == null ? L("LOC_UI_DEBUG_WAITING") : F("LOC_UI_DEBUG_SCAN_LINE", check.RootValidationReason == "MetadataValidated" ? "✓" : "?", Status(check.TraversalStopReason), Status(check.QuestMembers.NativeTableStatus));
        int Age => check == null ? 0 : Math.Max(0, (int)(DateTime.UtcNow - check.TimestampUtc).TotalSeconds);
        public string TimingLine => check == null ? "—" : F("LOC_UI_DEBUG_TIMING_LINE", check.MemoryReadAttempts, check.ElapsedMilliseconds, Age);
        string relayState = "Disabled";
        int relayPeers, relayApplied, relaySkipped;
        public bool IsRelayEnabled => ConfigHelper.Main.Values.Relay.Enabled;
        public string RelayLine => F("LOC_UI_RELAY_LINE", L("LOC_UI_RELAY_" + relayState), relayPeers, relayApplied, relaySkipped);
        public string NoteLine => error ?? (IsWorld ? L("LOC_UI_DEBUG_WORLD_NOTE") : L(IsRelayEnabled ? "LOC_UI_RELAY_NOTE" : "LOC_UI_DEBUG_HINT"));
        public void SetRelayState(string state, int peers, int applied, int skipped)
        {
            relayState = state; relayPeers = peers; relayApplied = applied; relaySkipped = skipped;
            NotifyPropertyChanged(nameof(RelayLine)); NotifyPropertyChanged(nameof(IsRelayEnabled)); NotifyPropertyChanged(nameof(NoteLine));
        }
        static string L(string key) => UiText.Instance[key];
        static string F(string key, params object[] values) => String.Format(L(key), values);
        static string Status(string code)
        {
            switch (code)
            {
                case "Completed": return L("LOC_UI_DEBUG_DONE");
                case "Partial": return L("LOC_UI_DEBUG_PARTIAL");
                case "CurrentFieldsAgree": return L("LOC_UI_DEBUG_CURRENT");
                case "QuestEndedOrLoading": return L("LOC_UI_DEBUG_ENDED");
                case "NoPrimaryField": case "Unavailable": return L("LOC_UI_DEBUG_NO_CURRENT");
                case "ConflictingPaths": return L("LOC_UI_DEBUG_CONFLICT");
                case "ObjectLimit": return L("LOC_UI_DEBUG_OBJECT_LIMIT");
                case "ReadBudget": case "MetadataReadBudget": return L("LOC_UI_DEBUG_READ_LIMIT");
                case "ChangedDuringRead": return L("LOC_UI_DEBUG_CHANGED");
                case null: return L("LOC_UI_DEBUG_WAITING");
                default:
                    return ConfigHelper.Localization.Values.Strings.TryGetValue(TextKeyNames.Canonical("LOC_UI_DEBUG_STATUS_" + code), out var text)
                        ? text : F("LOC_UI_DEBUG_UNKNOWN_STATE", code);
            }
        }
        public void Observe(WildsSnapshot value) { snapshot = value; StageId = value.StageId; Refresh(); }
        public void ApplyNetwork(WildsNetworkCheck value) { check = value; error = null; preview = false; Refresh(); }
        public void SetError(string value) { error = value == null ? null : value.Substring(0, Math.Min(180, value.Length)); NotifyPropertyChanged(nameof(NoteLine)); }
        public void SetCollectionEnabled(bool value)
        {
            if (collectionEnabled == value) return;
            collectionEnabled = value; check = null; error = null; Refresh();
        }
        public void Reset() { check = null; snapshot = null; error = null; preview = false; HasGameProcess = false; Refresh(); }
        public void ShowPreview(WildsSnapshot value)
        {
            if (!preview)
            {
                check = new WildsNetworkCheck { RootValidationReason = "MetadataValidated", TraversalStopReason = "ObjectLimit", MemoryReadAttempts = 1824, ElapsedMilliseconds = 3,
                    QuestSessionCandidate = new WildsQuestSessionCandidate { CurrentState = "CurrentFieldsAgree", CurrentCandidateSha256 = "1905647336ab" } };
                check.QuestMembers.UserListStatus = "Completed"; check.QuestMembers.NativeTableStatus = "Completed"; check.QuestMembers.UserArrayLength = 4;
                check.QuestMembers.Users.Add(new WildsNetworkManagedMember { Type = "app.Net_QuestUserInfo" });
                check.QuestMembers.NativeMembers.Add(new WildsNetworkNativeMember { NativeIndex = 0, IsHost = true });
            }
            preview = true; error = null; Observe(value);
        }
        public void ClearPreview() { if (preview) { check = null; snapshot = null; preview = false; Refresh(); } }
        public override void UpdateFromConfig()
        {
            base.UpdateFromConfig();
            SetCollectionEnabled(ConfigHelper.Main.Values.Debug.CollectNetworkDiagnostics);
            NotifyPropertyChanged(nameof(FontSize)); NotifyPropertyChanged(nameof(HeadingFontSize)); NotifyPropertyChanged(nameof(AllowVisibility)); Refresh();
        }
        void Refresh()
        {
            foreach (string property in new[] { nameof(WorldDetails), nameof(WildsDetails), nameof(LobbySessionLine), nameof(NativeRoleLine), nameof(Heading), nameof(GameLine), nameof(SessionLine), nameof(HasCurrentSession), nameof(PartyLine), nameof(MemberLine), nameof(ScanLine), nameof(TimingLine), nameof(NoteLine), nameof(RelayLine), nameof(IsRelayEnabled) }) NotifyPropertyChanged(property);
        }
        int m_StageId = -1;
        public int StageId { get => m_StageId; set { if (SetProperty(ref m_StageId, value)) NotifyPropertyChanged(nameof(GameLine)); } }
        public DebugWidgetContext()
        {
            ViewModels.ConsoleViewModel.Instance.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == "GameVersion") { NotifyPropertyChanged(nameof(Version)); NotifyPropertyChanged(nameof(GameLine)); }
                if (e.PropertyName == "Status") { NotifyPropertyChanged(nameof(State)); NotifyPropertyChanged(nameof(GameLine)); }
            };
            ConfigHelper.Localization.Loaded += (s, e) => Refresh();
            ConfigHelper.SettingsSaved += (s, e) => UpdateFromConfig();
        }
    }
}
