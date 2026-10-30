using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SmartHunter.Config;
using SmartHunter.Core;
using SmartHunter.Core.Config;
using SmartHunter.Game;
using SmartHunter.Game.Config;
using SmartHunter.Game.Data.ViewModels;
using SmartHunter.Game.Helpers;
using SmartHunter.Ui.Windows;
using SmartHunter.Wilds;
using SmartHunter.World;
using static TestSupport;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

static class CatalogCompatibilityTests
{
    static JToken Fixture(string file)
    {
        using (var stream = typeof(CatalogCompatibilityTests).Assembly.GetManifestResourceStream("SmartHunter.Tests.Fixtures." + file))
        using (var reader = new StreamReader(stream)) return JToken.Parse(reader.ReadToEnd());
    }
    internal static void Identifiers()
    {
        var rows = (JArray)Fixture("wilds-monster-identifiers.json");
        var catalog = WildsCatalog.Instance;
        Assert(rows.Count == 35 && catalog.Monsters.Count == 35, "complete catalog contains every supported species and the real training object");
        foreach (var row in rows)
        {
            int index = (int)row["RuntimeIndex"]; string id = (string)row["Id"], name = (string)row["NameStringId"];
            Assert(WildsMonsterTypes.Id(index) == id && WildsMonsterTypes.Index(id) == index && catalog.Monster(index).NameStringId == name, "correct enum/EM/name identity: " + index);
            foreach (string language in new[] { "en-US", "zh-TW", "zh-CN" })
            {
                var strings = LanguageCatalog.ReadBuiltIn(language + ".Wilds.json");
                Assert(strings.ContainsKey(name) && !strings[name].StartsWith("LOC_"), "named monster in " + language + ": " + id);
                Assert(catalog.Monster(index).Parts.All(p => strings.ContainsKey(p.StringId)), "all native part slots translated: " + id);
            }
        }
        Assert(catalog.Monster(0).NameStringId == "LOC_MONSTER_RATHIAN" && catalog.Monster(13).NameStringId == "LOC_MONSTER_MIZUTSUNE", "zero is Rathian; thirteen is Mizutsune");
        Assert(catalog.Monster(-1) == null && catalog.Monster(4096) == null && WildsMonsterTypes.Id(4096).StartsWith("unknown_"), "future enum values cannot alias a known species");
        Assert(!catalog.Monsters.ContainsKey("em000_00"), "invalid generated EM ID removed");
    }
    static JObject LegacyMonsterFixture()
    {
        var monsters=new JObject();
        foreach(var row in (JArray)Fixture("wilds-monster-identifiers.json"))
        {
            int index=(int)row["RuntimeIndex"];
            monsters[(string)row["PreviousId"]]=new JObject {
                ["NameStringId"]="LOC_WILDS_MONSTER_"+index,
                ["BaseSize"]=1000+index*17,["ScaleModifier"]=1.25,
                ["Crowns"]=new JObject {["Mini"]=0.9,["Silver"]=1.15,["Gold"]=1.23},
                ["Parts"]=new JArray {
                    new JObject {["GroupId"]="Part",["StringId"]="LOC_PART_HEAD",["IsRemovable"]=false},
                    new JObject {["GroupId"]="Part",["StringId"]="LOC_PART_L_WING",["IsRemovable"]=false},
                    new JObject {["GroupId"]="Removable",["StringId"]="LOC_PART_R_WING",["IsRemovable"]=true}
                }
            };
        }
        return new JObject {["Monsters"]=monsters};
    }
    internal static void LegacyMonsterFile()
    {
        var input = LegacyMonsterFixture(); input["Custom"] = "keep";
        var rows = (JArray)Fixture("wilds-monster-identifiers.json");
        input["Monsters"]["em000_00"]["BaseSize"] = 9876.5;
        input["Monsters"]["em000_00"]["Custom"] = "Rathian setting";
        string converted = TextKeyNames.UpgradeData(input.ToString(), wildsMonsters: true);
        var output = JObject.Parse(converted);
        Assert(((JObject)output["Monsters"]).Count == 35 && (string)output["Custom"] == "keep", "all actual user entries and root metadata retained");
        foreach (var row in rows)
        {
            var before = input["Monsters"][(string)row["PreviousId"]]; var after = output["Monsters"][(string)row["Id"]];
            Assert(after != null && (string)after["NameStringId"] == (string)row["NameStringId"], "all user monster IDs converted without collisions");
            Assert(JToken.DeepEquals(before["BaseSize"], after["BaseSize"]) && JToken.DeepEquals(before["Crowns"], after["Crowns"]), "user dimensions/crowns preserved");
            var oldParts = (JArray)before["Parts"]; var newParts = (JArray)after["Parts"];
            Assert(oldParts.Count == newParts.Count, "no native slots removed or reordered");
            for (int i = 0; i < oldParts.Count; i++)
                Assert((string)newParts[i]["StringId"] == TextKeyNames.Canonical((string)oldParts[i]["StringId"]) && JToken.DeepEquals(oldParts[i]["IsRemovable"], newParts[i]["IsRemovable"]), "native part identity retained");
        }
        Assert((string)output["Monsters"]["em001_00"]["Custom"] == "Rathian setting" && TextKeyNames.UpgradeData(converted, wildsMonsters: true) == converted, "user settings survive and conversion is idempotent");
        Assert(!converted.Contains("LOC_WILDS_MONSTER_") && !converted.Contains("LOC_PART_L_") && !converted.Contains("LOC_PART_R_"), "active data contains semantic World-style keys");
        string root = Folder(), path = Path.Combine(root, "Config/Wilds/MonsterData.json"); Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, input.ToString());
        StorageLayout.Prepare(root); Assert(JToken.DeepEquals(JObject.Parse(File.ReadAllText(path)), output), "real startup applies the complete migration");
    }
    internal static void TranslationAliases()
    {
        const string input = "{\"Author\":\"User\",\"Strings\":{\"LOC_WILDS_MONSTER_13\":\"我的泡狐龍\",\"LOC_PART_WING_LEFT\":\"左翼\",\"LOC_PART_L_WING\":\"我的左翼\",\"LOC_PART_WING_RIGHT\":\"目前自訂右翼\",\"LOC_PART_R_WING\":\"右翼\",\"LOC_PLAYER_STATUS_EFFECT_HEALTH_REC_L\":\"我的大型回復\"}}";
        string migrated = LanguageCatalog.UpgradeBuiltInFile("zh-TW.Wilds.json", input); var data = JObject.Parse(migrated); var text = data["Strings"];
        Assert((string)text["LOC_MONSTER_MIZUTSUNE"] == "我的泡狐龍" && (string)text["LOC_PART_WING_LEFT"] == "我的左翼" && (string)text["LOC_PART_WING_RIGHT"] == "目前自訂右翼", "custom old aliases replace stock defaults; current custom entries win");
        Assert((string)text["LOC_STATUS_EFFECT_HEALTH_RECOVERY_L"] == "我的大型回復" && (string)data["Author"] == "User", "player semantic aliases preserve custom translations and metadata");
        Assert(LanguageCatalog.UpgradeBuiltInFile("zh-TW.Wilds.json", migrated) == null, "language migration never rewrites a second time");
    }
    internal static void DisplayAllTypes()
    {
        var original = GameProfile.Active; GameProfile.Active = GameChoice.Wilds; ConfigHelper.SwitchGameData(GameChoice.Wilds);
        var applier = new WildsSnapshotApplier();
        try
        {
            foreach (var row in (JArray)Fixture("wilds-monster-identifiers.json"))
            {
                var game = new FakeGame(); game.Monster((int)row["RuntimeIndex"]); var snapshot = game.Read();
                applier.Apply(snapshot); var overlay = OverlayViewModel.Instance.MonsterWidget.Context.Monsters.Single();
                string id = (string)row["Id"], name = LocalizationHelper.GetString((string)row["NameStringId"]);
                Assert(overlay.Id == id && overlay.Name == name && overlay.Health.Current == 1000, "actual reader boundary reaches the correct overlay: " + id);
                var relay = WildsRelayData.Create(snapshot, "test", "host", true, false); var view = WebViewData.Wilds(snapshot, relay.Monsters).Monsters.Single();
                Assert(view.Id == id && view.Name == name && relay.Monsters.Single().Id == (int)row["RuntimeIndex"], "web IDs are canonical; native relay enum is unchanged: " + id);
            }
            var translated = ConfigHelper.Localization.Values.Strings.First(p => p.Key.StartsWith("LOC_SUPPORT_HUNTER_", StringComparison.Ordinal));
            int npcId = Int32.Parse(translated.Key.Substring("LOC_SUPPORT_HUNTER_".Length));
            var npcGame = new FakeGame(); npcGame.Monster(0); ulong npc = npcGame.M.Alloc(), creation = npcGame.M.Alloc(), npcContext = npcGame.M.Alloc();
            npcGame.M.Int(npc + 0x44, 2); npcGame.M.Ptr(npc + 0x10, creation); npcGame.M.Ptr(npc + 0x28, npcContext); npcGame.M.Int(creation + 0x28, npcId);
            npcGame.M.Ptr(npcGame.F("NpcManager", "NpcParty"), npcGame.M.Array(npc)); var npcSnapshot = npcGame.Read();
            Assert(npcSnapshot.Players.Single(p => p.IsNpc).Name == translated.Key, "native support hunters use the new semantic translation key");
            applier.Apply(npcSnapshot); Assert(OverlayViewModel.Instance.TeamWidget.Context.Players.Single(p => p.Index == 1).Name == translated.Value, "support-hunter names remain translated in the team overlay");
        }
        finally { applier.Clear(); GameProfile.Active = original; ConfigHelper.SwitchGameData(original); }
    }
    internal static void OldPeerPartNames()
    {
        var local = new WildsSnapshot(); var monster = new WildsMonsterSnapshot { Address = 1, Id = 0, TargetKey = 2, Size = 100, MaxHealth = 1000, Health = 900 };
        monster.Parts.Add(new WildsPartSnapshot { Slot = 0, NameId = "LOC_PART_WING_LEFT", MaxHealth = 100, Health = 90 }); local.Monsters.Add(monster);
        var remote = WildsRelayData.Create(local, "test", "host", true, false).Monsters; remote[0].TargetKey = 3; remote[0].Parts[0].NameId = "LOC_PART_L_WING"; remote[0].Parts[0].Health = 42;
        Assert(WildsRelayData.Merge(local, remote, false, out int skipped) == 1 && monster.Parts[0].Health == 42 && monster.Health == 900, "older peers can still synchronize renamed body parts without replacing local HP");
    }
}

static class LanguageSelectionTests
{
internal static void LanguageSwitch()
    {
        var vm = SettingsViewModel.Instance;
        foreach (string file in new[] { "zh-CN.json", "en-US.json", "zh-TW.json" })
        {
            vm.SelectedLanguage = vm.Languages.First(l => l.FileName == LanguageCatalog.ProfileName(file));
            Assert((string)JObject.Parse(File.ReadAllText(ConfigHelper.Main.FullPathFileName))["LocalizationFileName"] == file, "saved language");
        }
        Assert(vm.Sections.First().Name == "一般設定", "updated labels");
        Assert(ConfigHelper.Main.FileName == "Settings.Wilds.json" && ConfigHelper.Memory.Values.ProcessName == "MonsterHunterWilds", "separate config");
        string custom = "test-locale-" + Guid.NewGuid().ToString("N") + ".json", path = Path.Combine(Root, custom);
        string content = "{\"Strings\":{\"LOC_UI_GENERAL\":\"Custom Wilds\"}}";
        File.WriteAllText(path, content);
        try
        {
            vm.SelectedLanguage = new LanguageOption(custom, "Custom"); Assert(vm.Sections[0].Name == "Custom Wilds", "custom locale applied");
            vm.SelectedLanguage = vm.Languages.First(l => l.FileName == "zh-TW.Wilds.json");
            Assert(vm.Sections[0].Name == "一般設定" && File.ReadAllText(path) == content, "no leaked strings or overwritten file");
        }
        finally { File.Delete(path); }
    }
internal static void LegacyLanguage()
    {
        var vm = SettingsViewModel.Instance;
        string[][] languages =
        {
            new[] { "en-US.json", "Track up to three large monsters, with the details you need.", "Show your selected hunt target. Turn off to track up to three large monsters.", "Connected to game · outside a hunting area" },
            new[] { "zh-TW.json", "最多追蹤三隻大型魔物，自訂你需要的狩獵資訊。", "只顯示目前選取的狩獵目標；關閉後最多追蹤三隻大型魔物。", "已連接遊戲 · 目前不在狩獵區域" },
            new[] { "zh-CN.json", "最多追踪三只大型怪物，自订你需要的狩猎信息。", "只显示目前选取的狩猎目标；关闭後最多追踪三只大型怪物。", "已连接游戏 · 当前不在狩猎区域" }
        };
        foreach (var language in languages)
        {
            string name = "test-legacy-" + Guid.NewGuid().ToString("N") + ".json", path = Path.Combine(Root, name);
            var content = new JObject { ["Strings"] = new JObject { ["LOC_UI_GENERAL"] = "My custom general", ["LOC_UI_MONSTERS_DESC"] = language[1], ["LOC_SETTING_SHOW_ONLY_SELECTED_MONSTER_DESC"] = language[2], ["LOC_UI_STATE_SAFEZONE"] = language[3] } }.ToString();
            File.WriteAllText(path, content);
            try
            {
                vm.SelectedLanguage = new LanguageOption(name, "Legacy");
                var builtin = LanguageCatalog.ReadBuiltIn(language[0]);
                Assert(UiText.Instance["LOC_UI_MONSTERS_DESC"] == builtin["LOC_UI_MONSTERS_DESC"] && UiText.Instance["LOC_SETTING_SHOW_ONLY_SELECTED_MONSTER_DESC"] == builtin["LOC_SETTING_SHOW_ONLY_SELECTED_MONSTER_DESC"], "old stock descriptions updated for " + language[0]);
                Assert(UiText.Instance["LOC_UI_STATE_SAFEZONE"] == builtin["LOC_UI_STATE_SAFEZONE"], "camp state explains when overlays appear");
                Assert(UiText.Instance["LOC_UI_GENERAL"] == "My custom general" && File.ReadAllText(path) == content, "custom strings and disk content preserved");
                var custom = new System.Collections.Generic.Dictionary<string, string> { ["LOC_UI_MONSTERS_DESC"] = "My custom monster description" };
                LanguageCatalog.UpgradeLegacyDescriptions(custom); Assert(custom["LOC_UI_MONSTERS_DESC"] == "My custom monster description", "custom description unchanged");
            }
            finally { vm.SelectedLanguage = vm.Languages.First(l => l.FileName == "zh-TW.Wilds.json"); File.Delete(path); }
        }
    }
}

static class LegacyTextTests
{
internal static void KeyMigration()
    {
        const string old="{\"Author\":\"User\",\"Strings\":{\"AILMENT_ENRAGE\":\"我的憤怒\",\"LOC_STATUS_EFFECT_RAGE\":\"憤怒\",\"ABNORMALITY_BLAST\":\"爆破異常\",\"AILMENT_BLAST\":\"爆破累積\",\"PART_HEAD\":\"我的頭部\",\"CUSTOM\":\"自訂\"}}";
        var document=JObject.Parse(LanguageCatalog.UpgradeBuiltInFile("zh-TW.Wilds.json",old));var strings=(JObject)document["Strings"];
        Assert((string)document["Author"]=="User" && (string)strings["LOC_STATUS_EFFECT_RAGE"]=="我的憤怒" && (string)strings["LOC_STATUS_EFFECT_BLASTBLIGHT"]=="爆破異常" && (string)strings["LOC_STATUS_EFFECT_BLAST"]=="爆破累積" && (string)strings["LOC_PART_HEAD"]=="我的頭部" && (string)strings["LOC_CUSTOM"]=="自訂" && strings["AILMENT_ENRAGE"]==null,"custom values migrate, distinct blast meanings do not collide");
        Assert(LanguageCatalog.UpgradeBuiltInFile("zh-TW.Wilds.json",document.ToString())==null,"second key upgrade does not rewrite the language");
        var data=JObject.Parse(TextKeyNames.UpgradeData("{\"StatusEffects\":[{\"NameStringId\":\"AILMENT_ENRAGE\"}],\"Parts\":[{\"StringId\":\"PART_HEAD\"}]}"));Assert((string)data["StatusEffects"][0]["NameStringId"]=="LOC_STATUS_EFFECT_RAGE" && (string)data["Parts"][0]["StringId"]=="LOC_PART_HEAD","editable data references migrate with the language");
    }
internal static void DiskNames()
    {
        string root=Folder(),language=Path.Combine(root,"Languages/Wilds/zh-TW.json"),data=Path.Combine(root,"Config/Wilds/MonsterData.json");Directory.CreateDirectory(Path.GetDirectoryName(language));Directory.CreateDirectory(Path.GetDirectoryName(data));
        File.WriteAllText(language,"{\"Author\":\"Custom\",\"Strings\":{\"AILMENT_ENRAGE\":\"自訂憤怒\",\"PART_HEAD\":\"自訂頭部\"}}");File.WriteAllText(data,"{\"StatusEffects\":[{\"NameStringId\":\"AILMENT_ENRAGE\"}],\"Monsters\":{\"em001_00\":{\"Parts\":[{\"StringId\":\"PART_HEAD\"}]}}}");
        StorageLayout.Prepare(root);var strings=JObject.Parse(File.ReadAllText(language));var monster=JObject.Parse(File.ReadAllText(data));
        Assert((string)strings["Author"]=="Custom" && (string)strings["Strings"]["LOC_STATUS_EFFECT_RAGE"]=="自訂憤怒" && (string)monster["StatusEffects"][0]["NameStringId"]=="LOC_STATUS_EFFECT_RAGE" && !File.ReadAllText(data).Contains("AILMENT_") && !File.ReadAllText(language).Contains("AILMENT_") && !Directory.Exists(Path.Combine(root,"Backups")),"disk upgrade changes references and keys together");
    }
}

static class LocalizationTests
{
    internal static void TaiwanDefaults()
    {
        foreach (string game in new[] { "World", "Wilds" })
        {
            string language = "zh-TW." + game + ".json";
            var document = JObject.Parse("{\"Author\":\"User\",\"Strings\":{\"LOC_UI_EDIT_PROFILE\":\"編輯配置\",\"LOC_UI_PROFILE_LANGUAGE\":\"此配置的語言\",\"LOC_PART_FORELEG_LEFT\":\"自訂左前腳\",\"LOC_STATUS_EFFECT_SLEEP\":\"我的睡眠\"}}");
            var upgraded = JObject.Parse(LanguageCatalog.UpgradeBuiltInFile(language, document.ToString()));
            var strings = upgraded["Strings"];
            Assert((string)strings["LOC_UI_EDIT_PROFILE"] == "設定對象" && (string)strings["LOC_UI_PROFILE_LANGUAGE"] == "這款遊戲的語言", "previous generated labels refresh: " + game);
            Assert((string)strings["LOC_PART_FORELEG_LEFT"] == "自訂左前腳" && (string)strings["LOC_STATUS_EFFECT_SLEEP"] == "我的睡眠" && (string)upgraded["Author"] == "User", "custom labels and metadata survive the wording upgrade: " + game);
            Assert(LanguageCatalog.UpgradeBuiltInFile(language, upgraded.ToString()) == null, "wording migration runs once: " + game);
            var defaults = LanguageCatalog.ReadBuiltIn(language);
            Assert(defaults["LOC_STATUS_EFFECT_ELDERSEAL"] == "龍封力" && defaults["LOC_STATUS_EFFECT_VIOLATED"] == "流口水", "user-requested World status meanings remain exact: " + game);
            Assert(defaults.Values.All(v => !v.Contains("配置") && !v.Contains("數據") && !v.Contains("全屬性異常狀態") && !v.Contains("／")), "Taiwan wording and separator audit: " + game);
        }
        var wilds = LanguageCatalog.ReadBuiltIn("zh-TW.Wilds.json");
        Assert(wilds["LOC_PART_FORELEG_LEFT_VEIL"].StartsWith("左") && wilds["LOC_PART_FORELEG_RIGHT_VEIL"].StartsWith("右") && wilds["LOC_PART_WINGARM_RIGHT_EXPOSED"].StartsWith("右"), "anatomical left/right distinctions survive translation");
    }

    static bool Valid(string key)=>Regex.IsMatch(key,@"^LOC_[A-Z0-9]+(?:_[A-Z0-9]+)*$");
    static JObject Fixture()
    {
        using(var stream=typeof(LocalizationTests).Assembly.GetManifestResourceStream("SmartHunter.Tests.Fixtures.legacy-text-keys.json"))
        using(var reader=new StreamReader(stream))
        {
            var keys=(JArray)JObject.Parse(reader.ReadToEnd())["Keys"];
            var strings=new JObject();int index=0;
            foreach(string key in keys)strings[key]="自訂翻譯 "+(++index);
            strings["ABNORMALITY_STUN"]="玩家昏厥";
            strings["AILMENT_STUN"]="魔物昏厥";
            return new JObject { ["Strings"]=strings };
        }
    }
internal static void Attachment()
    {
        var input=Fixture();input["Author"]="User";var original=(JObject)input["Strings"];
        Assert(original.Count==295 && original.Properties().Select(p=>TextKeyNames.Canonical(p.Name)).Distinct().Count()==295,"one-to-one mapping, including all unknown part/status IDs");
        var output=JObject.Parse(LanguageCatalog.UpgradeBuiltInFile("zh-TW.Wilds.json",input.ToString()));var strings=(JObject)output["Strings"];
        foreach(var pair in original.Properties())Assert((string)strings[TextKeyNames.Canonical(pair.Name)]==(string)pair.Value,"exact text preserved: "+pair.Name);
        Assert(strings.Properties().All(p=>Valid(p.Name)) && (string)output["Author"]=="User","all generated keys are canonical and metadata survives");
        Assert((string)strings["LOC_STATUS_EFFECT_PLAYER_STUN"]=="玩家昏厥" && (string)strings["LOC_STATUS_EFFECT_STUN"]=="魔物昏厥","player and monster meanings do not overwrite one another");
    }
internal static void Stock()
    {
        var catalog = SmartHunter.Wilds.WildsCatalog.Instance;
        var catalogReferences = catalog.Monsters.Values.SelectMany(m => m.Parts.Select(p => p.StringId).Concat(m.SoftenParts.Select(p => p.StringId)).Concat(new[] { m.NameStringId })).Concat(catalog.StatusEffects.Select(s => s.NameStringId)).Concat(catalog.PlayerStatusEffects.Select(s => s.NameStringId)).Distinct().ToArray();
        Assert(catalogReferences.All(k => k.StartsWith("LOC_")), "canonical immutable catalog references");
        var copy = LanguageCatalog.ReadBuiltIn("zh-TW.Wilds.json"); copy["LOC_STATUS_EFFECT_RAGE"] = "custom";
        Assert(LanguageCatalog.ReadBuiltIn("zh-TW.Wilds.json")["LOC_STATUS_EFFECT_RAGE"] == "憤怒", "cached translations remain isolated from caller overrides");
        foreach(var game in new[]{GameChoice.World,GameChoice.Wilds})foreach(string code in new[]{"en-US","zh-TW","zh-CN"})
        {
            var strings=LanguageCatalog.ReadBuiltIn(code+"."+game+".json");Assert(strings.Keys.All(Valid),"canonical stock keys: "+game+code);
            var monster=JObject.FromObject(new MonsterDataConfig(game));var player=JObject.FromObject(new PlayerDataConfig(game));
            var references=monster.SelectTokens("$..NameStringId").Concat(monster.SelectTokens("$..StringId")).Concat(player.SelectTokens("$..NameStringId")).Select(v=>(string)v).Distinct().ToArray();
            if (game == GameChoice.Wilds) references = references.Concat(catalogReferences).Distinct().ToArray();
            Assert(references.All(Valid),"canonical data references: "+game);
            string missing=String.Join(",",references.Where(k=>!strings.ContainsKey(k)));
            Assert(missing.Length==0,"missing labels in "+game+code+": "+missing);
        }
    }
internal static void LanguageIO()
    {
        string root=Folder(),path=Path.Combine(root,"custom.Wilds.json");var input=Fixture();input["Author"]="Original author";input["Extra"]=new JObject{["keep"]=42};File.WriteAllText(path,input.ToString());
        var active=GameProfile.Active;
        try
        {
            GameProfile.Active=GameChoice.Wilds;string inactive=Path.Combine(root,"Languages/World/en-US.json");Directory.CreateDirectory(Path.GetDirectoryName(inactive));File.WriteAllText(inactive,"{\"Strings\":{}}");
            var worldFile=new ConfigContainer<LocalizationConfig>(inactive);
            Assert(!worldFile.Values.Strings.Keys.Any(k=>k.StartsWith("LOC_PLAYER_STATUS_EFFECT_",StringComparison.Ordinal)),"inactive World language uses its own baseline and never leaks Wilds player strings");
        }
        finally{GameProfile.Active=active;}
        var file=new ConfigContainer<LocalizationConfig>(path);file.Values.Strings["LOC_STATUS_EFFECT_DASH_JUICE"]="自訂強走藥";file.Values.Strings["ABNORMALITY_TEST"]="自訂測試";file.Values.Strings["MY_CUSTOM_LABEL"]="保留";file.Save(false);
        var saved=JObject.Parse(File.ReadAllText(path));Assert(((JObject)saved["Strings"]).Properties().All(p=>Valid(p.Name)) && (string)saved["Strings"]["LOC_STATUS_EFFECT_DASH_JUICE"]=="自訂強走藥" && (string)saved["Strings"]["LOC_MY_CUSTOM_LABEL"]=="保留" && (string)saved["Author"]=="Original author" && (int)saved["Extra"]["keep"]==42,"save strips all aliases and retains metadata and user text");
        var aliases=JObject.Parse(LanguageCatalog.UpgradeBuiltInFile("zh-TW.Wilds.json","{\"Strings\":{\"UI_General\":\"自訂一般設定\",\"LOG_UPDATE_Download\":\"自訂分享日誌 {0}\"}}"));
        Assert((string)aliases["Strings"]["LOC_UI_GENERAL"]=="自訂一般設定" && (string)aliases["Strings"]["LOC_LOG_UPDATE_DOWNLOAD"]=="自訂分享日誌 {0}","former UI/log identifiers and format placeholders retain custom wording");
        string target=Path.Combine(root,"imported");var imported=SettingsImporter.Import(new[]{path},target,GameChoice.Wilds,"en-US.Wilds.json");var converted=JObject.Parse(File.ReadAllText(Path.Combine(target,imported.LanguageFileName)));
        Assert(((JObject)converted["Strings"]).Properties().All(p=>Valid(p.Name)) && (string)converted["Strings"]["LOC_STATUS_EFFECT_DASH_JUICE"]=="自訂強走藥","import preserves the same canonical contract");
    }
internal static void PlayerReferences()
    {
        const string data="{\"StatusEffects\":[{\"NameStringId\":\"LOC_STATUS_EFFECT_SLEEP\"},{\"NameStringId\":\"ABNORMALITY_PARALYSIS\"}],\"Extra\":\"keep\"}";
        var player=JObject.Parse(TextKeyNames.UpgradeData(data,true));var monster=JObject.Parse(TextKeyNames.UpgradeData(data));
        Assert((string)player["StatusEffects"][0]["NameStringId"]=="LOC_STATUS_EFFECT_PLAYER_SLEEP" && (string)player["StatusEffects"][1]["NameStringId"]=="LOC_STATUS_EFFECT_PLAYER_PARALYSIS" && (string)monster["StatusEffects"][0]["NameStringId"]=="LOC_STATUS_EFFECT_SLEEP","file kind resolves old shared identifiers without changing native status indices");
        var migrated=JObject.Parse(LanguageCatalog.UpgradeBuiltInFile("zh-TW.Wilds.json","{\"Strings\":{\"LOC_STATUS_EFFECT_SLEEP\":\"自訂睡眠\",\"ABNORMALITY_PARALYSIS\":\"玩家麻痺\",\"AILMENT_PARALYSIS\":\"魔物麻痺\"}}"));
        Assert((string)migrated["Strings"]["LOC_STATUS_EFFECT_PLAYER_SLEEP"]=="自訂睡眠" && (string)migrated["Strings"]["LOC_STATUS_EFFECT_PLAYER_PARALYSIS"]=="玩家麻痺" && (string)migrated["Strings"]["LOC_STATUS_EFFECT_PARALYSIS"]=="魔物麻痺","old canonical overrides and raw category aliases migrate together");
    }
    static void CheckDefaults(string root)
    {
        foreach(var game in new[]{GameChoice.World,GameChoice.Wilds})
        {
            foreach(string name in new[]{"Settings","MonsterData","PlayerData","Memory"})Assert(File.Exists(Path.Combine(root,"Config",game.ToString(),name+".json")),"first-run game file: "+game+name);
            foreach(string code in new[]{"en-US","zh-TW","zh-CN"})
            {
                string path=Path.Combine(root,"Languages",game.ToString(),code+".json");Assert(File.Exists(path),"first-run locale: "+path);Assert(((JObject)JObject.Parse(File.ReadAllText(path))["Strings"]).Properties().All(p=>Valid(p.Name)),"first-run language keys");
            }
            Assert((string)JObject.Parse(File.ReadAllText(Path.Combine(root,"Config",game.ToString(),"Memory.json")))["ProcessName"]==(game==GameChoice.World?"MonsterHunterWorld":"MonsterHunterWilds"),"correct game-specific memory defaults");
        }
        Assert(!Directory.Exists(Path.Combine(root,"Config/Shared")) && !Directory.Exists(Path.Combine(root,"Backups")),"no retired directories");
    }
internal static void Defaults()
    {
        string root=Folder();var active=GameProfile.Active;DefaultFiles.Ensure(root);CheckDefaults(root);Assert(GameProfile.Active==active,"creating inactive files never switches the game");
        string custom=Path.Combine(root,"Languages/World/zh-TW.json"),text="{\"Strings\":{\"LOC_CUSTOM\":\"保留\"},\"Author\":\"User\"}";File.WriteAllText(custom,text);var timestamp=File.GetLastWriteTimeUtc(custom);
        string missing=Path.Combine(root,"Languages/World/en-US.json");File.Delete(missing);DefaultFiles.Ensure(root);
        Assert(File.Exists(missing) && File.ReadAllText(custom)==text && File.GetLastWriteTimeUtc(custom)==timestamp,"recreate only missing files, preserve all existing custom bytes and timestamps");
    }
internal static void Startup()
    {
        string target=Folder();foreach(string name in new[]{"SmartHunter.exe","SmartHunter.exe.config","Newtonsoft.Json.dll","Microsoft.Xaml.Behaviors.dll","OxyPlot.dll","OxyPlot.Wpf.dll","Themes/Default.xaml"}){string path=Path.Combine(target,name);Directory.CreateDirectory(Path.GetDirectoryName(path));File.Copy(Path.Combine(Root,name),path);}
        using(var process=Process.Start(new ProcessStartInfo(Path.Combine(target,"SmartHunter.exe"),"--no-updates"){WorkingDirectory=target,WindowStyle=ProcessWindowStyle.Hidden}))
        {
            try{process.WaitForInputIdle(10000);Assert(SpinWait.SpinUntil(()=>{process.Refresh();return process.HasExited || process.MainWindowHandle!=IntPtr.Zero;},10000) && !process.HasExited,"clean app opens without a game");CheckDefaults(target);Assert(process.CloseMainWindow() && process.WaitForExit(10000) && process.ExitCode==0,"clean app closes normally");}
            finally{if(!process.HasExited){process.Kill();process.WaitForExit(3000);}}
        }
    }
}
