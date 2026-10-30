using System;
using System.ComponentModel;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using ErrorEventArgs = Newtonsoft.Json.Serialization.ErrorEventArgs;

namespace SmartHunter.Core.Config
{
    public class ConfigContainer<T> : FileContainer
    {
        public T Values = (T)Activator.CreateInstance(typeof(T), new object[] { });

        public event EventHandler Loaded;

        public ConfigContainer(string fileName) : base(fileName)
        {
            bool isDesignInstance = LicenseManager.UsageMode == LicenseUsageMode.Designtime;
            if (!isDesignInstance)
            {
                Load();
            }
        }

        public void HandleDeserializationError(object sender, ErrorEventArgs args)
        {
            Log.WriteException(args.ErrorContext.Error);
            args.ErrorContext.Handled = true;
        }
        
        override protected void OnChanged()
        {
            Load();
        }

        public void Reload() { Load(); }

        void Load()
        {
            // A language is an English baseline plus bundled and user overrides.
            // Reusing the old dictionary would leak translations between languages.
            if (Values is SmartHunter.Config.LocalizationConfig)
            {
                var localization = new SmartHunter.Config.LocalizationConfig("en-US." + (SmartHunter.Game.Helpers.LanguageCatalog.IsWorld(FileName) ? "World" : "Wilds") + ".json");
                foreach (var pair in SmartHunter.Game.Helpers.LanguageCatalog.ReadBuiltIn(FileName))
                    localization.Strings[pair.Key] = pair.Value;
                Values = (T)(object)localization;
            }
            if (File.Exists(FullPathFileName))// && (FileName.Equals("Config.json") || FileName.Equals("Memory.json")))
            {
                try
                {
                    string contents = null;
                    using (FileStream stream = File.Open(FullPathFileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                        {
                            contents = reader.ReadToEnd();
                        }
                    }

                    var settings = new JsonSerializerSettings();
                    settings.Formatting = Formatting.Indented;
                    settings.ContractResolver = new ContractResolver();
                    settings.Error = HandleDeserializationError;

                    // Solves dictionary/lists being added to instead of overwritten but causes problems elsewhere
                    // https://stackoverflow.com/questions/29113063/json-net-why-does-it-add-to-list-instead-of-overwriting
                    // https://stackoverflow.com/questions/27848547/explanation-for-objectcreationhandling-using-newtonsoft-json
                    // This has been moved to ContractResolver to target Dictionaries specifically
                    // settings.ObjectCreationHandling = ObjectCreationHandling.Replace;
                    settings.Converters.Add(new StringEnumConverter());
                    settings.Converters.Add(new StringFloatConverter());

                    string upgraded = Values is SmartHunter.Config.LocalizationConfig
                        ? SmartHunter.Game.Helpers.LanguageCatalog.UpgradeBuiltInFile(FileName, contents)
                        : Values is SmartHunter.Game.Config.MonsterDataConfig || Values is SmartHunter.Game.Config.PlayerDataConfig
                          ? SmartHunter.Game.Helpers.TextKeyNames.UpgradeData(contents, Values is SmartHunter.Game.Config.PlayerDataConfig && !SmartHunter.Game.Helpers.LanguageCatalog.IsWorld(FileName), Values is SmartHunter.Game.Config.MonsterDataConfig && !SmartHunter.Game.Helpers.LanguageCatalog.IsWorld(FileName)) : null;
                    JsonConvert.PopulateObject(upgraded ?? contents, Values, settings);
                    var localization = (object)Values as SmartHunter.Config.LocalizationConfig;
                    if (localization != null)
                    {
                        SmartHunter.Game.Helpers.LanguageCatalog.UpgradeLegacyDescriptions(localization.Strings, FileName);
                        SmartHunter.Game.Helpers.LanguageCatalog.NormalizeSeparators(localization.Strings);
                    }

                    Log.WriteLine($"{FileName} loaded");
                    if (upgraded != null && upgraded != contents)
                    {
                        // Persist new editable keys without replacing custom values
                        // or additional fields in the user's language document.
                        TryPauseWatching();
                        try { AtomicFile.Write(FullPathFileName, upgraded); }
                        finally { TryUnpauseWatching(); }
                    }
                }
                catch (Exception ex)
                {
                    Log.WriteException(ex);
                }
            }
            else
            {
                Save();
            }

            if (Loaded != null)
            {
                Loaded(this, null);
            }
        }

        public void Save(bool printToLog = true)
        {
            TryPauseWatching();

            try
            {
                var settings = new JsonSerializerSettings();
                settings.Formatting = Formatting.Indented;
                settings.NullValueHandling = NullValueHandling.Ignore;
                settings.ContractResolver = new ContractResolver();

                settings.Converters.Add(new StringEnumConverter());
                settings.Converters.Add(new StringFloatConverter());

                var jsonString = JsonConvert.SerializeObject(Values, settings);
                if (Values is SmartHunter.Config.LocalizationConfig language)
                {
                    var document = File.Exists(FullPathFileName) ? Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(FullPathFileName)) : new Newtonsoft.Json.Linq.JObject();
                    document["Strings"] = Newtonsoft.Json.Linq.JObject.FromObject(language.Strings);
                    SmartHunter.Game.Helpers.TextKeyNames.Normalize(document, FileName, SmartHunter.Game.Helpers.LanguageCatalog.ReadBuiltIn(FileName));
                    language.Strings = document["Strings"].ToObject<System.Collections.Generic.Dictionary<string,string>>();
                    jsonString = document.ToString(Formatting.Indented);
                }
                else if (Values is SmartHunter.Game.Config.MonsterDataConfig || Values is SmartHunter.Game.Config.PlayerDataConfig)
                    jsonString = SmartHunter.Game.Helpers.TextKeyNames.UpgradeData(jsonString, Values is SmartHunter.Game.Config.PlayerDataConfig && !SmartHunter.Game.Helpers.LanguageCatalog.IsWorld(FileName), Values is SmartHunter.Game.Config.MonsterDataConfig && !SmartHunter.Game.Helpers.LanguageCatalog.IsWorld(FileName));

                AtomicFile.Write(FullPathFileName, jsonString);

                if (printToLog)
                {
                    Log.WriteLine($"{FileName} saved");
                }
            }
            catch (Exception ex)
            {
                Log.WriteException(ex);
            }

            TryUnpauseWatching();
        }
    }
}
