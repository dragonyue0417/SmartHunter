using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using SmartHunter.Core;
using SmartHunter.Game.Config;
using SmartHunter.Game.Helpers;
using static TestSupport;

internal static class Program
{
    static int passed;
    static readonly Dictionary<string, Action<Action<string, Action>>> Suites = new Dictionary<string, Action<Action<string, Action>>>(StringComparer.OrdinalIgnoreCase)
    {
        {"memory", test => {
            test("RIP relocation handles signed backward references and rejects overflow", ScanTests.Rip);
            test("wildcard anchor search agrees with exhaustive matching on varied masks", ScanTests.Wildcards);
            test("all game versions require eight independently scanned roots", ScanTests.Versions);
            test("scan covers chunk boundaries, negative RIP displacements and actual ASLR bases", ScanTests.Boundaries);
            test("distinct candidate roots are rejected instead of taking the first match", ScanTests.Ambiguity);
            test("known game versions accept independently relocated managers without a relative address table", ScanTests.IndependentManagers);
            test("unreadable executable pages and malformed headers reject scanning; unknown versions are scanned", ScanTests.Rejections);
            test("an unknown Wilds version resolves all eight existing wildcard signatures", ScanTests.UnknownVersionScan);
            test("verified Wilds 1.42.0.3 retains eight independent AOB targets and rejects missing signatures", ScanTests.VerifiedPatchScan);
            test("unknown-version roots relocate independently without using relative manager locations", ScanTests.IndependentDirectRoots);
            test("every missing unknown-version signature rejects reading while retaining scan evidence", ScanTests.MissingUnknownSignatures);
            test("unknown-version duplicate references share one root while distinct probe targets reject reading", ScanTests.DirectProbeDuplicates);
            test("future Wilds versions attempt the same complete signature scan without a version whitelist", ScanTests.FutureVersions);
            test("scan cancellation stops before resolving partial data", ScanTests.Cancellation);
            test("health parameter storage must provide all sixteen bytes before reading is enabled", ScanTests.EncryptionStorage);
            test("Windows read-only memory supports the complete scanner and inferred roots", ScanTests.NativeScan);
            test("new candidate signatures match the user\u0027s actual instruction windows", ScanTests.ActualInstructionWindows);
            test("ambiguous additional signatures reject reads and preserve diagnostic masks", ScanTests.CandidateDiagnostics);
            test("an external address file cannot bypass a failed scan", ScanTests.ExternalAddressFilesIgnored);
            test("runtime scan checks contain game structure counts without player names", ScanTests.RuntimeChecks);
        }},
        {"wilds", test => {
            test("AES low-float decoding agrees with 1000 independent AES-NI vectors", WildsReaderTests.AesVectors);
            test("health decoding owns its key vectors and rejects partial blocks without changing game bytes", WildsReaderTests.CryptoInputs);
            test("real Windows memory is opened read-only and rejects unreadable addresses", WildsReaderTests.NativeRead);
            test("built-in pointer chains match the independent reference without external address files", WildsReaderTests.BuiltInLayout);
            test("unknown builds are not mislabeled as verified field layouts", WildsReaderTests.UnknownVersion);
            test("pointer traversal follows both RE Engine conventions and stops on null", WildsReaderTests.PointerChains);
            test("dynamic and managed array counts cannot cause unbounded reads", WildsReaderTests.BoundedArrays);
            test("initializing or invalid game data produces no stale overlay", WildsReaderTests.NotReady);
            test("dynamic large-monster populations include every instance and exclude small actors", WildsReaderTests.DynamicPopulation);
            test("spawning, leaving, dying and selecting preserve each monster\u0027s identity", WildsReaderTests.DynamicLifecycle);
            test("selected monster priority and selected-only mode use instance address", WildsReaderTests.Selection);
            test("invalid encrypted health is excluded", WildsReaderTests.InvalidHealth);
            test("loading screens and safe zones hide game data", WildsReaderTests.SafeZone);
            test("parts use Wilds encrypted health and break multipliers", WildsReaderTests.Parts);
            test("ailments use Wilds timers, buildup and IDs", WildsReaderTests.Ailments);
            test("expired ailment timers clear at zero and the next activation counts once", WildsReaderTests.AilmentExpiry);
            test("party slots preserve remote quest index and synced damage", WildsReaderTests.PartySlots);
            test("damage history excludes unrelated targets and sums bounded data", WildsReaderTests.History);
            test("consumable, song and skill timers use Wilds schemas", WildsReaderTests.PlayerEffects);
            test("all five Wilds mantles transition from live duration to cooldown and ready", MantleTests.Timers);
            test("captured Wilds mantle cooldowns use the shared live timer and clear retained references at zero", MantleTests.SharedCountdown);
            test("unavailable save selection cannot hide live mantle timers", MantleTests.Selection);
            test("invalid mantle counters, flags and bounds clear without stale values", MantleTests.Invalid);
            test("typed mantle arrays recover moved components and cache offsets without stale pointers", MantleTests.TypedRecovery);
            test("mantle recovery rejects unrelated arrays and bounds repeated searches and cycles", MantleTests.RecoveryBounds);
            test("legacy player data gains missing equipment keys without changing World or custom rows", MantleTests.Definitions);
            test("mantle diagnostics are opt-in, bounded, local and retain the last usable sample", MantleTests.Diagnostics);
            test("the real player overlay renders mantle duration and cooldown in seconds", MantleTests.Display);
            test("AOB-rooted equipment diagnostics capture all five native objects and timer candidates", MantleTests.NativeProbe);
            test("deeper cyclic equipment graphs remain diagnostic without changing overlay values", MantleTests.DeepProbe);
            test("latest CT mantle parameters remain separate from live countdowns and retain broken-path evidence", MantleTests.ParameterProbe);
            test("equipment diagnostic history retains the last discovered controller", MantleTests.ProbeRetention);
            test("real ManagedObject mantle arrays require typed elements and reject replaced objects", MantleTests.RealManagedArrays);
            test("conditional debuffs, skill variants, Palico songs and buildup retain native semantics", WildsReaderTests.ConditionalEffects);
            test("snapshot updates remove despawned monsters and departed players", WildsReaderTests.SnapshotUpdates);
            test("preview samples explicitly exercise more than three monsters", WildsReaderTests.Preview);
            test("all 35 supported runtime types reach their canonical overlay and web identities", CatalogCompatibilityTests.DisplayAllTypes);
            test("completed, failed and abandoned quests retain totals and include the final hit", QuestLifecycleTests.QuestEnd);
            test("quest retries, zero totals and read gaps never mix free-roam damage", QuestLifecycleTests.QuestRestart);
            test("quest results preserve the remote and NPC roster through teardown", QuestLifecycleTests.Roster);
            test("active quests keep damage and chart history through a mid-quest camp visit", QuestReturnTests.ActiveCamp);
            test("quest results remain frozen until settlement returns to the same field camp", QuestReturnTests.ReturnToCamp);
            test("clear and fail results reset after returning to a hub even when camp display is disabled", QuestReturnTests.ReturnToHub);
            test("a quest finishing while already in camp waits for the return transition", QuestReturnTests.EndWhileInCamp);
            test("post-return exploration starts at zero and counts only new hits before the next quest", QuestReturnTests.Exploration);
        }},
        {"sync", test => {
            test("unknown builds resolve matching live sessions without promoting diagnostic fingerprints", NativeSessionTests.UnknownVersionDiagnostics);
            test("unknown-build diagnostics reject unrelated roots and retain hard read limits", NativeSessionTests.UnknownVersionLimits);
            test("paired schema-7 evidence resolves the same session and opposite automatic roles", NativeSessionTests.PairedSchema7Roles);
            test("session transitions during member reads cannot publish a mismatched identity", NativeSessionTests.SessionTransition);
            test("enemy scalar candidates reuse the bounded body read and never export process pointers", NativeSessionTests.ScalarCandidates);
            test("expanded monster diagnostics preserve full parts and hash bounded verified enemy contexts", NativeSessionTests.MonsterIdentity);
            test("the user\u0027s real network paths complete below the unchanged read limit", NativeSessionTests.UserGraph);
            test("network diagnostics explicitly report read, depth and array limits", NativeSessionTests.Limits);
            test("reflection metadata records bounded getter code without reading private values", NativeSessionTests.Properties);
            test("network caches last one capture and never retain old session fingerprints", NativeSessionTests.Freshness);
            test("metadata names beside unreadable memory use bounded page-safe reads", NativeSessionTests.NameBoundary);
            test("real schema-2 callback noise cannot starve session or member branches", NativeSessionTests.Schema2Graph);
            test("quest session GUID candidates agree locally without claiming cross-client validation", NativeSessionTests.QuestIdentity);
            test("observed quest lifecycle never publishes retained matchmaking values as current", NativeSessionTests.QuestLifecycle);
            test("targeted quest arrays work without fixed-size array metadata and verify live getter fields", NativeSessionTests.TargetedMembers);
            test("targeted member diagnostics reject invalid bounds, getters and stale table reads", NativeSessionTests.TargetedLimits);
            test("the real quest-user subtype is recognized while base offsets require verified inheritance", NativeSessionTests.DerivedUsers);
            test("native identity metadata follows one verified in-module helper without invoking it", NativeSessionTests.IdentityMetadata);
            test("relay session reads require matching fields and a verified self getter to identify the local host", NativeSessionTests.RelayIdentity);
            test("monster relay updates matched parts and optional inactive buildup, never health or damage", RelayTests.Merge);
            test("previous client part names remain compatible with renamed local parts", CatalogCompatibilityTests.OldPeerPartNames);
            test("duplicate descriptors attempt persistent one-to-one pairing across refresh and wave changes", RelayTests.Approximate);
            test("relay roles, lifecycle, stale responses and settings keep local reading independent", RelayTests.Lifecycle);
            test("copied settings never share a runtime relay identity or persist a replacement client ID", RelayTests.RuntimeIdentity);
            test("HTTP publishes at 250ms intervals without multiplying validated session reads", RelayTests.FastPolling);
            test("HTTP failures back off and recover without overlapping or blocking the local reader", RelayTests.RetryPolling);
            test("the user\u0027s real five-monster snapshots pair differing keys and update the corresponding overlay part", RelayTests.RealSnapshots);
            test("session probes validate metadata, bound traversal and redact values", SessionMetadataTests.NetworkProbe);
            test("HTTP IP synchronization stays separate from HTTPS viewing and corrects previous defaults", WebShareTests.Routing);
            test("web sharing defaults off and preserves independent game preferences", WebShareTests.Preferences);
            test("per-run sharing keys and URLs survive quest changes without exposing private keys", WebShareTests.Identity);
            test("new processes ignore copied legacy sharing keys and never create identity files", WebShareTests.FreshProcessIdentity);
            test("display snapshots retain all health/status fields without changing relay merge semantics or exporting pointers", WebShareTests.Snapshots);
        }},
        {"settings", test => {
            test("native Ctrl\u002BF6 callback contains handler failures and continues processing subsequent keys", InputMigrationTests.HookFailure);
            test("shared relay migration creates independent game servers and current profiles take precedence", InputMigrationTests.Servers);
            test("network settings edit the selected game while its sibling reader stays active", InputMigrationTests.ServerEditor);
            test("legacy server and translation import persist directly without Imports folders", InputMigrationTests.Import);
            test("language switching persists and preserves custom translations", LanguageSelectionTests.LanguageSwitch);
            test("legacy three-monster descriptions upgrade without rewriting user languages", LanguageSelectionTests.LegacyLanguage);
            test("all 295 legacy keys convert uniquely while custom test text remains exact", LocalizationTests.Attachment);
            test("every bundled language and native data reference passes the complete LOC naming audit", LocalizationTests.Stock);
            test("Taiwan wording upgrades old defaults once while preserving custom text and anatomical directions", LocalizationTests.TaiwanDefaults);
            test("the complete monster table distinguishes EM identifiers from runtime enum values", CatalogCompatibilityTests.Identifiers);
            test("generated old-format settings migrate all 35 monster IDs and custom part slots without collisions", CatalogCompatibilityTests.LegacyMonsterFile);
            test("renamed monster, part and player text keys retain custom translation precedence", CatalogCompatibilityTests.TranslationAliases);
            test("language load, save and import canonicalize all keys while preserving custom text and metadata", LocalizationTests.LanguageIO);
            test("old Wilds player references migrate separately from identically named monster states", LocalizationTests.PlayerReferences);
            test("first-run creation writes both games\u0027 complete editable JSON files without overwriting custom files", LocalizationTests.Defaults);
            test("a clean real application startup creates both language folders before any game is running", LocalizationTests.Startup);
            test("old backup settings are ignored and absent from the interface", RetiredSettingsTests.BackupRemoved);
            test("single-game settings persist fonts, backgrounds, keys, servers and overlays independently", ProfilePersistenceTests.SeparateFiles);
            test("old shared settings migrate once without overwriting newer game settings or creating backups", ProfilePersistenceTests.Migrate);
            test("legacy translation keys convert with custom text and metadata preserved", LegacyTextTests.KeyMigration);
            test("repeated direct imports leave sources and the other game unchanged and create no archives", ProfilePersistenceTests.DirectImport);
            test("unchanged profile saves leave file timestamps and live reader references intact", ProfilePersistenceTests.StableWrites);
            test("external edits reload just the corresponding game\u0027s settings without switching the reader", ProfilePersistenceTests.LiveReload);
            test("invalid profile edits cannot partially change live settings or replace malformed files", ProfilePersistenceTests.InvalidReload);
            test("atomic settings writes recover from temporary locks and preserve the original after bounded failure", AtomicFileTests.LockedFile);
            test("preview selection follows the next game\u0027s own options on the first reader switch", ProfilePersistenceTests.IndependentPreview);
            test("on-disk language and data migration removes legacy references without losing custom text", LegacyTextTests.DiskNames);
            test("automatic game selection respects manual choice and never oscillates when both games run", GameProfileTests.Selection);
            test("World and Wilds overlay coordinates and translations persist independently", GameProfileTests.Profiles);
            test("World UI import preserves source files, custom translations and existing destination languages", GameProfileTests.Import);
            test("late World HTTP callbacks are discarded after reader invalidation", GameProfileTests.LateCallbacks);
            test("shared settings and overlay render both World and Wilds profiles", GameProfileTests.RenderProfiles);
            test("one in-process reader stops before another starts and leaves only one shared overlay model", GameProfileTests.ReaderLifecycle);
            test("classification migrates flat files directly, preserves newer destinations and is idempotent", SettingsImportTests.Storage);
            test("real application migrates legacy config before WPF initializes resource view models", SettingsImportTests.StartupUpgrade);
            test("classified config watcher monitors the physical file name", SettingsImportTests.Watcher);
            test("multi-select import converts configs and languages only into the selected game", SettingsImportTests.MultiImport);
            test("invalid multi-select input fails preflight before writing destination files", SettingsImportTests.InvalidImport);
            test("editing another game\u0027s settings and language never changes the active reader", SettingsImportTests.Editor);
            test("built-in locale aliases do not duplicate options or mix World and Wilds baselines", SettingsImportTests.Locales);
            test("all settings, options, weapon and status labels exist in each profile and language", SettingsImportTests.Wording);
            test("old stock wording is upgraded while user text and metadata are preserved", SettingsImportTests.WordingUpgrade);
        }},
        {"world", test => {
            WorldMemoryTests.Register(test);
            WorldStatusTests.Register(test);
        }},
        {"overlay", test => {
            test("seven-plus monsters automatically wrap without shrinking or horizontal scrolling", AdaptiveLayoutTests.Wrap);
            test("selected detail mode follows the target and restores details when no target exists", AdaptiveLayoutTests.Focus);
            AppearanceTests.Register(test);
            test("debug overlay stays available during loading and rejects stale, disabled and ended session display", DebugOverlayTests.Transitions);
            test("diagnostic panel renders readable localized text and persists its visibility and size settings", DebugOverlayTests.RenderSettings);
            test("Wilds lobby fingerprints and native roles stay separate from quest IDs and clear when stale", DebugOverlayTests.SessionDetails);
            test("layout selectors persist columns and rows and localize immediately", OverlayLayoutTests.LayoutSettings);
            test("the shipped external overlay skin resolves its assemblies and types", OverlayLayoutTests.ExternalSkin);
            test("Wilds UI loads, filters settings and renders both languages", OverlayLayoutTests.RenderUi);
            test("dynamic overlay wraps, scrolls to the final actor and fits narrow screens", OverlayLayoutTests.DynamicLayout);
            test("horizontal default stays in one row and vertical mode remains scrollable", OverlayLayoutTests.LayoutModes);
            test("compact health, part and status rows share adjustable widths without overlap", OverlayLayoutTests.CompactDetails);
            test("actual monster windows accept independent viewport widths without changing cards or text", ViewportTests.WindowWidth);
            test("window width editing validates arbitrary integers, resets to automatic and preserves the sibling game", ViewportTests.WidthEditor);
            test("background defaults, replacement, fallback and reset work without file locks", DesktopDisplayTests.Background);
            test("interface text size persists and updates navigation and logs immediately", DesktopDisplayTests.FontSizes);
            test("camp display reads live monsters while loading still clears the overlay", DesktopDisplayTests.CampDisplay);
            test("live log statistics refresh on each response without appending repeated log summaries", LiveDiagnosticsTests.LiveStatistics);
            test("loading, camp display and quest-end status follow the live snapshot", LiveDiagnosticsTests.SnapshotState);
            test("existing built-in languages gain editable status keys without losing custom text or metadata", LiveDiagnosticsTests.LanguageUpgrade);
            test("sync diagnostics log transitions and cumulative payload/timing summaries without poll spam", LiveDiagnosticsTests.Statistics);
            test("real request completion reports UTF-8 payload size, HTTP failure, latency and recovery", LiveDiagnosticsTests.HttpDiagnostics);
            test("each overlay text-size setting updates rendered text independently and preserves monster width", LiveDiagnosticsTests.OverlayFonts);
            test("short and wrapped monster rows center without scaling text or changing column widths", WindowLayoutTests.Centering);
            test("World diagnostics include player, session, party, hosts and weapon, then clear on detach", WorldSessionDisplayTests.WorldDebug);
            test("compact settings header renders at minimum width and maximum readable font size", WindowLayoutTests.Header);
        }},
        {"updates", test => {
            test("raw update manifests validate repository, branch, file whitelist and SHA-256", UpdateTests.Catalog);
            test("local update verification rejects tampering, missing files and mismatched executable versions", UpdateTests.ManifestFiles);
            test("in-place installation preserves user files and restores program files after a locked-file failure", UpdateTests.Install);
            test("GitHub checks and downloads the complete uncompressed folder and installs it with user data preserved", UpdateTests.Http);
            test("raw update failures, unpublished folders, truncation, tampering and cancellation never install", UpdateTests.HttpFailures);
            test("only changed program files are downloaded after local hash validation", UpdateTests.ReuseFiles);
            test("all update downloads stay pinned to one commit even when the branch moves", UpdateTests.Pinning);
            test("one update URL defaults to unified, migrates old sources and keeps profiles independent", UpdateTests.SourceSettings);
            test("release migration disables diagnostics once, removes chat settings and preserves later user choices", UpdateTests.Defaults);
            test("original World and Wilds files convert in place with servers, languages and independent overlays", UpdateTests.Legacy);
            test("invalid legacy data is retained without partial conversion and current settings take precedence", UpdateTests.Preflight);
            test("retired root files and save backups retain their original bytes in one archive", LegacyArchiveTests.FlatFiles);
            test("a nested legacy installation migrates settings and languages before archiving", LegacyArchiveTests.NestedMigration);
            test("current settings and source project folders are protected during cleanup", LegacyArchiveTests.CurrentFilesWin);
            test("active custom themes keep their original path until no longer used", LegacyArchiveTests.ActiveAssets);
            test("locked legacy files remain available and are retried on a later launch", LegacyArchiveTests.LockedFile);
            test("legacy archiving cannot follow a junction outside the installation", LegacyArchiveTests.LinkedArchive);
            test("release UI opens Logs with visible share/update controls and no damage sharing section", UpdateTests.Ui);
        }},
        {"performance", test => {
            test("snapshot pointer reuse is bounded to one frame and never caches failed reads", ReadPerformanceTests.PointerFreshness);
            test("optimized 1, 5 and 12-monster snapshots retain every value with fewer native calls", ReadPerformanceTests.Parity);
            test("unreadable bulk ranges fall back to the original scalar reads", ReadPerformanceTests.Fallback);
            test("reused AES buffers cannot decode stale bytes after read failure", ReadPerformanceTests.CryptoFailure);
            test("streamed diagnostics preserve JSON and replace files without backup artifacts", ReadPerformanceTests.Streaming);
            test("diagnostic byte limits retain the newest complete check", ReadPerformanceTests.FileBounds);
            test("native session fingerprints remain bounded diagnostics and never authorize sharing", ReadPerformanceTests.NativeSession);
            test("scan history retains one successful summary as evidence only", ScanHistoryTests.ScanHistory);
        }},
    };
    static void Test(string name, Action body)
    {
        body(); passed++;
        Console.WriteLine("PASS " + name);
    }
    static void Usage()
    {
        Console.WriteLine("SmartHunter.Tests [--all | --suite <name> | --list | --help]");
        Console.WriteLine("Suites: " + String.Join(", ", Suites.Keys));
        Console.WriteLine("No arguments runs every suite. Tests and their output are not part of the client ZIP.");
    }
    [STAThread] static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--web-share-probe")
        {
            Console.WriteLine(WebShareIdentity.Token(WebShareIdentity.GetKey(SmartHunter.World.GameChoice.World)));
            Console.WriteLine(WebShareIdentity.Token(WebShareIdentity.GetKey(SmartHunter.World.GameChoice.Wilds)));
            return 0;
        }
        if (args.Length == 1 && args[0] == "--help") { Usage(); return 0; }
        if (args.Length == 1 && args[0] == "--list") { foreach (string name in Suites.Keys) Console.WriteLine(name); return 0; }
        string selected = null;
        if (args.Length == 2 && args[0] == "--suite" && Suites.ContainsKey(args[1])) selected = args[1];
        else if (args.Length != 0 && !(args.Length == 1 && args[0] == "--all")) { Usage(); return 2; }
        try
        {
            RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
            new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            StorageLayout.Prepare(Root);
            ConfigHelper.Main.Values = new MainConfig();
            DefaultFiles.Ensure(Root);
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/SmartHunter;component/Ui/Resources/Default.xaml", UriKind.Relative) });
            foreach (var suite in Suites)
                if (selected == null || String.Equals(suite.Key, selected, StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("SUITE " + suite.Key);
                    suite.Value(Test);
                }
            Console.WriteLine("RESULT " + passed + " " + (selected ?? "all") + " tests passed");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
