using System.Collections.Generic;
using SmartHunter.Core.Config;
using SmartHunter.Game.Helpers;
namespace SmartHunter.Config
{
    public sealed class LocalizationConfig
    {
        [PreserveCollectionIntegrity]
        public Dictionary<string,string> Strings;
        public LocalizationConfig() : this("en-US.json") { }
        public LocalizationConfig(string fileName) { Strings = LanguageCatalog.ReadBuiltIn(fileName); }
    }
}
