using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using SmartHunter.Core;
using SmartHunter.Core.Helpers;
using SmartHunter.Game.Data.ViewModels;

namespace SmartHunter.Game.Helpers
{
    internal static class WorldReadDiagnostics
    {
        static long nextCapture;
        internal static void Observe(Process process, ulong slot, ulong names, ulong player, ulong lobby)
        {
            if (!ConfigHelper.Main.Values.Overlay.DebugWidget.IsVisible || ConfigHelper.Main.Values.Debug.UseSampleData) return;
            long now=(long)(Stopwatch.GetTimestamp()*1000d/Stopwatch.Frequency);
            if(now<nextCapture) return;
            nextCapture=now+5000;
            try
            {
                WriteSnapshot(Capture(process,slot,names,player,lobby),OverlayViewModel.Instance.MonsterWidget.Context.Monsters.Any());
            }
            catch(Exception error) when(error is IOException || error is UnauthorizedAccessException || error is System.ComponentModel.Win32Exception || error is InvalidOperationException)
            { Log.WriteLine("World read diagnostics: "+error.Message); }
        }
        internal static void WriteSnapshot(object report,bool hasMonsters)
        {
            string contents=JsonConvert.SerializeObject(report,Formatting.Indented);
            File.WriteAllText(FileContainer.GetFullPathFileName("Read.World.json"),contents,new UTF8Encoding(false));
            if(hasMonsters)
                File.WriteAllText(FileContainer.GetFullPathFileName("Read.World.Monsters.json"),contents,new UTF8Encoding(false));
        }
        internal static object Capture(Process process, ulong slot, ulong names, ulong player, ulong lobby)
        {
            var game=OverlayViewModel.Instance.DebugWidget.Context.CurrentGame;
            var offsets=typeof(MhwHelper.DataOffsets.PlayerNameCollection).GetFields().ToDictionary(f=>f.Name,f=>f.GetValue(null));
            string computedRoomKey=null;
            if(!String.IsNullOrEmpty(game.SessionID) && !String.IsNullOrEmpty(game.LobbyID) && game.LobbyID!="Not Online")
                using(var sha=SHA1.Create()) computedRoomKey=Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(game.SessionID+game.LobbyID)));
            return new {
                Schema=4, Reader="OriginalSmartHunter", AppVersion=typeof(WorldReadDiagnostics).Assembly.GetName().Version.ToString(),
                CapturedAtUtc=DateTime.UtcNow, GameVersion=process.MainModule.FileVersionInfo.FileVersion,
                Purpose="LocalOnly: World session fields and monster status diagnostics; never uploaded",
                PlayerNamesPointerSlot=Hex(slot),PlayerNamesBase=Hex(names),PointerWasAbove32Bits=names>UInt32.MaxValue,
                PlayerNamesResolution=WorldPlayerNames.LastResolution,
                CurrentPlayerAddress=Hex(player),LobbyStatusAddress=Hex(lobby),Offsets=offsets,
                Fields=new {
                    Player=Field(process,player,33),
                    SessionId=Field(process,names==0?0:names+MhwHelper.DataOffsets.PlayerNameCollection.SessionID,13),
                    SessionHost=Field(process,names==0?0:names+MhwHelper.DataOffsets.PlayerNameCollection.SessionHostPlayerName,33),
                    LobbyId=Field(process,names==0?0:names+MhwHelper.DataOffsets.PlayerNameCollection.LobbyID,13),
                    LobbyHosts=Enumerable.Range(0,4).Select(i=>Field(process,names==0?0:names+MhwHelper.DataOffsets.PlayerNameCollection.LobbyHostPlayerName+(ulong)i*MhwHelper.DataOffsets.PlayerNameCollection.NextLobbyHostName,33)).ToArray()
                },
                Displayed=new { game.CurrentPlayerName,game.SessionID,game.SessionHostPlayerName,game.LobbyID,game.LobbyHostPlayerName,game.LobbyHostWasFallback },
                LegacySync=new { ComputedRoomKey=computedRoomKey,RegisteredRoomKey=game.key,
                    RegistrationMatches=computedRoomKey!=null && computedRoomKey==game.key,
                    game.helloDone,game.checkDone,game.playersCheckDone,
                    ServerEnabled=ConfigHelper.Main.Values.Overlay.MonsterWidget.UseNetworkServer },
                StatusDefinitions=ConfigHelper.MonsterData.Values.StatusEffects.Select((s,i)=>new { Index=i,s.GroupId,s.NameStringId }).ToArray(),
                StatusSettings=new {ConfigHelper.MonsterData.FileName,ConfigHelper.Main.Values.Overlay.MonsterWidget.ShowStatusEffects,
                    ConfigHelper.Main.Values.Overlay.MonsterWidget.ShowUnchangedStatusEffects,ConfigHelper.Main.Values.Overlay.MonsterWidget.HideStatusEffectsAfterSeconds,
                    ConfigHelper.Main.Values.Overlay.MonsterWidget.IncludeStatusEffectGroupIdRegex},
                Monsters=OverlayViewModel.Instance.MonsterWidget.Context.Monsters.Select(m=>new {
                    m.Id,Address=Hex(m.Address),CachedNativeStatuses=m.StatusEffects.Count(s=>s.GroupId=="StatusEffect"),
                    NativeStatuses=WorldStatusDiagnostics.Probe(process,m),
                    CachedStatusSources=WorldStatusDiagnostics.ProbeCachedSources(process,m),
                    Statuses=m.StatusEffects.Select(s=>new {s.Index,s.Name,Address=Hex(s.Address),s.GroupId,s.IsVisible,s.InitialTime,s.LastChangedTime,Buildup=s.Buildup.Current,MaxBuildup=s.Buildup.Max,Remaining=s.Duration.Current,MaxDuration=s.Duration.Max,s.TimesActivatedCount}).ToArray()
                }).ToArray()
            };
        }
        static object Field(Process process,ulong address,int length)
        {
            byte[] data=new byte[length];UIntPtr read=UIntPtr.Zero;
            bool success=address!=0 && WindowsApi.ReadProcessMemory(process.Handle,(IntPtr)address,data,(UIntPtr)(uint)length,out read) && read.ToUInt64()==(ulong)length;
            int end=Array.IndexOf(data,(byte)0);if(end<0) end=data.Length;
            return new {Address=Hex(address),ReadSucceeded=success,Text=success?Encoding.UTF8.GetString(data,0,end):"",Bytes=success?BitConverter.ToString(data).Replace('-',' '):""};
        }
        static string Hex(ulong value)=>"0x"+value.ToString("X");
    }
}
