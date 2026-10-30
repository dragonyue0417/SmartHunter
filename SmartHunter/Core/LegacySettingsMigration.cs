using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SmartHunter.Game.Config;
using SmartHunter.World;

namespace SmartHunter.Core
{
    public static class LegacySettingsMigration
    {
        public static int Convert(string root)
            => Convert(root, new LegacyFileArchive(root));

        internal static int Convert(string root, LegacyFileArchive archive)
        {
            int count = ConvertFrom(root, root, archive);
            string nested = archive.NestedInstallation();
            if (nested != null) count += ConvertFrom(root, nested, archive);
            return count;
        }

        static int ConvertFrom(string root, string sourceRoot, LegacyFileArchive archive)
        {
            int count=0;
            foreach(var item in new[]{new{File="Config.json",Game=GameChoice.World},new{File="Config.Wilds.json",Game=GameChoice.Wilds}})
            {
                string source=Path.Combine(sourceRoot,item.File),target=GameSettingsStore.PathFor(root,item.Game);
                if(!File.Exists(source) || !archive.SafePath(source) || File.Exists(target))continue;
                if (!archive.SafePath(target) || !archive.SafePath(Path.Combine(root, "Languages", item.Game.ToString())))
                    throw new IOException("Migration destination contains a reparse point.");
                var document=JObject.Parse(File.ReadAllText(source));
                if(!(document["Overlay"] is JObject))throw new InvalidDataException("Legacy settings have no Overlay: "+item.File);
                var data=new Dictionary<string,string>();var languages=new List<string>();
                foreach(string file in Directory.GetFiles(sourceRoot,"*.json"))
                {
                    if (!archive.SafePath(file)) continue;
                    string name=Path.GetFileName(file);
                    if(item.Game==GameChoice.World && name.EndsWith(".Wilds.json",StringComparison.OrdinalIgnoreCase) || item.Game==GameChoice.Wilds && name.EndsWith(".World.json",StringComparison.OrdinalIgnoreCase))continue;
                    if(new[]{"Memory.json","MonsterData.json","PlayerData.json"}.Contains(name) && item.Game==GameChoice.World)
                    {JObject.Parse(File.ReadAllText(file));data[file]=Path.Combine(root,"Config","World",name);}
                    else
                    {
                        try{if(JObject.Parse(File.ReadAllText(file))["Strings"] is JObject)languages.Add(file);}catch(JsonException){}
                    }
                }
                string temporary=Path.Combine(Path.GetTempPath(),"SmartHunter-Migration-"+Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(temporary);
                try
                {
                string languageFolder=Path.Combine(root,"Languages",item.Game.ToString());
                string stagedLanguages=Path.Combine(temporary,"Languages",item.Game.ToString());Directory.CreateDirectory(stagedLanguages);
                if(Directory.Exists(languageFolder))foreach(string language in Directory.GetFiles(languageFolder,"*.json"))
                {JObject.Parse(File.ReadAllText(language));File.Copy(language,Path.Combine(stagedLanguages,Path.GetFileName(language)));}
                var values=new MainConfig();
                SettingsImporter.Import(new[]{source}.Concat(languages),temporary,item.Game,(string)document["LocalizationFileName"]??"en-US.json",values);
                var converted=JObject.Parse(File.ReadAllText(GameSettingsStore.PathFor(temporary,item.Game)));
                foreach(string option in new[]{"ShutdownWhenProcessExits","SkinFileName","BackgroundImagePath","InterfaceFontSize"})if(document[option]!=null)converted[option]=document[option].DeepClone();
                if (!String.Equals(root, sourceRoot, StringComparison.OrdinalIgnoreCase))
                    foreach (string key in new[] { "SkinFileName", "BackgroundImagePath" })
                    {
                        string value = (string)converted[key];
                        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && !uri.IsFile) continue;
                        if (!String.IsNullOrWhiteSpace(value) && !Path.IsPathRooted(value) && value != "Default.xaml")
                        {
                            string asset = Path.GetFullPath(Path.Combine(sourceRoot, value));
                            if (archive.SafePath(asset) && File.Exists(asset)) converted[key] = asset;
                        }
                    }
                if(document["StartMHWWhenSmartHunterStart"]!=null)converted["StartGameWithSmartHunter"]=document["StartMHWWhenSmartHunterStart"].DeepClone();
                if(document["Keybinds"] is JObject keys)
                {
                    var retained=new JObject();foreach(string name in new[]{"ManipulateWidget","HideWidgets"})if(keys[name]!=null)retained[name]=keys[name].DeepClone();converted["Keybinds"]=retained;
                }
                if(document["AutomaticallyCheckAndDownloadUpdates"]?.Type==JTokenType.Boolean)
                    converted["Updates"]=new JObject{["Enabled"]=document["AutomaticallyCheckAndDownloadUpdates"].DeepClone(),["Repository"]="dragonyue0417/SmartHunter"};
                converted.Remove("ReleaseDefaultsVersion");GameSettingsStore.UpgradeDocument(converted);
                // Preflight the final converted settings before retiring the old file.
                GameSettingsStore.Apply(new MainConfig(),item.Game,converted);
                foreach(var pair in data)if(!File.Exists(pair.Value))AtomicFile.Write(pair.Value,File.ReadAllText(pair.Key));
                foreach(string language in Directory.GetFiles(stagedLanguages,"*.json"))AtomicFile.Write(Path.Combine(languageFolder,Path.GetFileName(language)),File.ReadAllText(language));
                AtomicFile.Write(target,converted.ToString(Formatting.Indented));
                count++;
                // A nested installation is kept intact until it can be moved
                // as a whole. Root-level originals are retained in the archive.
                if (String.Equals(root, sourceRoot, StringComparison.OrdinalIgnoreCase))
                {
                    archive.Move(source);
                    foreach(var file in languages){string destination=Path.Combine(root,"Languages",item.Game.ToString(),Path.GetFileName(file));if(File.Exists(destination) && archive.Move(file))count++;}
                    foreach(var pair in data)if(File.Exists(pair.Value) && archive.Move(pair.Key))count++;
                }
                }
                finally{if(Directory.Exists(temporary))Directory.Delete(temporary,true);}
            }
            return count;
        }
    }
}
