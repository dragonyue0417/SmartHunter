// Read-only AOB scanner: CT instruction patterns and user-captured game code.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;

namespace SmartHunter.Wilds
{
    public sealed class WildsScanReport
    {
        public string CapturedAtUtc { get; set; } = DateTime.UtcNow.ToString("o");
        public string GameVersion { get; set; }
        public string LayoutVersion { get; set; } = WildsMemoryLayout.LayoutVersion;
        public string AddressSource { get; set; } = "DirectSignatures";
        public string FieldLayoutStatus { get; set; } = "KnownGameVersion";
        public string Status { get; set; } = "NotStarted";
        public string Reason { get; set; }
        public long ElapsedMilliseconds { get; set; }
        public long BytesRead { get; set; }
        public int UnreadablePages { get; set; }
        public Dictionary<string, string[]> ScanCandidates { get; } = new Dictionary<string, string[]>();
        public Dictionary<string, string> ResolvedRvas { get; } = new Dictionary<string, string>();
        public List<WildsCodeReference> CodeReferences { get; } = new List<WildsCodeReference>();
        public List<WildsProbeResult> CandidateSignatures { get; } = new List<WildsProbeResult>();
        public List<WildsRuntimeCheck> RuntimeChecks { get; } = new List<WildsRuntimeCheck>();
        public List<WildsScanPattern> ScanPatterns { get; } = new List<WildsScanPattern>();
        public WildsScanSummary PreviousSuccessfulScan { get; set; }
    }

    public sealed class WildsScanPattern
    {
        public string Target { get; set; }
        public string Pattern { get; set; }
        public bool UsedForReading { get; set; }
        public int DisplacementOffset { get; } = 3;
        public int InstructionLength { get; } = 7;
    }

    public sealed class WildsCodeReference
    {
        public string Target { get; set; }
        public string TargetRva { get; set; }
        public string InstructionRva { get; set; }
        public string ContextRva { get; set; }
        public string Bytes { get; set; }
        public string Pattern { get; set; }
        public string Source { get; set; }
    }

    public sealed class WildsProbeResult
    {
        public string Target { get; set; }
        public string[] CandidateRvas { get; set; }
        public string ResolvedOffsetRva { get; set; }
        public string Status { get; set; }
        public bool UsedForReading { get; set; }
    }

    public sealed class WildsRuntimeCheck
    {
        public string CapturedAtUtc { get; set; }
        public bool IsReady { get; set; }
        public bool InHuntingZone { get; set; }
        public bool IsLoading { get; set; }
        public bool CanDisplayData { get; set; }
        public int StageId { get; set; }
        public int MonsterCount { get; set; }
        public int SelectedCount { get; set; }
        public int PartCount { get; set; }
        public int AilmentCount { get; set; }
        public int PartyCount { get; set; }
        public bool AllHealthValid { get; set; }
        public static WildsRuntimeCheck From(WildsSnapshot data) => new WildsRuntimeCheck {
            CapturedAtUtc = DateTime.UtcNow.ToString("o"), IsReady = data.IsReady, InHuntingZone = data.InHuntingZone,
            IsLoading = data.IsLoading, CanDisplayData = data.CanDisplayData,
            StageId = data.StageId, MonsterCount = data.Monsters.Count, SelectedCount = data.Monsters.Count(m => m.Selected),
            PartCount = data.Monsters.Sum(m => m.Parts.Count), AilmentCount = data.Monsters.Sum(m => m.Ailments.Count),
            PartyCount = data.Players.Count, AllHealthValid = data.Monsters.All(m => WildsReader.ValidHealth(m.Health, m.MaxHealth))
        };
    }

    public sealed class WildsScanResult
    {
        public WildsAddresses Addresses { get; internal set; }
        public WildsScanReport Report { get; internal set; }
    }

    public sealed class WildsByteSignature
    {
        readonly byte?[] pattern;
        readonly byte[] anchor;
        readonly int anchorOffset;
        readonly int[] skip = new int[256];
        public string Name { get; }
        public string PatternText { get; }
        public int Length => pattern.Length;
        public WildsByteSignature(string name, string text)
        {
            Name = name;
            PatternText = text;
            pattern = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s == "?" || s == "??" ? (byte?)null : byte.Parse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToArray();
            int bestStart = 0, bestLength = 0;
            for (int i = 0; i < pattern.Length;)
            {
                if (!pattern[i].HasValue) { i++; continue; }
                int start = i;
                while (i < pattern.Length && pattern[i].HasValue) i++;
                if (i - start > bestLength) { bestStart = start; bestLength = i - start; }
            }
            if (bestLength == 0) throw new ArgumentException("Signature must contain literal bytes.");
            anchorOffset = bestStart; anchor = pattern.Skip(bestStart).Take(bestLength).Select(x => x.Value).ToArray();
            for (int i = 0; i < skip.Length; i++) skip[i] = anchor.Length;
            for (int i = 0; i < anchor.Length - 1; i++) skip[anchor[i]] = anchor.Length - 1 - i;
        }
        // Search the longest literal run, then check the complete wildcard mask.
        public IEnumerable<int> Find(byte[] bytes, int count)
        {
            if (count < 0 || count > bytes.Length) throw new ArgumentOutOfRangeException(nameof(count));
            for (int at = anchorOffset; at <= count - anchor.Length;)
            {
                int i = anchor.Length - 1;
                while (i >= 0 && bytes[at + i] == anchor[i]) i--;
                if (i >= 0) { at += skip[bytes[at + anchor.Length - 1]]; continue; }
                int start = at - anchorOffset;
                bool matches = start >= 0 && start <= count - pattern.Length;
                if (matches) for (i = 0; i < pattern.Length; i++)
                    if (pattern[i].HasValue && pattern[i].Value != bytes[start + i]) { matches = false; break; }
                if (matches) yield return start;
                at++;
            }
        }
    }

    public static class WildsPatternScanner
    {
        public const int ChunkSize = 65536;
        const int Overlap = 96;
        static readonly WildsByteSignature[] signatures = {
            new WildsByteSignature("PlayerManager", "48 8B 15 ?? ?? ?? ?? E8 ?? ?? ?? ?? 48 85 C0 74 2D 48 8B 40 30 48 8B 40 18"),
            new WildsByteSignature("SaveManager", "48 8B ?? ?? ?? ?? ?? 80 B8 E4 00 00 00 ?? 75 ?? 80 B8 E5 00 00 00 ?? 75 ?? 80 B8 E6 00 00 00 ?? 75"),
            new WildsByteSignature("EnemyManager", "48 8B 15 ?? ?? ?? ?? 84 DB 74 1F 48 8B 82 D0 00 00 00"),
            new WildsByteSignature("QuestManager", "48 8B 05 ?? ?? ?? ?? 48 8B 80 48 01 00 00 BB 08 00 00 00 80 78 4C 00")
        };
        // Direct signatures from the user's existing 1.42.0.2 instruction windows.
        // All versions require these targets independently; no manager-location table.
        static readonly WildsByteSignature[] probes = {
            new WildsByteSignature("CameraManager", "48 8B 15 ?? ?? ?? ?? E8 ?? ?? ?? ?? 48 85 C0 74 ?? 80 B8 8D 00 00 00 00 74 ?? F6 40 64 60 75 ?? 48 89 F1 48 89 C2 E8 ?? ?? ?? ?? 89 C1 B0 01 84"),
            new WildsByteSignature("PauseManager", "48 8B 05 ?? ?? ?? ?? 48 85 C0 74 ?? 48 83 78 10 00 74 ?? 48 8B 50 50 48 89 F9 E8 ?? ?? ?? ?? 84 C0 74 ??"),
            new WildsByteSignature("HealthEncryptionRound", "48 8D 15 ?? ?? ?? ?? 48 8D 74 24 20 41 B9 01 00 00 00 48 89 F1 E8 ?? ?? ?? ?? 80 7E 08 00 74"),
            new WildsByteSignature("NpcManager", "48 8B 15 ?? ?? ?? ?? 48 8B 52 58 48 8B 52 10 48 98 48 8B 44 C2 20 48 85 C0 0F 84 ?? ?? ?? ?? 48 83 78 10 00 0F 84")
        };
        static readonly string[] additionalRoots = {
            "CameraManager", "PauseManager", "HealthEncryptionKey", "HealthEncryptionRound", "NpcManager"
        };
        // Whether the field profile is verified for this exact version. This is
        // deliberately not a prerequisite for scanning the executable.
        public static bool SupportsLayout(string version) => WildsMemoryLayout.Supports(version);
        public static IReadOnlyList<WildsByteSignature> Signatures => Array.AsReadOnly(signatures);
        public static IReadOnlyList<WildsByteSignature> CandidateSignatures => Array.AsReadOnly(probes);
        static string Hex(ulong n) => "0x" + n.ToString("X");

        public static bool TryRipTarget(ulong instruction, byte[] bytes, int start, int displacementOffset, int instructionLength, out ulong target)
        {
            target = 0;
            if (start < 0 || displacementOffset < 0 || instructionLength < displacementOffset + 4 ||
                start > bytes.Length - displacementOffset - 4 || instruction > long.MaxValue) return false;
            try
            {
                long address = checked((long)instruction + instructionLength + BitConverter.ToInt32(bytes, start + displacementOffset));
                if (address < 0) return false;
                target = (ulong)address; return true;
            }
            catch (OverflowException) { return false; }
        }

        public static WildsScanResult Resolve(IWildsMemory memory, string version, ulong moduleBase, int moduleSize, CancellationToken cancellation = default(CancellationToken))
        {
            bool knownLayout = SupportsLayout(version);
            var report = new WildsScanReport { GameVersion = version,
                AddressSource = "DirectSignatures",
                FieldLayoutStatus = knownLayout ? "KnownGameVersion" : "UnverifiedGameVersion" };
            foreach (var signature in signatures.Concat(probes))
                report.ScanPatterns.Add(new WildsScanPattern { Target = signature.Name, Pattern = signature.PatternText, UsedForReading = true });
            var result = new WildsScanResult { Report = report };
            var watch = Stopwatch.StartNew();
            var candidates = signatures.ToDictionary(s => s.Name, s => new HashSet<ulong>());
            var probeCandidates = probes.ToDictionary(s => s.Name, s => new HashSet<ulong>());
            try
            {
                if (moduleSize < 4096 || moduleBase > long.MaxValue || moduleBase > (ulong)long.MaxValue - (ulong)moduleSize)
                    throw new InvalidDataException("Invalid module bounds.");
                ulong moduleEnd = moduleBase + (ulong)moduleSize;
                var sections = CodeSections(memory, moduleBase, moduleSize);
                var buffer = new byte[ChunkSize + Overlap];
                foreach (var section in sections)
                    for (ulong offset = section.Item1; offset < section.Item2;)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        int nominal = (int)Math.Min((ulong)ChunkSize, section.Item2 - offset);
                        int length = (int)Math.Min((ulong)buffer.Length, section.Item2 - offset);
                        if (memory.Read(moduleBase + offset, buffer, length))
                            ProcessBlock(buffer, length, nominal, moduleBase + offset, moduleBase, moduleEnd, memory, candidates, probeCandidates, report);
                        else
                        {
                            // Retry page-sized reads; a single unreadable page must not
                            // discard a complete 64 KiB range or reuse stale buffer bytes.
                            for (int page = 0; page < nominal; page += 4096)
                            {
                                cancellation.ThrowIfCancellationRequested();
                                int pageCount = Math.Min(4096, nominal - page), readCount = Math.Min(pageCount + Overlap, length - page);
                                if (!memory.Read(moduleBase + offset + (ulong)page, buffer, readCount))
                                {
                                    readCount = pageCount;
                                    if (!memory.Read(moduleBase + offset + (ulong)page, buffer, readCount)) { report.UnreadablePages++; continue; }
                                    // Losing the cross-page tail prevents exhaustive matching.
                                    if (pageCount < length - page) report.UnreadablePages++;
                                }
                                ProcessBlock(buffer, readCount, pageCount, moduleBase + offset + (ulong)page, moduleBase, moduleEnd, memory, candidates, probeCandidates, report);
                            }
                        }
                        offset += (ulong)nominal;
                    }
                if (report.UnreadablePages != 0) throw new InvalidDataException("Executable pages were unreadable; scan completeness cannot be established.");
                foreach (var signature in signatures)
                {
                    var targets = candidates[signature.Name];
                    if (targets.Count != 1) throw new InvalidDataException(signature.Name + " has " + targets.Count + " distinct scan targets; expected one.");
                }
                var scannedRvas = candidates.ToDictionary(p => p.Key, p => p.Value.Single() - moduleBase);
                foreach (var probe in probes)
                {
                    var targets = probeCandidates[probe.Name];
                    if (targets.Count != 1) throw new InvalidDataException(probe.Name + " has " + targets.Count + " distinct direct scan targets; expected one.");
                    scannedRvas[probe.Name] = targets.Single() - moduleBase;
                }
                var resolved = WildsMemoryLayout.Resolve(scannedRvas, version);
                // Validate every root used by the reader, including adjacent vectors.
                foreach (string key in signatures.Select(s => s.Name).Concat(additionalRoots))
                {
                    ulong address = resolved.Address(key, moduleBase);
                    int width = key.StartsWith("HealthEncryption", StringComparison.Ordinal) ? 16 : 8;
                    if (address < moduleBase || address > moduleEnd - (ulong)width || address % (ulong)width != 0 || !memory.Read(address, new byte[width], width))
                        throw new InvalidDataException("Resolved root is outside readable aligned module data: " + key);
                    report.ResolvedRvas[key] = Hex(address - moduleBase);
                }
                // These are storage addresses. Key = Round + 0x10, not key bytes.
                if (resolved.Address("HealthEncryptionKey", 0) - resolved.Address("HealthEncryptionRound", 0) != 0x10)
                    throw new InvalidDataException("Unexpected cryptography vector layout.");
                report.Status = "Resolved"; result.Addresses = resolved;
                foreach (var reference in report.CodeReferences) reference.Source = "ActiveSignature";
            }
            catch (Exception error) when (error is InvalidDataException || error is OverflowException || error is ArgumentException)
            {
                report.Status = "Rejected"; report.Reason = error.Message;
                report.ResolvedRvas.Clear();
            }
            finally
            {
                foreach (var pair in candidates) report.ScanCandidates[pair.Key] = pair.Value.OrderBy(v => v).Select(v => Hex(v - moduleBase)).ToArray();
                foreach (var pair in probeCandidates)
                {
                    string expected = result.Addresses == null ? null : Hex(result.Addresses.Address(pair.Key, 0));
                    string[] found = pair.Value.OrderBy(v => v).Select(v => Hex(v - moduleBase)).ToArray();
                    report.CandidateSignatures.Add(new WildsProbeResult { Target = pair.Key, CandidateRvas = found, ResolvedOffsetRva = expected, UsedForReading = result.Addresses != null,
                        Status = found.Length == 0 ? "NoMatch" : found.Length > 1 ? "Ambiguous" : expected == null ? "NoResolvedReference" : found[0] == expected ? "MatchesDirectScan" : "DiffersFromDirectScan" });
                }
                report.ElapsedMilliseconds = watch.ElapsedMilliseconds;
            }
            return result;
        }

        static List<Tuple<ulong, ulong>> CodeSections(IWildsMemory memory, ulong moduleBase, int size)
        {
            var dos = new byte[64];
            if (!memory.Read(moduleBase, dos, dos.Length) || BitConverter.ToUInt16(dos, 0) != 0x5A4D) throw new InvalidDataException("Invalid DOS header.");
            int pe = BitConverter.ToInt32(dos, 0x3C);
            if (pe < 64 || pe > Math.Min(size - 24, 1048576)) throw new InvalidDataException("Invalid PE header offset.");
            var header = new byte[24];
            if (!memory.Read(moduleBase + (ulong)pe, header, header.Length) || BitConverter.ToUInt32(header, 0) != 0x4550 || BitConverter.ToUInt16(header, 4) != 0x8664)
                throw new InvalidDataException("Expected an x64 PE module.");
            int count = BitConverter.ToUInt16(header, 6), optional = BitConverter.ToUInt16(header, 20);
            if (count < 1 || count > 96 || optional < 112 || optional > 4096 || (long)pe + 24 + optional + count * 40 > size)
                throw new InvalidDataException("Invalid PE section table.");
            var magic = new byte[2];
            if (!memory.Read(moduleBase + (ulong)pe + 24, magic, 2) || BitConverter.ToUInt16(magic, 0) != 0x20B) throw new InvalidDataException("Expected PE32+ optional header.");
            var table = new byte[count * 40];
            if (!memory.Read(moduleBase + (ulong)(pe + 24 + optional), table, table.Length)) throw new InvalidDataException("Cannot read PE sections.");
            var result = new List<Tuple<ulong, ulong>>();
            for (int i = 0; i < count; i++)
            {
                int at = i * 40; uint flags = BitConverter.ToUInt32(table, at + 36);
                if ((flags & 0x20000000) == 0) continue;
                ulong start = BitConverter.ToUInt32(table, at + 12), length = BitConverter.ToUInt32(table, at + 8);
                if (length == 0) length = BitConverter.ToUInt32(table, at + 16);
                if (length == 0) continue;
                if (start < 4096 || start >= (ulong)size || length > (ulong)size - start) throw new InvalidDataException("Executable section exceeds module bounds.");
                if (result.Any(s => start < s.Item2 && start + length > s.Item1)) throw new InvalidDataException("Overlapping executable sections.");
                result.Add(Tuple.Create(start, start + length));
            }
            if (result.Count == 0) throw new InvalidDataException("No executable module sections.");
            return result;
        }

        static void ProcessBlock(byte[] bytes, int count, int nominal, ulong address, ulong moduleBase, ulong moduleEnd,
            IWildsMemory memory, Dictionary<string, HashSet<ulong>> candidates, Dictionary<string, HashSet<ulong>> probeCandidates, WildsScanReport report)
        {
            report.BytesRead += nominal;
            CollectMatches(signatures, candidates, false, bytes, count, nominal, address, moduleBase, moduleEnd, memory, report);
            CollectMatches(probes, probeCandidates, true, bytes, count, nominal, address, moduleBase, moduleEnd, memory, report);
        }

        static void CollectMatches(WildsByteSignature[] patterns, Dictionary<string, HashSet<ulong>> candidates, bool probe,
            byte[] bytes, int count, int nominal, ulong address, ulong moduleBase, ulong moduleEnd, IWildsMemory memory, WildsScanReport report)
        {
            foreach (var signature in patterns)
                foreach (int at in signature.Find(bytes, count))
                {
                    if (at >= nominal || (bytes[at + 2] & 0xC7) != 5) continue; // MOV / LEA r64,[RIP+disp32]
                    if (!TryRipTarget(address + (ulong)at, bytes, at, 3, 7, out ulong target)) continue;
                    if (target < moduleBase || target > moduleEnd - 8 || target % 8 != 0) continue;
                    if (!memory.Read(target, new byte[8], 8)) continue;
                    if (candidates[signature.Name].Count < 129) candidates[signature.Name].Add(target);
                    if (!probe && candidates[signature.Name].Count > 128) throw new InvalidDataException("Too many scan candidates.");
                    string source = probe ? "CandidateSignature" : "ActiveSignature";
                    if (report.CodeReferences.Count(r => r.Target == signature.Name && r.Source == source) >= 4) continue;
                    string rva = Hex(address + (ulong)at - moduleBase);
                    if (report.CodeReferences.Any(r => r.Target == signature.Name && r.InstructionRva == rva && r.Source == source)) continue;
                    int start = Math.Max(0, at - 16), end = Math.Min(count, at + 48);
                    report.CodeReferences.Add(new WildsCodeReference { Target = signature.Name, Pattern = signature.PatternText, Source = source, TargetRva = Hex(target - moduleBase), InstructionRva = rva,
                        ContextRva = Hex(address + (ulong)start - moduleBase), Bytes = BitConverter.ToString(bytes, start, end - start).Replace('-', ' ') });
                }
        }
    }
}
