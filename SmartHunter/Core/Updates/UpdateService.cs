using System;
using System.IO;
using System.Threading;
using System.Windows;
using SmartHunter.Core.Data;
using SmartHunter.Game.Helpers;
using SmartHunter.World;

namespace SmartHunter.Core.Updates
{
    public sealed class UpdateService : Bindable,IDisposable
    {
        public static UpdateService Instance {get;}=new UpdateService();
        readonly CancellationTokenSource cancellation=new CancellationTokenSource();
        string state="Idle",stage="";
        string stageSource="";
        public bool Busy {get;private set;}
        public string Status=>UiText.Instance["LOC_UI_UPDATE_"+state.ToUpperInvariant()];
        public string ActionText=>UiText.Instance[Ready?"LOC_UI_UPDATE_RESTART":"LOC_UI_UPDATE_CHECK"];
        public bool Ready=>!String.IsNullOrEmpty(stage);
        public Command Action {get;}
        public event EventHandler Changed;
        public void SourceChanged() { stage="";stageSource="";State("Idle"); }
        UpdateService(){Action=new Command(_=>{if(!Busy){if(Ready)Apply();else Check(false);}});}
        void State(string value)
        {
            state=value;NotifyPropertyChanged(nameof(Status));NotifyPropertyChanged(nameof(ActionText));NotifyPropertyChanged(nameof(Busy));Changed?.Invoke(this,EventArgs.Empty);
        }
        public async void Check(bool startup)
        {
            if(Busy)return;
            var settings=ConfigHelper.Main.Values.GetOptions(GameProfile.Active).Updates;
            string source=settings.Url;
            if(startup&&!settings.Enabled){State("Disabled");return;}
            Busy=true;State("Checking");
            try
            {
                Version current=typeof(UpdateService).Assembly.GetName().Version;
                if(!String.IsNullOrEmpty(settings.PendingDirectory))
                {
                    try
                    {
                    string incoming=Path.GetFullPath(settings.PendingDirectory);
                    string expectedRoot=Path.Combine(Path.GetTempPath(),"SmartHunter-Updates")+Path.DirectorySeparatorChar;
                    if(settings.PendingSource==source && incoming.StartsWith(expectedRoot,StringComparison.OrdinalIgnoreCase) && Directory.Exists(incoming) && UpdateFiles.Validate(incoming)>current){stage=incoming;stageSource=source;}
                    else {settings.PendingDirectory="";settings.PendingSource="";ConfigHelper.SaveSettings();}
                    }
                    catch(Exception error) when(error is InvalidDataException || error is IOException || error is UnauthorizedAccessException || error is System.Security.SecurityException || error is ArgumentException || error is Newtonsoft.Json.JsonException)
                    {settings.PendingDirectory="";settings.PendingSource="";ConfigHelper.SaveSettings();Log.WriteLine(UiText.Instance["LOC_LOG_UPDATE_FAILED"]+" "+error.Message);}
                }
                if(!Ready)
                {
                    Log.WriteLine(UiText.Instance["LOC_LOG_UPDATE_CHECK"]);
                    using(var client=new GitHubUpdateClient())
                    {
                        var release=await client.Check(source,current,cancellation.Token);
                        if(settings.Url!=source){State("Idle");return;}
                        if(release==null){State("Current");Log.WriteLine(UiText.Instance["LOC_LOG_UPDATE_CURRENT"]);return;}
                        State("Downloading");Log.WriteLine(String.Format(UiText.Instance["LOC_LOG_UPDATE_DOWNLOAD"],release.Version,release.Size));
                        string downloaded=await client.Download(release,cancellation.Token,FileContainer.GetFullPath());
                        if(settings.Url!=source){State("Idle");return;}
                        stage=downloaded;stageSource=source;
                        settings.PendingDirectory=stage;settings.PendingSource=source;ConfigHelper.SaveSettings();
                    }
                }
                State("Ready");Log.WriteLine(UiText.Instance["LOC_LOG_UPDATE_READY"]);
                if(startup&&!GameProfile.Running("MonsterHunterWorld")&&!GameProfile.Running("MonsterHunterWilds"))Apply();
            }
            catch(UpdateSourceMissingException){State("SourceMissing");Log.WriteLine(String.Format(UiText.Instance["LOC_LOG_UPDATE_SOURCE_MISSING"],source));}
            catch(OperationCanceledException){if(!cancellation.IsCancellationRequested){State("Failed");Log.WriteLine(UiText.Instance["LOC_LOG_UPDATE_TIMEOUT"]);}}
            catch(Exception error){State("Failed");Log.WriteLine(UiText.Instance["LOC_LOG_UPDATE_FAILED"]+" "+error.Message);}
            finally{Busy=false;State(state);}
        }
        void Apply()
        {
            try
            {
                var settings=ConfigHelper.Main.Values.GetOptions(GameProfile.Active).Updates;
                if(stageSource!=settings.Url){SourceChanged();return;}
                UpdateFiles.Validate(stage);
                UpdateFiles.Launch(stage,FileContainer.GetFullPath());
                Application.Current.Shutdown();
            }
            catch(Exception error){State("Failed");Log.WriteLine(UiText.Instance["LOC_LOG_UPDATE_FAILED"]+" "+error.Message);}
        }
        public void Dispose(){cancellation.Cancel();cancellation.Dispose();}
    }
}
