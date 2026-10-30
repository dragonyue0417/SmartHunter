using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using SmartHunter.Core.Updates;

namespace SmartHunter.Core.Helpers
{
    // The original client hands installation to the verified Unified EXE.
    // Its helper waits for this process to exit and preserves user data.
    public class Updater
    {
        Task<UpdatePackage> check;
        Task<string> download;
        public bool CheckCompleted => check != null && check.IsCompleted;
        public bool HasUpdate => CheckCompleted && check.Status == TaskStatus.RanToCompletion && check.Result != null;
        public bool DownloadCompleted => download != null && download.IsCompleted;
        public bool DownloadSucceeded => DownloadCompleted && download.Status == TaskStatus.RanToCompletion && download.Result != null;

        public void BeginCheck()
        {
            if (check != null) return;
            check = Task.Run(async () =>
            {
                try
                {
                    using (var client = new GitHubUpdateClient())
                    {
                        var package = await client.Check(UpdateSource.DefaultUrl, Assembly.GetExecutingAssembly().GetName().Version, CancellationToken.None).ConfigureAwait(false);
                        Log.WriteLine(package == null ? "No newer Unified version is available." : "Found SmartHunter Unified " + package.Version + ".");
                        return package;
                    }
                }
                catch (Exception error)
                {
                    Log.WriteLine("Unable to check for the Unified upgrade; continuing with the original client.");
                    Log.WriteException(error);
                    return null;
                }
            });
        }

        public void BeginDownload()
        {
            if (download != null || !HasUpdate) return;
            var package = check.Result;
            download = Task.Run(async () =>
            {
                try
                {
                    using (var client = new GitHubUpdateClient())
                    {
                        string stage = await client.Download(package, CancellationToken.None, AppDomain.CurrentDomain.BaseDirectory).ConfigureAwait(false);
                        Log.WriteLine("Unified program files downloaded and verified. Existing settings and translations were preserved.");
                        return stage;
                    }
                }
                catch (Exception error)
                {
                    Log.WriteLine("Unable to download the Unified upgrade; continuing with the original client.");
                    Log.WriteException(error);
                    return null;
                }
            });
        }

        public bool LaunchInstallation()
        {
            if (!DownloadSucceeded) return false;
            try
            {
                UpdateFiles.Launch(download.Result, AppDomain.CurrentDomain.BaseDirectory);
                return true;
            }
            catch (Exception error)
            {
                Log.WriteException(error);
                return false;
            }
        }
    }
}
