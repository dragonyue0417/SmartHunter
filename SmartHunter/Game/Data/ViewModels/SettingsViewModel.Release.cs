using SmartHunter.Core;
using SmartHunter.Core.Updates;
using SmartHunter.Game.Helpers;
using SmartHunter.World;

namespace SmartHunter.Game.Data.ViewModels
{
    public sealed partial class SettingsViewModel
    {
        public Command ShareWebLink {get;private set;}
        public bool CanShareWeb=>GameProfile.Active!=GameChoice.World;
        public string ShareWebLabel=>UiText.Instance[ConfigHelper.Main.Values.Servers.Wilds.ShareWebView?"LOC_UI_WEB_SHARE_COPY":"LOC_UI_WEB_SHARE_START_COPY"];
        public string ShareWebHint=>UiText.Instance[CanShareWeb?"LOC_UI_WEB_SHARE_QUICK_HINT":"LOC_UI_WEB_SHARE_WILDS_ONLY"];
        public string UpdateStatus=>UpdateService.Instance.Status;
        public string UpdateActionText=>UpdateService.Instance.ActionText;
        public bool CanUpdate=>!UpdateService.Instance.Busy;
        public Command UpdateAction=>UpdateService.Instance.Action;
        void RefreshReleaseActions()
        {
            NotifyPropertyChanged(nameof(CanShareWeb));NotifyPropertyChanged(nameof(ShareWebLabel));NotifyPropertyChanged(nameof(ShareWebHint));
            NotifyPropertyChanged(nameof(UpdateStatus));NotifyPropertyChanged(nameof(UpdateActionText));NotifyPropertyChanged(nameof(CanUpdate));
        }
        void InitializeReleaseActions()
        {
            ShareWebLink=new Command(_=>
            {
                if(!CanShareWeb)return;
                var server=ConfigHelper.Main.Values.Servers.Wilds;
                server.Enabled=true;server.ShareWebView=true;ConfigHelper.SaveSettings();
                ClipboardService.Copy(WebShareIdentity.Link(GameChoice.Wilds,server.ServerUrl));
                Log.WriteLine(UiText.Instance["LOC_LOG_WEB_SHARE_QUICK"]);RefreshReleaseActions();
            });
            UpdateService.Instance.Changed+=(s,e)=>RefreshReleaseActions();
            Add("General","LOC_UI_UPDATE_ENABLED",()=>EditingOptions.Updates.Enabled,value=>EditingOptions.Updates.Enabled=value,false);
            Setting source=null;
            source=new Setting("General","LOC_UI_UPDATE_URL",()=>EditingOptions.Updates.Url,value=>
            {
                if(!UpdateSource.TryParse(value,out var parsed)){source.ValidationMessage=UiText.Instance["LOC_UI_UPDATE_URL_INVALID"];return;}
                source.ValidationMessage="";EditingOptions.Updates.Url=parsed.Url;EditingOptions.Updates.PendingDirectory="";EditingOptions.Updates.PendingSource="";UpdateService.Instance.SourceChanged();ConfigHelper.SaveSettings();
            },true){IsWideText=true};Settings.Add(source);
        }
    }
}
