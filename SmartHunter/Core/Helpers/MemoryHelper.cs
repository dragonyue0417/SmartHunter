using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace SmartHunter.Core.Helpers
{
    public static class MemoryHelper
    {
        static readonly List<PointerTrace> s_UniquePointerTraces = new List<PointerTrace>();
        [ThreadStatic] static byte[] s_ReadBuffer;
        const int ScanChunkSize = 1024 * 1024;

        static class ValueSize<T> where T : struct
        {
            public static readonly int Size = Marshal.SizeOf(typeof(T));
        }

        static byte[] GetReadBuffer(int size)
        {
            if (s_ReadBuffer == null || s_ReadBuffer.Length < size)
                s_ReadBuffer = new byte[Math.Max(size, 128)];
            return s_ReadBuffer;
        }

        static bool CheckProtection(uint flags)
        {
            return (flags & (uint)(WindowsApi.RegionPageProtection.PAGE_GUARD |
                                  WindowsApi.RegionPageProtection.PAGE_NOACCESS)) == 0;
        }

        public static List<ulong> FindPatternAddresses(Process process, AddressRange addressRange,
            BytePattern pattern, bool stopAfterFirst, CancellationToken cancellationToken = default(CancellationToken))
        {
            var matches = new List<ulong>();
            if (pattern.Bytes.Length == 0 || addressRange.Size == 0) return matches;
            // Bound allocation independently of the game's committed region size.
            var buffer = new byte[checked(ScanChunkSize + pattern.Bytes.Length - 1)];
            ulong currentAddress = addressRange.Start;
            while (currentAddress < addressRange.End && !process.HasExited)
            {
                cancellationToken.ThrowIfCancellationRequested();
                WindowsApi.MEMORY_BASIC_INFORMATION64 region;
                if (WindowsApi.VirtualQueryEx(process.Handle, (IntPtr)currentAddress, out region,
                    (uint)Marshal.SizeOf(typeof(WindowsApi.MEMORY_BASIC_INFORMATION64))) == 0) break;
                ulong regionEnd = region.BaseAddress + region.RegionSize;
                if (region.RegionSize == 0 || regionEnd <= currentAddress) break;
                if (region.State == (uint)WindowsApi.RegionPageState.MEM_COMMIT && CheckProtection(region.Protect))
                {
                    ulong end = Math.Min(regionEnd, addressRange.End);
                    for (ulong start = Math.Max(currentAddress, region.BaseAddress); start < end;)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        int count = (int)Math.Min((ulong)buffer.Length, end - start);
                        UIntPtr bytesRead;
                        WindowsApi.ReadProcessMemory(process.Handle, (IntPtr)start, buffer, (UIntPtr)(uint)count, out bytesRead);
                        int actual = (int)Math.Min((ulong)count, bytesRead.ToUInt64());
                        // Never search the unread (possibly previously filled) buffer tail.
                        foreach (int offset in FindPatternMatchIndices(buffer, pattern, stopAfterFirst, actual))
                        {
                            ulong address = start + (ulong)offset;
                            matches.Add(address);
                            pattern.Config.LastResultAddress = address.ToString("X8");
                            Log.WriteLine($"Found '{pattern.Config.Name}' at address 0x{address:X8}");
                        }
                        if (stopAfterFirst && matches.Count > 0) return matches;
                        if ((ulong)count == end - start) break;
                        start += ScanChunkSize; // pattern-length overlap between chunks
                    }
                }
                currentAddress = regionEnd;
            }
            return matches;
        }

        // A KMP prefix table cannot safely treat arbitrary '?' bytes as equal.
        // Anchor search retains overlapping matches and validates every fixed byte.
        internal static List<int> FindPatternMatchIndices(byte[] bytes, BytePattern pattern,
            bool stopAfterFirst, int count = -1)
        {
            var matches = new List<int>();
            int length = count < 0 ? bytes.Length : Math.Min(count, bytes.Length);
            int size = pattern.Bytes.Length;
            if (size == 0 || length < size) return matches;
            int anchor = size - 1;
            while (anchor >= 0 && !pattern.Bytes[anchor].HasValue) anchor--;
            int last = length - size;
            int start = 0;
            while (start <= last)
            {
                if (anchor >= 0)
                {
                    int found = Array.IndexOf(bytes, pattern.Bytes[anchor].Value, start + anchor, last - start + 1);
                    if (found < 0) break;
                    start = found - anchor;
                }
                bool match = true;
                for (int i = 0; i < size; i++)
                {
                    if (pattern.Bytes[i].HasValue && bytes[start + i] != pattern.Bytes[i].Value)
                    { match = false; break; }
                }
                if (match)
                {
                    matches.Add(start);
                    if (stopAfterFirst) break;
                }
                start++;
            }
            return matches;
        }

        public static List<AddressRange> DivideAddressRange(AddressRange range, int divisionCount)
        {
            if (divisionCount <= 0) throw new ArgumentOutOfRangeException(nameof(divisionCount));
            var divisions = new List<AddressRange>();
            ulong start = range.Start;
            ulong quotient = range.Size / (ulong)divisionCount;
            ulong remainder = range.Size % (ulong)divisionCount;
            for (int i = 0; i < divisionCount; i++)
            {
                ulong size = quotient + ((ulong)i < remainder ? 1UL : 0UL);
                if (size == 0) break;
                divisions.Add(new AddressRange(start, size));
                start += size;
            }
            return divisions;
        }

        public static T Read<T>(Process process, ulong address) where T : struct
        {
            if (address == 0) return default(T);
            int size = ValueSize<T>.Size;
            byte[] bytes = GetReadBuffer(size);
            UIntPtr bytesRead;
            if (!WindowsApi.ReadProcessMemory(process.Handle, (IntPtr)address, bytes, (UIntPtr)(uint)size, out bytesRead)
                || bytesRead.ToUInt64() != (ulong)size) return default(T);
            // Reuse scalar buffers and avoid a pinned GC handle for every read.
            if (typeof(T) == typeof(ulong)) return (T)(object)BitConverter.ToUInt64(bytes, 0);
            if (typeof(T) == typeof(long)) return (T)(object)BitConverter.ToInt64(bytes, 0);
            if (typeof(T) == typeof(float)) return (T)(object)BitConverter.ToSingle(bytes, 0);
            if (typeof(T) == typeof(int)) return (T)(object)BitConverter.ToInt32(bytes, 0);
            if (typeof(T) == typeof(uint)) return (T)(object)BitConverter.ToUInt32(bytes, 0);
            if (typeof(T) == typeof(byte)) return (T)(object)bytes[0];
            if (typeof(T) == typeof(short)) return (T)(object)BitConverter.ToInt16(bytes, 0);
            if (typeof(T) == typeof(ushort)) return (T)(object)BitConverter.ToUInt16(bytes, 0);
            if (typeof(T) == typeof(double)) return (T)(object)BitConverter.ToDouble(bytes, 0);
            GCHandle handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try { return (T)Marshal.PtrToStructure(handle.AddrOfPinnedObject(), typeof(T)); }
            finally { handle.Free(); }
        }

        public static string ReadString(Process process, ulong address, uint length)
        {
            if (address == 0 || length == 0) return string.Empty;
            byte[] bytes = GetReadBuffer(checked((int)length));
            UIntPtr bytesRead;
            if (!WindowsApi.ReadProcessMemory(process.Handle, (IntPtr)address, bytes, (UIntPtr)length, out bytesRead)
                || bytesRead.ToUInt64() != length) return string.Empty;
            int end = Array.IndexOf(bytes, (byte)0, 0, (int)length);
            return Encoding.UTF8.GetString(bytes, 0, end < 0 ? (int)length : end);
        }

        public static ulong ReadMultiLevelPointer(bool traceUniquePointers, Process process, ulong address, params long[] offsets)
        {
            PointerTrace trace = traceUniquePointers ? new PointerTrace() : null;
            foreach (long offset in offsets)
            {
                if (address == 0) return 0;
                ulong read = Read<ulong>(process, address);
                if (read == 0) return 0;
                ulong result = unchecked((ulong)((long)read + offset));
                if (trace != null) trace.Levels.Add(new PointerTraceLevel(address, read, offset, result));
                address = result;
            }
            if (trace != null)
            {
                lock (s_UniquePointerTraces)
                {
                    if (!s_UniquePointerTraces.Contains(trace))
                    {
                        s_UniquePointerTraces.Add(trace);
                        Log.WriteLine($"Unique Pointer Trace:\r\n{trace}");
                    }
                }
            }
            return address;
        }

        public static ulong LoadEffectiveAddressRelative(Process process, ulong address)
        {
            int operand = Read<int>(process, address + 3);
            return unchecked((ulong)((long)address + operand + 7));
        }

    }
}
