using SmartHunter.Core.Data;
using SmartHunter.Game.Data.WidgetContexts;
using SmartHunter.Game.Helpers;

namespace SmartHunter.Game.Data.ViewModels
{
    public class OverlayViewModel : Bindable
    {
        static OverlayViewModel s_Instance = null;
        public static OverlayViewModel Instance
        {
            get
            {
                if (s_Instance == null)
                {
                    s_Instance = new OverlayViewModel();

                }

                return s_Instance;
            }
        }

        public ContextualWidget<TeamWidgetContext> TeamWidget { get; private set; }
        public ContextualWidget<MonsterWidgetContext> MonsterWidget { get; private set; }
        public ContextualWidget<PlayerWidgetContext> PlayerWidget { get; private set; }
        public ContextualWidget<DebugWidgetContext> DebugWidget { get; private set; }
        bool m_IsPreview;
        public bool IsPreview { get => m_IsPreview; set { if (SetProperty(ref m_IsPreview, value)) NotifyPropertyChanged(nameof(IsDebugVisible)); } }
        public bool IsDebugVisible => !HideWidgetsRequested && (IsPreview || DebugWidget.Context.AllowVisibility);

        bool m_CanManipulateWindows;
        public bool CanManipulateWindows
        {
            get { return m_CanManipulateWindows; }
            set { SetProperty(ref m_CanManipulateWindows, value); }
        }

        bool m_HideWidgetsRequested;
        public bool HideWidgetsRequested
        {
            get { return m_HideWidgetsRequested; }
            set
            {
                if (SetProperty(ref m_HideWidgetsRequested, value))
                {
                    NotifyPropertyChanged(nameof(IsVisible));
                    NotifyPropertyChanged(nameof(IsDebugVisible));
                }
            }
        }

        bool m_IsGameActive = false;
        public bool IsGameActive
        {
            get { return m_IsGameActive; }
            set
            {
                if (SetProperty(ref m_IsGameActive, value))
                {
                    NotifyPropertyChanged(nameof(IsVisible));
                }
            }
        }

        public bool IsVisible
        {
            get
            {
                return IsGameActive && !HideWidgetsRequested;
            }
        }

        public OverlayViewModel()
        {
            TeamWidget = new ContextualWidget<TeamWidgetContext>(ConfigHelper.Main.Values.Overlay.TeamWidget, new TeamWidgetContext());
            MonsterWidget = new ContextualWidget<MonsterWidgetContext>(ConfigHelper.Main.Values.Overlay.MonsterWidget, new MonsterWidgetContext());
            PlayerWidget = new ContextualWidget<PlayerWidgetContext>(ConfigHelper.Main.Values.Overlay.PlayerWidget, new PlayerWidgetContext());
            DebugWidget = new ContextualWidget<DebugWidgetContext>(ConfigHelper.Main.Values.Overlay.DebugWidget, new DebugWidgetContext());
            DebugWidget.Context.PropertyChanged += (s, e) => { if (e.PropertyName == "AllowVisibility") NotifyPropertyChanged(nameof(IsDebugVisible)); };
            ConfigHelper.Localization.Loaded += (s, e) => RefreshLocalization();
        }

        internal void RefreshLocalization()
        {
            foreach (var monster in MonsterWidget.Context.Monsters) monster.UpdateLocalization();
            foreach (var effect in PlayerWidget.Context.StatusEffects)
                effect.NotifyPropertyChanged(nameof(PlayerStatusEffect.Name));
        }

    }
}
