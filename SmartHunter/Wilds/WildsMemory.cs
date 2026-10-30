using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace SmartHunter.Wilds
{
    public interface IWildsMemory { bool Read(ulong address, byte[] buffer, int size); }

    public sealed class WildsProcessMemory : IWildsMemory, IDisposable
    {
        readonly SafeProcessHandle process;
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern SafeProcessHandle OpenProcess(uint access, bool inheritHandle, int processId);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool ReadProcessMemory(SafeProcessHandle handle, IntPtr address, byte[] destination, UIntPtr requested, out UIntPtr copied);
        public WildsProcessMemory(int id)
        {
            const uint readAndQuery = 0x0010 | 0x1000;
            process = OpenProcess(readAndQuery, false, id);
            if (process.IsInvalid)
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        public bool Read(ulong address, byte[] buffer, int size)
        {
            if (buffer == null || size < 0 || size > buffer.Length || address < 65536 || address > 0x7FFFFFFFFFFF)
                return false;
            return ReadProcessMemory(process, (IntPtr)address, buffer, (UIntPtr)(uint)size, out var copied) && copied.ToUInt64() == (ulong)size;
        }
        public void Dispose() => process.Dispose();
    }

    public sealed class WildsAddresses
    {
        readonly Dictionary<string, ulong> roots;
        readonly Dictionary<string, int[]> paths;
        public string Version { get; }
        public WildsAddresses(string version, IDictionary<string, ulong> resolvedRvas, IDictionary<string, int[]> fieldOffsets)
        {
            Version = version;
            roots = new Dictionary<string, ulong>(resolvedRvas, StringComparer.Ordinal);
            paths = fieldOffsets.ToDictionary(field => field.Key, field => (int[])field.Value.Clone(), StringComparer.Ordinal);
        }
        public ulong Address(string key, ulong moduleBase) => checked(moduleBase + roots[key]);
        public int[] Offsets(string key) => paths.TryGetValue(key, out var offsets) ? offsets : Array.Empty<int>();
        public bool HasOffset(string key) => paths.ContainsKey(key);
        public bool HasAddress(string key) => roots.ContainsKey(key);
    }

    public sealed class WildsMemoryView
    {
        const int MaximumRead = 65536, MaximumCachedPointers = 8192;
        readonly IWildsMemory source;
        readonly byte[] scalar = new byte[8];
        readonly Dictionary<ulong, ulong> pointers = new Dictionary<ulong, ulong>();
        bool cachePointers;
        public int PointerCacheHits { get; private set; }
        public WildsMemoryView(IWildsMemory memory) { source = memory; }
        public void BeginSnapshot() { pointers.Clear(); PointerCacheHits = 0; cachePointers = true; }
        public void EndSnapshot() { cachePointers = false; pointers.Clear(); }
        public bool ReadInto(ulong address, byte[] buffer, int size) => address != 0 && buffer != null && size >= 0 && size <= MaximumRead && size <= buffer.Length && source.Read(address, buffer, size);
        public byte[] Bytes(ulong address, int size)
        {
            if (address == 0 || size < 0 || size > MaximumRead)
                return null;
            var bytes = new byte[size];
            return source.Read(address, bytes, size) ? bytes : null;
        }
        bool Scalar(ulong address, int bytes) => address != 0 && source.Read(address, scalar, bytes);
        public ulong Ptr(ulong address)
        {
            if (address == 0)
                return 0;
            if (cachePointers && pointers.TryGetValue(address, out ulong stored))
            { PointerCacheHits++; return stored; }
            if (!Scalar(address, 8))
                return 0;
            ulong value = BitConverter.ToUInt64(scalar, 0);
            if (cachePointers && pointers.Count < MaximumCachedPointers)
                pointers.Add(address, value);
            return value;
        }
        public int Int(ulong address) => Scalar(address, 4) ? BitConverter.ToInt32(scalar, 0) : 0;
        public short Short(ulong address) => Scalar(address, 2) ? BitConverter.ToInt16(scalar, 0) : (short)0;
        public float Float(ulong address) => Scalar(address, 4) ? BitConverter.ToSingle(scalar, 0) : Single.NaN;
        public byte Byte(ulong address) => Scalar(address, 1) ? scalar[0] : (byte)0;
        static ulong Add(ulong address, int offset) => checked(address + checked((ulong)offset));
        ulong Walk(ulong start, int[] offsets, bool returnSlot)
        {
            ulong current = start;
            foreach (int offset in offsets)
            {
                if (returnSlot)
                { current = Ptr(current); if (current == 0) return 0; current = Add(current, offset); }
                else
                { if (current == 0) return 0; current = Ptr(Add(current, offset)); }
            }
            return current;
        }
        public ulong Field(ulong address, params int[] offsets) => Walk(address, offsets, true);
        public ulong Object(ulong address, params int[] offsets) => Walk(address, offsets, false);
        public ulong[] Array(ulong address, int max = 128)
        {
            if (address == 0)
                return System.Array.Empty<ulong>();
            int count = Int(checked(address + 28));
            if (count < 0 || count > max)
                return System.Array.Empty<ulong>();
            var bytes = Bytes(checked(address + 32), count * 8);
            if (bytes == null)
                return System.Array.Empty<ulong>();
            var values = new ulong[count];
            for (int i = 0; i < count; i++)
                values[i] = BitConverter.ToUInt64(bytes, i * 8);
            return values;
        }
        public string String(ulong address)
        {
            if (address == 0)
                return System.String.Empty;
            int characters = Int(checked(address + 16));
            if (characters < 1 || characters > 64)
                return System.String.Empty;
            var bytes = Bytes(checked(address + 20), characters * 2);
            return bytes == null ? System.String.Empty : Encoding.Unicode.GetString(bytes).TrimEnd((char)0);
        }
    }
}
