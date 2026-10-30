using System;
using System.Diagnostics;
using System.Linq;
using SmartHunter.Core;
using SmartHunter.Core.Helpers;

namespace SmartHunter.Game.Helpers
{
    internal static class WorldPlayerNames
    {
        internal sealed class Candidate
        {
            public long Offset;
            public ulong Address;
            public bool PlayerNameMatches, SessionFieldsValid;
        }
        internal sealed class Resolution
        {
            public ulong PointerSlot, RawAddress, Address;
            public long Offset;
            public string Reason;
            public Candidate[] Candidates;
        }
        internal static Resolution LastResolution;
        static string lastLogged;

        internal static ulong Resolve(Process process, ulong pointerSlot, ulong currentPlayerAddress = 0)
        {
            ulong raw = MemoryHelper.Read<ulong>(process, pointerSlot);
            var settings = SmartHunter.World.WorldConfigHelper.Memory.Values;
            string currentPlayer = MemoryHelper.ReadString(process,currentPlayerAddress,33);
            var offsets = new[] { settings.PlayerNameCollectionOffset, 0L, -0x30L }.Distinct();
            var candidates = offsets.Where(offset => raw != 0 && (offset >= 0 || raw >= (ulong)-offset))
                .Select(offset => Inspect(process, unchecked((ulong)((long)raw + offset)), offset, currentPlayer)).ToArray();
            var selected = candidates.FirstOrDefault(c => c.Offset == settings.PlayerNameCollectionOffset);
            string reason = "Configured";
            if(settings.AutoResolvePlayerNameCollection)
            {
                var anchored = candidates.Where(c => c.PlayerNameMatches).ToArray();
                if(anchored.Length == 1) { selected=anchored[0];reason="PlayerName"; }
                else if(anchored.Length > 1)
                {
                    var withSession=anchored.Where(c=>c.SessionFieldsValid).ToArray();
                    if(withSession.Length==1) { selected=withSession[0];reason="PlayerAndSession"; }
                    else reason="Ambiguous";
                }
                else
                {
                    var withSession=candidates.Where(c=>c.SessionFieldsValid).ToArray();
                    if(withSession.Length==1) { selected=withSession[0];reason="SessionFields"; }
                    else reason=withSession.Length>1?"Ambiguous":"NoAnchor";
                }
            }
            LastResolution = new Resolution { PointerSlot=pointerSlot,RawAddress=raw,Address=selected?.Address??0,
                Offset=selected?.Offset??settings.PlayerNameCollectionOffset,Reason=raw==0?"UnreadableRoot":reason,Candidates=candidates };
            string signature=process.Id+"/"+pointerSlot+"/"+LastResolution.Offset+"/"+LastResolution.Reason;
            if(signature!=lastLogged && raw!=0)
            {
                lastLogged=signature;
                Log.WriteLine(String.Format(LocalizationHelper.GetString("LOC_LOG_WORLD_NAME_TABLE"),LastResolution.Offset,
                    LocalizationHelper.GetString("LOC_UI_WORLD_TABLE_"+LastResolution.Reason)));
            }
            return LastResolution.Address;
        }
        static Candidate Inspect(Process process,ulong address,long offset,string player)
        {
            string session=MemoryHelper.ReadString(process,address+MhwHelper.DataOffsets.PlayerNameCollection.SessionID,13);
            string host=MemoryHelper.ReadString(process,address+MhwHelper.DataOffsets.PlayerNameCollection.SessionHostPlayerName,33);
            bool match = !String.IsNullOrEmpty(player) && Enumerable.Range(0,4).Any(i=>MemoryHelper.ReadString(process,
                address+MhwHelper.DataOffsets.PlayerNameCollection.FirstPlayerName+(ulong)(i*33),33)==player);
            return new Candidate {Offset=offset,Address=address,PlayerNameMatches=match,
                SessionFieldsValid=session.Length==12 && session.All(c=>c>32 && c<127) && !String.IsNullOrWhiteSpace(host) && !host.Any(c=>Char.IsControl(c)||c=='\uFFFD')};
        }
    }
}
