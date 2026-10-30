using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Xaml;
using SmartHunter.Core;
using SmartHunter.Game;
using SmartHunter.Game.Data.ViewModels;
using SmartHunter.Game.Helpers;
using SmartHunter.Ui.Windows;
using XamlReader = System.Windows.Markup.XamlReader;

namespace SmartHunter
{
    public partial class App : Application
    {
        WildsOverlay m_Overlay;
        FileContainer m_SkinFile;

        string m_LastSkinFileName;
        string m_LegacyArchive;
        int m_LegacyArchivedItems;
        string[] m_LegacyArchiveErrors;

        public App()
        {
            DispatcherUnhandledException += (s, e) => Log.WriteException(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                if (e.ExceptionObject is Exception error) Log.WriteException(error);
            };
            // Resource dictionaries can instantiate view models during
            // InitializeComponent, before OnStartup. Migrate before any of
            // those models can create a default classified configuration.
            StorageLayout.Prepare(FileContainer.GetFullPath());
            m_LegacyArchive = StorageLayout.LastArchiveDirectory;
            m_LegacyArchivedItems = StorageLayout.LastArchivedItems;
            m_LegacyArchiveErrors = StorageLayout.LastArchiveErrors;
            DefaultFiles.Ensure(FileContainer.GetFullPath());
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            SmartHunter.World.GameProfile.Initialize();
            //var culture = new System.Globalization.CultureInfo("es-ES");
            //System.Globalization.CultureInfo.CurrentCulture = culture;
            //System.Globalization.CultureInfo.CurrentUICulture = culture;

            // Initialize the console view model first thing so we can see any problems that may occur
            var consoleViewModel = ConsoleViewModel.Instance;

            Log.WriteLine($"Started SmartHunter {Assembly.GetExecutingAssembly().GetName().Version}");
            //Log.WriteLine($"Culture: {System.Globalization.CultureInfo.CurrentCulture.Name}");

            SetPerMonitorDpiAwareness();

            ConfigHelper.EnsureConfigs();
            if (m_LegacyArchivedItems > 0)
                Log.WriteLine(String.Format(UiText.Instance["LOC_LOG_LEGACY_ARCHIVED"], m_LegacyArchivedItems, m_LegacyArchive));
            foreach (string error in m_LegacyArchiveErrors ?? new string[0])
                Log.WriteLine(String.Format(UiText.Instance["LOC_LOG_LEGACY_ARCHIVE_FAILED"], error));
            ConfigHelper.Main.Loaded += Config_Loaded;
            ConfigHelper.SettingsSaved += Config_Loaded;

            m_SkinFile = new FileContainer(ConfigHelper.Main.Values.SkinFileName);
            m_SkinFile.Changed += (s1, e1) => { LoadSkin(); };
            LoadSkin();

            m_Overlay = new WildsOverlay(new ConsoleWindow(), new TeamWidgetWindow(), new MonsterWidgetWindow(), new PlayerWidgetWindow(), new DebugWidgetWindow());

            base.OnStartup(e);
            if(!e.Args.Contains("--no-updates"))SmartHunter.Core.Updates.UpdateService.Instance.Check(true);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            ConfigHelper.SettingsSaved -= Config_Loaded;
            m_Overlay?.Stop();
            SmartHunter.Core.Updates.UpdateService.Instance.Dispose();
            Log.WriteLine("Ended");
            base.OnExit(e);
        }

        private void Config_Loaded(object sender, EventArgs e)
        {
            if (m_LastSkinFileName != ConfigHelper.Main.Values.SkinFileName)
            {
                LoadSkin();
            }
        }

        void LoadSkin()
        {
            if (ConfigHelper.Main.Values.Debug.UseInternalSkin)
            {
                return;
            }

            var skinFileName = ConfigHelper.Main.Values.SkinFileName;
            if (!File.Exists(FileContainer.GetFullPathFileName(skinFileName)))
            {
                Log.WriteLine($"Failed to load skin file '{skinFileName}'");
                return;
            }

            m_SkinFile.TryChangeFileName(skinFileName);

            try
            {
                ResourceDictionary resourceDictionary = null;

                using (var streamReader = new StreamReader(m_SkinFile.FullPathFileName, Encoding.UTF8))
                {
                    LoadExternalAssemblies();
                    var xmlReaderSettings = new XamlXmlReaderSettings
                    {
                        LocalAssembly = Assembly.GetExecutingAssembly()
                    };

                    using (var xamlReader = new XamlXmlReader(streamReader.BaseStream, XamlReader.GetWpfSchemaContext(), xmlReaderSettings))
                    {
                        resourceDictionary = XamlReader.Load(xamlReader) as ResourceDictionary;
                    }
                }

                if (resourceDictionary != null)
                {
                    Resources.MergedDictionaries.Clear();
                    Resources.MergedDictionaries.Add(resourceDictionary);

                    if (m_Overlay != null)
                    {
                        m_Overlay.RefreshWidgetsLayout();
                    }

                    Log.WriteLine($"{m_SkinFile.FileName} loaded");
                }
            }
            catch (Exception ex)
            {
                Log.WriteException(ex);
            }

            m_LastSkinFileName = skinFileName;
        }

        void LoadExternalAssemblies()
        {
            // External dependencies should be loaded into AppDomain before they can be used in dynamically loaded XAML (Default.xaml)
            Assembly.Load(typeof(OxyPlot.Wpf.Plot).Assembly.GetName());
            Assembly.Load(typeof(Microsoft.Xaml.Behaviors.Behavior).Assembly.GetName());
        }

        void SetPerMonitorDpiAwareness()
        {
            // Win 8.1 added support for per monitor dpi
            if (Environment.OSVersion.Version >= new Version(6, 3, 0))
            {
                // Win 10 creators update added support for per monitor v2
                if (Environment.OSVersion.Version >= new Version(10, 0, 15063))
                {
                    WindowsApi.SetProcessDpiAwarenessContext((int)WindowsApi.DPI_AWARENESS_CONTEXT.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
                }
                else
                {
                    WindowsApi.SetProcessDpiAwareness(WindowsApi.PROCESS_DPI_AWARENESS.PROCESS_PER_MONITOR_DPI_AWARE);
                }
            }
            else
            {
                WindowsApi.SetProcessDPIAware();
            }
        }
    }
}
