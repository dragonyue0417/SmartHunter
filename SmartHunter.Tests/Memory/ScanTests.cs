using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SmartHunter.Wilds;
using static TestSupport;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

static class ScanTests
{
internal static void Rip()
    {
        var b = new byte[7];
        Array.Copy(BitConverter.GetBytes(-0x107), 0, b, 3, 4);
        Assert(WildsPatternScanner.TryRipTarget(0x2000, b, 0, 3, 7, out ulong target) && target == 0x1F00, "negative disp32");
        Array.Copy(BitConverter.GetBytes(0x101), 0, b, 3, 4);
        Assert(WildsPatternScanner.TryRipTarget(0x2000, b, 0, 3, 7, out target) && target == 0x2108, "positive disp32");
        Assert(!WildsPatternScanner.TryRipTarget((ulong)long.MaxValue, b, 0, 3, 7, out target), "overflow");
        Assert(!WildsPatternScanner.TryRipTarget(0, b, 6, 3, 7, out target), "short instruction");
    }
internal static void Wildcards()
    {
        var random = new Random(715);
        for (int n = 0; n < 80; n++)
        {
            int length = random.Next(1, 30); var mask = new byte?[length];
            for (int i = 0; i < length; i++) mask[i] = random.Next(3) == 0 ? (byte?)null : (byte)random.Next(8);
            mask[random.Next(length)] = 3;
            string text = string.Join(" ", mask.Select(b => b.HasValue ? b.Value.ToString("X2") : "??"));
            var sig = new WildsByteSignature("test", text); var bytes = new byte[200]; random.NextBytes(bytes);
            for (int i = 0; i < length; i++) bytes[91 + i] = mask[i] ?? (byte)random.Next(256);
            var expected = new List<int>();
            for (int start = 0; start <= bytes.Length - length; start++)
                if (Enumerable.Range(0, length).All(i => !mask[i].HasValue || mask[i].Value == bytes[start + i])) expected.Add(start);
            Assert(sig.Find(bytes, bytes.Length).SequenceEqual(expected), "literal anchor mismatch " + text);
            Assert(!sig.Find(bytes, Math.Min(length - 1, bytes.Length)).Any(), "bounded count");
        }
    }
internal static void Versions()
    {
        // Every game version must resolve all eight instructions. The same
        // user-captured roots are relocated as synthetic test data, not old maps.
        string[] versions = { "1.42.0.0", "1.42.0.1", "1.42.0.2" };
        long[] shifts = { -0x10000, 0x24000, 0 };
        for (int i = 0; i < versions.Length; i++)
        {
            var image = new Image(shifts[i]); var result = image.Scan(versions[i]);
            Assert(result.Addresses != null, versions[i] + ": " + result.Report.Reason);
            foreach (var signature in WildsPatternScanner.Signatures.Concat(WildsPatternScanner.CandidateSignatures))
                Assert(result.Addresses.Address(signature.Name, 0) == LayoutFixtures.Create(shifts[i]).Address(signature.Name, 0), "scanned root: " + signature.Name);
            Assert(result.Report.AddressSource == "DirectSignatures" && result.Report.ScanPatterns.All(p => p.UsedForReading), "all versions use independent scan results");
            Assert(result.Report.ResolvedRvas.Count == 9 && result.Report.CandidateSignatures.All(p => p.UsedForReading), "complete diagnostic evidence");
            Assert(result.Addresses.Address("HealthEncryptionRound", 0) + 16 == result.Addresses.Address("HealthEncryptionKey", 0), "native vector adjacency");
        }
    }
internal static void Boundaries()
    {
        foreach (ulong start in new[] { 0x1000UL, 0x14A00000UL })
        {
            var image = new Image(0, start, 0x180000000);
            image.ClearCode(); image.AddManagers(WildsPatternScanner.ChunkSize - 12);
            var result = image.Scan();
            Assert(result.Addresses != null, result.Report.Reason);
            Assert(result.Addresses.Address("EnemyManager", image.Base) == image.Base + 0x14885180, "ASLR");
            // Identical target referenced twice is one root, and a signature in a
            // non-executable section must not create a second candidate.
            image.AddInstruction(0x5000, 0, image.Targets[0]);
            image.AddDataDecoy();
            Assert(image.Scan().Addresses != null, "duplicate reference / data section");
        }
    }
internal static void Ambiguity()
    {
        var image = new Image(0); ulong different = image.Targets[0] + 0x100;
        image.AddRegion(different, new byte[8]); image.AddInstruction(0x5000, 0, different);
        var result = image.Scan();
        Assert(result.Addresses == null && result.Report.ScanCandidates["PlayerManager"].Length == 2 && result.Report.ResolvedRvas.Count == 0, "ambiguous root accepted");
    }
internal static void IndependentManagers()
    {
        var image = new Image(0); var roots = DirectRoots(image, true);
        FillDirectSignatures(image, roots);
        var result = image.Scan("1.42.0.2");
        Assert(result.Addresses != null, "known versions cannot require old manager spacing: " + result.Report.Reason);
        foreach (var root in roots) Assert(result.Addresses.Address(root.Key, image.Base) == root.Value, "independent root changed: " + root.Key);
    }
internal static void Rejections()
    {
        var image = new Image(0); image.Blocked = image.Base + image.CodeRva + 0x9000;
        var result = image.Scan();
        Assert(result.Addresses == null && result.Report.UnreadablePages > 0, "partial scan accepted");
        image = new Image(0); image.Header[0] = 0;
        Assert(image.Scan().Addresses == null, "bad PE header accepted");
        image = new Image(0); image.ClearCode();
        for (int i = 0; i < 4; i++) image.AddInstruction(0x100 + i * 0x100, i, image.Targets[i]);
        result = image.Scan("1.43.0.0");
        Assert(result.Addresses == null && result.Report.BytesRead > 0 && result.Report.ScanCandidates.Values.All(v => v.Length == 1) &&
            result.Report.CandidateSignatures.Any(p => p.CandidateRvas.Length == 0) && !WildsPatternScanner.SupportsLayout("1.42.0.4"),
            "unknown version was blocked before scanning, or missing direct signatures were accepted");
        image.ClearCode(); image.AddInstruction(0x100, 0, image.Base - 8);
        result = image.Scan();
        Assert(result.Addresses == null && result.Report.ScanCandidates["PlayerManager"].Length == 0, "out-of-module reference accepted");
    }
    static Dictionary<string, ulong> DirectRoots(Image image, bool independentlyRelocated = false)
    {
        var roots = new Dictionary<string, ulong>();
        var patterns = WildsPatternScanner.Signatures.Concat(WildsPatternScanner.CandidateSignatures).ToArray();
        for (int i = 0; i < patterns.Length; i++)
        {
            // Synthetic, deliberately unrelated root locations. These are not
            // addresses obtained from another game's version or an online address file.
            ulong root = independentlyRelocated ? image.Base + 0x09100000UL + (ulong)(i * i + 3 * i) * 0x1010UL :
                i < 4 ? image.Targets[i] : LayoutFixtures.Create().Address(patterns[i].Name, image.Base);
            roots.Add(patterns[i].Name, root);
        }
        return roots;
    }
    static void FillDirectSignatures(Image image, Dictionary<string, ulong> roots, string omitted = null)
    {
        image.ClearCode(); int offset = 0x100;
        for (int i = 0; i < WildsPatternScanner.Signatures.Count; i++)
        {
            var name = WildsPatternScanner.Signatures[i].Name;
            image.AddRegion(roots[name], new byte[8]);
            if (name != omitted) image.AddInstruction(offset, i, roots[name]);
            offset += 0x100;
        }
        foreach (var probe in WildsPatternScanner.CandidateSignatures)
        {
            image.AddRegion(roots[probe.Name], new byte[probe.Name == "HealthEncryptionRound" ? 16 : 8]);
            if (probe.Name != omitted) image.AddProbe(offset, probe.Name, roots[probe.Name]);
            offset += 0x100;
        }
        image.AddRegion(roots["HealthEncryptionRound"] + 0x10, new byte[16]);
    }
    static void AssertDirectScan(WildsScanResult result, Dictionary<string, ulong> roots, ulong moduleBase)
    {
        Assert(result.Addresses != null && result.Report.Status == "Resolved", result.Report.Reason ?? "unknown scan did not resolve");
        Assert(result.Report.BytesRead > 0 && result.Report.AddressSource == "DirectSignatures" &&
            result.Report.FieldLayoutStatus == "UnverifiedGameVersion", "unknown field layout presented as a verified version");
        foreach (var root in roots)
            Assert(result.Addresses.Address(root.Key, moduleBase) == root.Value, "direct target changed: " + root.Key);
        Assert(result.Addresses.Address("HealthEncryptionKey", moduleBase) == roots["HealthEncryptionRound"] + 0x10, "key is not adjacent to scanned Round storage");
        Assert(result.Addresses.Offsets("MonsterHealth").SequenceEqual(new[] { 0x30, 0x38, 0x10 }), "internal health fields changed during root scan");
        Assert(result.Report.ResolvedRvas.Count == 9 && result.Report.CandidateSignatures.Count == 4 &&
            result.Report.CandidateSignatures.All(p => p.CandidateRvas.Length == 1 && p.UsedForReading), "direct probe evidence is incomplete or unused");
        Assert(result.Report.ScanPatterns.Count == 8 && result.Report.ScanPatterns.All(p => p.Pattern.Contains("??") && p.UsedForReading), "accepted wildcard patterns not recorded as reading sources");
    }
internal static void UnknownVersionScan()
    {
        var image = new Image(0); var roots = DirectRoots(image);
        FillDirectSignatures(image, roots);
        Assert(!WildsPatternScanner.SupportsLayout("1.42.0.4"), "unverified field layout marked verified");
        AssertDirectScan(image.Scan("1.42.0.4"), roots, image.Base);
    }
internal static void VerifiedPatchScan()
    {
        Assert(WildsPatternScanner.SupportsLayout("1.42.0.3"), "user-tested patch must be recognized");
        var image = new Image(0); var roots = DirectRoots(image, true);
        FillDirectSignatures(image, roots);
        var result = image.Scan("1.42.0.3");
        Assert(result.Addresses != null && result.Report.FieldLayoutStatus == "KnownGameVersion" && result.Report.AddressSource == "DirectSignatures", "verification must not switch back to relative roots");
        Assert(result.Report.ScanPatterns.Count == 8 && result.Report.ScanPatterns.All(p => p.UsedForReading) && result.Report.CandidateSignatures.All(p => p.UsedForReading), "all eight signatures must still be active");
        foreach (var root in roots) Assert(result.Addresses.Address(root.Key, image.Base) == root.Value, "verified patch must use independent AOB results: " + root.Key);
        foreach (var pattern in WildsPatternScanner.Signatures.Concat(WildsPatternScanner.CandidateSignatures))
        {
            FillDirectSignatures(image, roots, pattern.Name);
            Assert(image.Scan("1.42.0.3").Addresses == null, "verified status cannot bypass a missing signature: " + pattern.Name);
        }
    }
internal static void IndependentDirectRoots()
    {
        var image = new Image(0, 0x1000, 0x180000000); var roots = DirectRoots(image, true);
        FillDirectSignatures(image, roots);
        var result = image.Scan("1.42.0.4"); AssertDirectScan(result, roots, image.Base);
        Assert(result.Addresses.Address("CameraManager", 0) != LayoutFixtures.Create().Address("CameraManager", 0), "fixture did not move camera independently");
        Assert(result.Addresses.Address("QuestManager", 0) - result.Addresses.Address("PlayerManager", 0) !=
            LayoutFixtures.Create().Address("QuestManager", 0) - LayoutFixtures.Create().Address("PlayerManager", 0), "fixture accidentally preserved manager spacing");
    }
internal static void MissingUnknownSignatures()
    {
        foreach (var pattern in WildsPatternScanner.Signatures.Concat(WildsPatternScanner.CandidateSignatures))
        {
            var image = new Image(0); var roots = DirectRoots(image, true);
            FillDirectSignatures(image, roots, pattern.Name);
            var result = image.Scan("1.42.0.4");
            Assert(result.Addresses == null && result.Report.Status == "Rejected" && result.Report.ResolvedRvas.Count == 0 && result.Report.BytesRead > 0,
                "missing signature accepted or not scanned: " + pattern.Name);
            Assert(result.Report.Reason != null && result.Report.Reason.Contains(pattern.Name), "missing target absent from failure reason: " + pattern.Name);
            Assert(result.Report.ScanPatterns.Count == 8 && result.Report.CodeReferences.Select(r => r.Target).Distinct().Count() == 7,
                "rejection discarded diagnostic patterns or other matches: " + pattern.Name);
            if (WildsPatternScanner.CandidateSignatures.Any(p => p.Name == pattern.Name))
                Assert(result.Report.CandidateSignatures.Single(p => p.Target == pattern.Name).CandidateRvas.Length == 0, "missing probe reported as matched");
            else Assert(result.Report.ScanCandidates[pattern.Name].Length == 0, "missing active signature reported as matched");
        }
    }
internal static void DirectProbeDuplicates()
    {
        foreach (var probe in WildsPatternScanner.CandidateSignatures)
        {
            var image = new Image(0); var roots = DirectRoots(image, true);
            FillDirectSignatures(image, roots);
            image.AddProbe(0x4000, probe.Name, roots[probe.Name]);
            AssertDirectScan(image.Scan("1.42.0.4"), roots, image.Base);
            ulong different = roots[probe.Name] + 0x100;
            image.AddRegion(different, new byte[16]); image.AddProbe(0x5000, probe.Name, different);
            var result = image.Scan("1.42.0.4");
            Assert(result.Addresses == null && result.Report.Status == "Rejected" && result.Report.ResolvedRvas.Count == 0 &&
                result.Report.CandidateSignatures.Single(p => p.Target == probe.Name).CandidateRvas.Length == 2,
                "distinct direct targets accepted: " + probe.Name);
        }
    }
internal static void FutureVersions()
    {
        foreach (string version in new[] { "1.43.0.0", "2.8.7.6", "999.0.0.1" })
        {
            var image = new Image(0); var roots = DirectRoots(image, true);
            FillDirectSignatures(image, roots);
            Assert(!WildsPatternScanner.SupportsLayout(version), "fixture version unexpectedly has a known field profile");
            AssertDirectScan(image.Scan(version), roots, image.Base);
        }
    }
internal static void Cancellation()
    {
        var cancel = new CancellationTokenSource(); cancel.Cancel(); var image = new Image(0);
        try { WildsPatternScanner.Resolve(image, "1.42.0.2", image.Base, image.Size, cancel.Token); }
        catch (OperationCanceledException) { return; }
        throw new Exception("scan cancellation ignored");
    }
internal static void EncryptionStorage()
    {
        foreach (string name in new[] { "HealthEncryptionRound", "HealthEncryptionKey" })
        {
            var image = new Image(0);
            ulong address = LayoutFixtures.Create().Address(name, image.Base);
            image.Regions.RemoveAll(region => region.Item1 == address);
            image.AddRegion(address, new byte[8]);
            var result = image.Scan();
            Assert(result.Addresses == null && result.Report.Reason.Contains(name), "all sixteen parameter bytes must be readable: " + name);
        }
    }
internal static void NativeScan()
    {
        const int size = 0x300000; IntPtr block = Marshal.AllocHGlobal(size);
        try
        {
            var image = new Image(-0x14700000, 0x1000, unchecked((ulong)block.ToInt64()), size);
            var bytes = new byte[size];
            foreach (var region in image.Regions) Array.Copy(region.Item2, 0, bytes, (long)(region.Item1 - image.Base), region.Item2.Length);
            Marshal.Copy(bytes, 0, block, size);
            using (var memory = new WildsProcessMemory(Process.GetCurrentProcess().Id))
            {
                var result = WildsPatternScanner.Resolve(memory, "1.42.0.2", image.Base, size);
                Assert(result.Addresses != null, result.Report.Reason);
                Assert(result.Addresses.Address("PauseManager", image.Base) == image.Base + 0x18CBD8, "native scanned pause");
                int offset = 0x4000;
                foreach (var probe in WildsPatternScanner.CandidateSignatures)
                {
                    image.AddProbe(offset, probe.Name);
                    offset += 0x100;
                }
                Marshal.Copy(image.Code, 0, (IntPtr)(image.Base + image.CodeRva), image.Code.Length);
                var unknown = WildsPatternScanner.Resolve(memory, "1.42.0.4", image.Base, size);
                Assert(unknown.Addresses != null && unknown.Report.BytesRead > 0 && unknown.Report.AddressSource == "DirectSignatures", "native unknown-version scan: " + unknown.Report.Reason);
                Assert(unknown.Addresses.Address("HealthEncryptionKey", image.Base) == image.Base + 0x263140, "native unknown-version key follows scanned Round");
            }
        }
        finally { Marshal.FreeHGlobal(block); }
    }
    static byte[] ProbeBytes(string name)
    {
        var scan = LayoutFixtures.UserScan();
        var signature = WildsPatternScanner.CandidateSignatures.Single(s => s.Name == name);
        foreach (var reference in scan["CodeReferences"].Where(r => (string)r["Target"] == name))
        {
            var bytes = ((string)reference["Bytes"]).Split(' ').Select(s => Convert.ToByte(s, 16)).ToArray();
            int[] matches = signature.Find(bytes, bytes.Length).ToArray();
            if (matches.Length == 0) continue;
            int at = matches[0]; ulong context = Convert.ToUInt64(((string)reference["ContextRva"]).Substring(2), 16);
            ulong expected = Convert.ToUInt64(((string)reference["TargetRva"]).Substring(2), 16);
            Assert(WildsPatternScanner.TryRipTarget(context + (ulong)at, bytes, at, 3, 7, out ulong target) && target == expected, "actual RIP target " + name);
            return bytes.Skip(at).Take(signature.Length).ToArray();
        }
        throw new Exception("Candidate does not match supplied instruction windows: " + name);
    }
internal static void ActualInstructionWindows()
    {
        foreach (var signature in WildsPatternScanner.CandidateSignatures) Assert(ProbeBytes(signature.Name).Length == signature.Length, signature.Name);
        var data = LayoutFixtures.UserScan();
        var roots = ((JObject)data["ScanCandidates"]).Properties().ToDictionary(p => p.Name, p => Convert.ToUInt64(((string)p.Value.Single()).Substring(2), 16));
        foreach (var pattern in WildsPatternScanner.CandidateSignatures)
        {
            var references = data["CodeReferences"].Where(r => (string)r["Target"] == pattern.Name);
            roots[pattern.Name] = references.Select(r => Convert.ToUInt64(((string)r["TargetRva"]).Substring(2), 16)).Distinct().Single();
        }
        var resolved = WildsMemoryLayout.Resolve(roots, (string)data["GameVersion"]);
        foreach (var pair in ((JObject)data["ResolvedRvas"]).Properties())
            Assert(resolved.Address(pair.Name, 0) == Convert.ToUInt64(((string)pair.Value).Substring(2), 16), "real scan reproduced without an address file " + pair.Name);
    }
internal static void CandidateDiagnostics()
    {
        var image = new Image(0); int offset = 0x3000;
        foreach (var signature in WildsPatternScanner.CandidateSignatures)
        { image.AddProbe(offset, signature.Name); offset += 0x100; }
        var result = image.Scan();
        Assert(result.Addresses != null && result.Report.CandidateSignatures.Count == 4 && result.Report.CandidateSignatures.All(p => p.Status == "MatchesDirectScan" && p.UsedForReading), "candidate comparison");
        Assert(result.Report.ScanPatterns.Count == 8 && result.Report.ScanPatterns.All(p => p.Pattern.Contains("??")), "report includes every wildcard pattern");
        Assert(result.Report.CodeReferences.All(r => r.Pattern.Contains("??") && !r.Bytes.Contains("?")), "masked patterns and observed bytes stay distinct");
        ulong different = LayoutFixtures.Create().Address("CameraManager", image.Base) + 0x100;
        image.AddRegion(different, new byte[8]); image.AddProbe(0x5000, "CameraManager", different);
        result = image.Scan();
        Assert(result.Addresses == null && result.Report.CandidateSignatures.Single(p => p.Target == "CameraManager").Status == "Ambiguous", "ambiguous camera must reject reading");
    }
internal static void ExternalAddressFilesIgnored()
    {
        string directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Address");
        string path = Path.Combine(directory, "MemoryOverride.json");
        Assert(!File.Exists(path), "external address file unexpectedly shipped"); Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(path, "Address PlayerManager 0x147EDF60\n");
            var image = new Image(0); image.ClearCode(); var result = image.Scan();
            Assert(result.Addresses == null && result.Report.ResolvedRvas.Count == 0, "legacy file bypassed failed scan");
        }
        finally { File.Delete(path); }
    }
internal static void RuntimeChecks()
    {
        var data = new WildsSnapshot { IsReady = true, InHuntingZone = true, StageId = 1, PlayerName = "DO NOT EXPORT PLAYER NAME" };
        var monster = new WildsMonsterSnapshot { Health = 150, MaxHealth = 200, Selected = true };
        monster.Parts.Add(new WildsPartSnapshot()); monster.Ailments.Add(new WildsAilmentSnapshot()); data.Monsters.Add(monster);
        var check = WildsRuntimeCheck.From(data);
        Assert(check.MonsterCount == 1 && check.SelectedCount == 1 && check.PartCount == 1 && check.AilmentCount == 1 && check.AllHealthValid, "snapshot check counts");
        Assert(!JsonConvert.SerializeObject(check).Contains(data.PlayerName), "player name exported");
    }

    sealed class Image : IWildsMemory
    {
        public readonly ulong Base, CodeRva;
        public readonly int Size;
        public readonly byte[] Header = new byte[4096], Code = new byte[WildsPatternScanner.ChunkSize + 0x10000];
        public readonly List<Tuple<ulong, byte[]>> Regions = new List<Tuple<ulong, byte[]>>();
        public readonly ulong[] Targets = new ulong[4];
        public ulong Blocked;
        readonly WildsAddresses layout;
        // Byte fixtures transcribed from the CT's independent debug pattern list;
        // wildcard operands are concrete here so tests also check profile masks.
        static readonly string[] instructions = {
            "48 8B 15 00 00 00 00 E8 01 02 03 04 48 85 C0 74 2D 48 8B 40 30 48 8B 40 18",
            "48 8B 05 00 00 00 00 80 B8 E4 00 00 00 01 75 11 80 B8 E5 00 00 00 02 75 22 80 B8 E6 00 00 00 03 75",
            "48 8B 15 00 00 00 00 84 DB 74 1F 48 8B 82 D0 00 00 00",
            "48 8B 05 00 00 00 00 48 8B 80 48 01 00 00 BB 08 00 00 00 80 78 4C 00"
        };
        public Image(long shift, ulong codeRva = 0x1000, ulong moduleBase = 0x140000000, int size = 0x15000000)
        {
            Base = moduleBase; CodeRva = codeRva; Size = size; layout = LayoutFixtures.Create(shift);
            PutHeader(0, BitConverter.GetBytes((ushort)0x5A4D)); PutHeader(0x3C, BitConverter.GetBytes(0x80));
            PutHeader(0x80, BitConverter.GetBytes(0x4550)); PutHeader(0x84, BitConverter.GetBytes((ushort)0x8664));
            PutHeader(0x86, BitConverter.GetBytes((ushort)2)); PutHeader(0x94, BitConverter.GetBytes((ushort)0xF0));
            PutHeader(0x98, BitConverter.GetBytes((ushort)0x20B));
            PutHeader(0x188 + 8, BitConverter.GetBytes(Code.Length)); PutHeader(0x188 + 12, BitConverter.GetBytes((uint)CodeRva)); PutHeader(0x188 + 36, BitConverter.GetBytes(0x60000020));
            PutHeader(0x1B0 + 8, BitConverter.GetBytes(0x1000)); PutHeader(0x1B0 + 12, BitConverter.GetBytes(0x80000)); PutHeader(0x1B0 + 36, BitConverter.GetBytes(0xC0000040));
            AddRegion(Base, Header); AddRegion(Base + CodeRva, Code); ClearCode();
            for (int i = 0; i < 4; i++) { Targets[i] = layout.Address(WildsPatternScanner.Signatures[i].Name, Base); AddRegion(Targets[i], new byte[8]); }
            foreach (string key in new[] { "CameraManager", "PauseManager", "NpcManager", "HealthEncryptionKey", "HealthEncryptionRound" })
                AddRegion(layout.Address(key, Base), new byte[key.StartsWith("HealthEncryption") ? 16 : 8]);
            AddManagers(0x100);
            // A concrete RIP reference for diagnostic extraction, unrelated to the
            // four manager signatures and never used to infer the scan roots.
            var reference = new byte[] { 0x48, 0x8B, 0x05, 0, 0, 0, 0 };
            Array.Copy(BitConverter.GetBytes(checked((int)((long)layout.Address("CameraManager", Base) - (long)(Base + CodeRva + 0x3007)))), 0, reference, 3, 4);
            Array.Copy(reference, 0, Code, 0x3000, 7);
        }
        void PutHeader(int at, byte[] bytes) => Array.Copy(bytes, 0, Header, at, bytes.Length);
        public void AddRegion(ulong address, byte[] bytes) => Regions.Add(Tuple.Create(address, bytes));
        public void ClearCode() { for (int i = 0; i < Code.Length; i++) Code[i] = 0x90; }
        public void AddManagers(int at)
        {
            for (int i = 0; i < 4; i++) AddInstruction(at + i * 0x100, i, Targets[i]);
            for (int i = 0; i < WildsPatternScanner.CandidateSignatures.Count; i++)
                AddProbe(at + (i + 4) * 0x100, WildsPatternScanner.CandidateSignatures[i].Name);
        }
        public void AddInstruction(int at, int which, ulong target)
        {
            byte[] instruction = instructions[which].Split(' ').Select(s => Convert.ToByte(s, 16)).ToArray();
            Array.Copy(BitConverter.GetBytes(checked((int)((long)target - (long)(Base + CodeRva + (ulong)at + 7)))), 0, instruction, 3, 4);
            Array.Copy(instruction, 0, Code, at, instruction.Length);
        }
        public void AddProbe(int at, string name, ulong? target = null)
        {
            byte[] instruction = ProbeBytes(name);
            ulong destination = target ?? layout.Address(name, Base);
            Array.Copy(BitConverter.GetBytes(checked((int)((long)destination - (long)(Base + CodeRva + (ulong)at + 7)))), 0, instruction, 3, 4);
            Array.Copy(instruction, 0, Code, at, instruction.Length);
        }
        public void AddDataDecoy()
        {
            ulong fake = Targets[0] + 0x300; AddRegion(fake, new byte[8]); var data = new byte[0x1000];
            byte[] instruction = instructions[0].Split(' ').Select(s => Convert.ToByte(s, 16)).ToArray();
            Array.Copy(BitConverter.GetBytes(checked((int)((long)fake - (long)(Base + 0x80000 + 7)))), 0, instruction, 3, 4);
            Array.Copy(instruction, data, instruction.Length); AddRegion(Base + 0x80000, data);
        }
        public WildsScanResult Scan(string version = "1.42.0.2") => WildsPatternScanner.Resolve(this, version, Base, Size);
        public bool Read(ulong address, byte[] buffer, int count)
        {
            if (Blocked != 0 && address <= Blocked && address + (ulong)count > Blocked) return false;
            foreach (var region in Regions)
                if (address >= region.Item1 && address - region.Item1 <= (ulong)region.Item2.Length && (ulong)count <= (ulong)region.Item2.Length - (address - region.Item1))
                { Array.Copy(region.Item2, (long)(address - region.Item1), buffer, 0, count); return true; }
            return false;
        }
    }
}
