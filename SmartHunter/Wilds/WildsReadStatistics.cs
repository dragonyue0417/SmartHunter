using System;
namespace SmartHunter.Wilds
{
    public sealed class WildsReadStatistics
    {
        public int Calls, PointerCacheHits;
        public long BytesRead;
        public double ElapsedMilliseconds;
    }
    internal sealed class WildsReadMemory : IWildsMemory
    {
        readonly IWildsMemory source;
        public int Calls;
        public long BytesRead;
        public WildsReadMemory(IWildsMemory source) { this.source=source; }
        public void Reset() { Calls=0;BytesRead=0; }
        public bool Read(ulong address,byte[] buffer,int size)
        {
            Calls++;
            bool success=source.Read(address,buffer,size);
            if(success)BytesRead+=size;
            return success;
        }
    }
}
