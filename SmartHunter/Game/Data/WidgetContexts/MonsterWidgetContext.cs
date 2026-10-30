using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using SmartHunter.Core.Data;
using SmartHunter.Game.Helpers;
using SmartHunter.Game.Config;
using System;

namespace SmartHunter.Game.Data.WidgetContexts
{
    public class MonsterWidgetContext : WidgetContext
    {
        protected override SmartHunter.Core.Config.WidgetConfig FontConfig => ConfigHelper.Main.Values.Overlay.MonsterWidget;
        public ObservableCollection<Monster> Monsters { get; private set; }
        int m_Columns = 1;
        double m_AvailableWidth = 1100, m_AvailableHeight = 600, m_Scale = 1;
        public bool IsHorizontal => ConfigHelper.Main.Values.Overlay.MonsterWidget.Layout == MonsterLayout.Horizontal;
        public bool HideScrollBars => SmartHunter.World.GameProfile.Active == SmartHunter.World.GameChoice.Wilds;
        public bool IsSingleRow => IsHorizontal && !ConfigHelper.Main.Values.Overlay.MonsterWidget.WrapHorizontal;
        public bool IsWrapping => ConfigHelper.Main.Values.Overlay.MonsterWidget.Layout == MonsterLayout.Grid || IsHorizontal && !IsSingleRow;
        public bool SelectedDetailsOnly => ConfigHelper.Main.Values.Overlay.MonsterWidget.SelectedDetailsOnly;
        public bool HasSelectedMonster => Monsters.Any(m => m.IsSelected);
        readonly HashSet<Monster> observedMonsters = new HashSet<Monster>();
        public double CardWidth => Math.Max(180, Math.Min(300, ConfigHelper.Main.Values.Overlay.MonsterWidget.CardWidth));
        public double CardStride => CardWidth + 12;
        // Columns affect presentation only. Every valid actor remains tracked.
        public bool CenterMonsters => ConfigHelper.Main.Values.Overlay.MonsterWidget.CenterMonsters;
        public double LayoutWidth
        {
            get
            {
                int count = IsSingleRow ? Monsters.Count : Math.Min(m_Columns, Monsters.Count);
                if(CenterMonsters && IsHorizontal) count = Math.Max(count,Math.Min(3, Math.Max(1,(int)(ViewportWidth/CardStride))));
                return Math.Max(1,count)*CardStride;
            }
        }
        public bool HasCustomWidth => ConfigHelper.Main.Values.Overlay.MonsterWidget.WindowWidth > 0;
        public double WindowWidth => !HasCustomWidth ? Double.NaN : Math.Max((CardStride+System.Windows.SystemParameters.VerticalScrollBarWidth)*m_Scale+50,
            Math.Min(Math.Max(400,Math.Min(7680,ConfigHelper.Main.Values.Overlay.MonsterWidget.WindowWidth)),m_AvailableWidth+50));
        public System.Windows.SizeToContent WindowSizing => HasCustomWidth ? System.Windows.SizeToContent.Height : System.Windows.SizeToContent.WidthAndHeight;
        public double ViewportWidth => Math.Max(CardStride + System.Windows.SystemParameters.VerticalScrollBarWidth, (HasCustomWidth ? WindowWidth-50 : m_AvailableWidth) / m_Scale);
        public double ViewportHeight => Math.Max(120, m_AvailableHeight / m_Scale);
        public void UpdateLayoutBounds(double availableWidth, double availableHeight, double scale)
        {
            m_AvailableWidth = availableWidth; m_AvailableHeight = availableHeight; m_Scale = Math.Max(0.5, scale);
            UpdateLayout();
        }
        void UpdateLayout()
        {
            var config = ConfigHelper.Main.Values.Overlay.MonsterWidget;
            int capacity = Math.Max(1, (int)((ViewportWidth - System.Windows.SystemParameters.VerticalScrollBarWidth) / CardStride));
            m_Columns = config.Layout == MonsterLayout.Grid ? Math.Min(capacity, Math.Max(1, Math.Min(12, config.Columns))) : IsWrapping ? capacity : 1;
            NotifyPropertyChanged(nameof(LayoutWidth)); NotifyPropertyChanged(nameof(ViewportWidth)); NotifyPropertyChanged(nameof(ViewportHeight));
            NotifyPropertyChanged(nameof(CenterMonsters));
            NotifyPropertyChanged(nameof(HasCustomWidth)); NotifyPropertyChanged(nameof(WindowWidth)); NotifyPropertyChanged(nameof(WindowSizing));
            NotifyPropertyChanged(nameof(CardWidth)); NotifyPropertyChanged(nameof(CardStride));
            NotifyPropertyChanged(nameof(HideScrollBars));
            NotifyPropertyChanged(nameof(IsSingleRow)); NotifyPropertyChanged(nameof(IsWrapping)); NotifyPropertyChanged(nameof(SelectedDetailsOnly));
        }

        bool m_ShowSize = false;
        public bool ShowSize
        {
            get { return m_ShowSize; }
            set { SetProperty(ref m_ShowSize, value); }
        }

        bool m_ShowCrown = true;
        public bool ShowCrown
        {
            get { return m_ShowCrown; }
            set { SetProperty(ref m_ShowCrown, value); }
        }

        bool m_ShowBars = true;
        public bool ShowBars
        {
            get { return m_ShowBars; }
            set { SetProperty(ref m_ShowBars, value); }
        }

        bool m_ShowNumbers = true;
        public bool ShowNumbers
        {
            get { return m_ShowNumbers; }
            set { SetProperty(ref m_ShowNumbers, value); }
        }

        bool m_ShowPercents = true;
        public bool ShowPercents
        {
            get { return m_ShowPercents; }
            set { SetProperty(ref m_ShowPercents, value); }
        }

        bool m_UseAnimations = true;
        public bool UseAnimations
        {
            get { return m_UseAnimations; }
            set { SetProperty(ref m_UseAnimations, value); }
        }

        bool m_AlwaysShowParts = false;
        public bool AlwaysShowParts
        {
            get { return m_AlwaysShowParts; }
            set { SetProperty(ref m_AlwaysShowParts, value); }
        }

        public MonsterWidgetContext()
        {
            Monsters = new ObservableCollection<Monster>();
            Monsters.CollectionChanged += MonstersChanged;

            UpdateFromConfig();
        }

        void MonstersChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                foreach (var monster in observedMonsters) monster.PropertyChanged -= SelectionChanged;
                observedMonsters.Clear();
            }
            if (e.OldItems != null) foreach (Monster monster in e.OldItems)
                if (observedMonsters.Remove(monster)) monster.PropertyChanged -= SelectionChanged;
            if (e.NewItems != null) foreach (Monster monster in e.NewItems)
                if (observedMonsters.Add(monster)) monster.PropertyChanged += SelectionChanged;
            NotifyPropertyChanged(nameof(LayoutWidth)); NotifyPropertyChanged(nameof(HasSelectedMonster));
        }
        void SelectionChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Monster.IsSelected)) NotifyPropertyChanged(nameof(HasSelectedMonster));
        }

        public Monster UpdateAndGetMonster(ulong address, string id, float maxHealth, float currentHealth, float sizeScale, float scaleModifier)
        {
            Monster monster = null;

            monster = Monsters.FirstOrDefault(existingMonster => existingMonster.Address == address);
            if (monster != null)
            {
                monster.Id = id;
                monster.Health.Max = maxHealth;
                monster.Health.Current = currentHealth;
                monster.SizeScale = sizeScale;
                monster.ScaleModifier = scaleModifier;
            }
            else
            {
                monster = new Monster(address, id, maxHealth, currentHealth, sizeScale, scaleModifier);
                Monsters.Add(monster);
            }

            monster.NotifyPropertyChanged(nameof(Monster.IsVisible));

            return monster;
        }

        public override void UpdateFromConfig()
        {
            base.UpdateFromConfig();

            ShowSize = ConfigHelper.Main.Values.Overlay.MonsterWidget.ShowSize;
            ShowCrown = ConfigHelper.Main.Values.Overlay.MonsterWidget.ShowCrown;
            ShowBars = ConfigHelper.Main.Values.Overlay.MonsterWidget.ShowBars;
            ShowNumbers = ConfigHelper.Main.Values.Overlay.MonsterWidget.ShowNumbers;
            ShowPercents = ConfigHelper.Main.Values.Overlay.MonsterWidget.ShowPercents;
            UseAnimations = ConfigHelper.Main.Values.Overlay.MonsterWidget.UseAnimations;
            AlwaysShowParts = ConfigHelper.Main.Values.Overlay.MonsterWidget.AlwaysShowParts;
            UpdateLayout();

            foreach (var monster in Monsters)
            {
                monster.NotifyPropertyChanged(nameof(Monster.IsVisible));
            }
        }
    }
}
