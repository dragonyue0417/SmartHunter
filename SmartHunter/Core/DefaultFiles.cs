using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using SmartHunter.Core.Config;
using SmartHunter.Game.Config;
using SmartHunter.Game.Helpers;
using SmartHunter.World;

namespace SmartHunter.Core
{
    public static class DefaultFiles
    {
        public static void Ensure(string root)
        {
            var values = GameSettingsStore.Read(root);
            var serializer = JsonSerializer.Create(new JsonSerializerSettings { ContractResolver = new ContractResolver(), Converters = { new StringEnumConverter() } });
            foreach (var game in new[] { GameChoice.World, GameChoice.Wilds })
            {
                if (!File.Exists(GameSettingsStore.PathFor(root, game))) GameSettingsStore.Write(root, values, game);
                foreach (string code in new[] { "en-US", "zh-TW", "zh-CN" })
                {
                    string path = Path.Combine(root,"Languages",game.ToString(),code+".json");
                    if (File.Exists(path)) continue;
                    var strings = LanguageCatalog.ReadBuiltIn("en-US."+game+".json");
                    foreach (var pair in LanguageCatalog.ReadBuiltIn(code+"."+game+".json")) strings[pair.Key]=pair.Value;
                    AtomicFile.Write(path, new JObject { ["Strings"]=JObject.FromObject(strings) }.ToString(Formatting.Indented));
                }
                foreach (var pair in new[] {
                    new { Name="MonsterData", Create=(Func<object>)(()=>new MonsterDataConfig(game)) },
                    new { Name="PlayerData", Create=(Func<object>)(()=>new PlayerDataConfig(game)) },
                    new { Name="Memory", Create=(Func<object>)(()=>game==GameChoice.World ? (object)new WorldMemoryConfig() : new MemoryConfig()) } })
                {
                    string path=Path.Combine(root,"Config",game.ToString(),pair.Name+".json");
                    if (!File.Exists(path)) AtomicFile.Write(path,JObject.FromObject(pair.Create(),serializer).ToString(Formatting.Indented));
                }
                string selected = StorageLayout.Resolve(root,values.GetLanguage(game));
                if (!File.Exists(selected) && Path.GetFullPath(selected).StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
                    AtomicFile.Write(selected,new JObject { ["Strings"]=JObject.FromObject(LanguageCatalog.ReadBuiltIn("en-US."+game+".json")) }.ToString(Formatting.Indented));
            }
        }
    }
}
