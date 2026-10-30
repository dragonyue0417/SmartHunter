using System;
using System.Collections.ObjectModel;
using System.Linq;
using SmartHunter.Core.Data;
using SmartHunter.Game.Data.ViewModels;
using SmartHunter.Game.Helpers;

namespace SmartHunter.Game.Data.WidgetContexts
{
    public class TeamWidgetContext : WidgetContext
    {
        protected override SmartHunter.Core.Config.WidgetConfig FontConfig => ConfigHelper.Main.Values.Overlay.TeamWidget;
        public Collection<Player> Players { get; private set; }
        public ObservableCollection<Player> Fake_Players { get; private set; } // TODO: Can dis be done inside the xaml file?

        bool m_DontShowIfAlone = false;
        public bool DontShowIfAlone
        {
            get { return m_DontShowIfAlone; }
            set { if (SetProperty(ref m_DontShowIfAlone, value) && Players != null) RefreshVisiblePlayers(); }
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

        bool m_ShowPercents = false;
        public bool ShowPercents
        {
            get { return m_ShowPercents; }
            set { SetProperty(ref m_ShowPercents, value); }
        }

        bool m_ShowChart = false;
        public bool ShowChart
        {
            get { return m_ShowChart; }
            set { SetProperty(ref m_ShowChart, value); }
        }

        public TeamWidgetContext()
        {
            Players = new Collection<Player>();
            Fake_Players = new ObservableCollection<Player>();

            UpdateFromConfig();
        }

        public event EventHandler PlayersDamageUpdated;

        public Player UpdateAndGetPlayer(int index, string name, int damage)
        {
            // A lobby slot is stable; collection positions change when someone leaves.
            Player player = Players.FirstOrDefault(p => p.Index == index);
            if (String.IsNullOrEmpty(name) && damage == 0)
            {
                if (player != null)
                {
                    Players.Remove(player);
                    RefreshVisiblePlayers();
                }
                return null;
            }
            if (player != null && !String.IsNullOrEmpty(name) && player.Name != name)
            {
                Players.Remove(player);
                player = null; // a new player occupying this slot needs a fresh chart
            }
            if (player == null)
            {
                player = new Player { Index = index, Name = String.IsNullOrEmpty(name)
                    ? LocalizationHelper.GetString(LocalizationHelper.UnknownPlayerStringId) : name };
                int position = 0;
                while (position < Players.Count && Players[position].Index < index) position++;
                Players.Insert(position, player);
                RefreshVisiblePlayers();
            }
            else if (!String.IsNullOrEmpty(name)) player.Name = name;
            player.Damage = damage;
            return player;
        }

        void RefreshVisiblePlayers()
        {
            int expected = DontShowIfAlone && Players.Count <= 1 ? 0 : Players.Count;
            bool unchanged = Fake_Players.Count == expected;
            for (int i = 0; unchanged && i < expected; i++)
                unchanged = ReferenceEquals(Fake_Players[i], Players[i]);
            if (unchanged) return;
            Fake_Players.Clear();
            for (int i = 0; i < expected; i++) Fake_Players.Add(Players[i]);
        }

        public void UpdateFractions()
        {
            long totalDamage = 0;
            int highestDamage = 0;
            foreach (var player in Players)
            {
                totalDamage += Math.Max(0, player.Damage);
                highestDamage = Math.Max(highestDamage, player.Damage);
            }
            foreach (var player in Players)
            {
                player.DamageFraction = totalDamage > 0 ? (float)((double)Math.Max(0, player.Damage) / totalDamage) : 0;
                player.BarFraction = highestDamage > 0 ? (float)Math.Max(0, player.Damage) / highestDamage : 0;
            }
            if (ShowChart) NormalizeDamagePoints();
            PlayersDamageUpdated?.Invoke(this, EventArgs.Empty);
        }

        public void ClearPlayers()
        {
            Players.Clear();
            Fake_Players.Clear();
            PlayersDamageUpdated?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Make sure that all players have datapoints at latest available timestamp
        /// </summary>
        private void NormalizeDamagePoints()
        {
            var maxTimeStamp = Players.Max(p => p.DamagePoints.LastOrDefault()?.TimeStamp);
            if (maxTimeStamp == null) return;

            foreach (var player in Players)
            {
                var last = player.DamagePoints.LastOrDefault();
                if (last == null)
                {
                    player.DamagePoints.Add(new DamagePoint(maxTimeStamp.Value, 0));
                }
                else if (last.TimeStamp < maxTimeStamp)
                {
                    player.DamagePoints.Add(new DamagePoint(maxTimeStamp.Value, last.Damage));
                }
            }
        }

        /// <summary>
        /// Adds datapoints for current timestamp so plot will continue even without new damage
        /// </summary>
        /// <param name="msThreshold">add point only if latest point older then this value</param>
        public void PadDamagePoints(long msThreshold = 0)
        {
            var maxTimeStamp = Players.Max(p => p.DamagePoints.LastOrDefault()?.TimeStamp);
            if (maxTimeStamp == null)
            {
                // no datapoints present, don't have to update anything
                return;
            }

            var now = DateTime.Now.ToFileTime();
            if (msThreshold > 0 && now - maxTimeStamp <= msThreshold * TimeSpan.TicksPerMillisecond)
            {
                return;
            }

            foreach (var player in Players)
            {
                var dmgPoints = player.DamagePoints;
                var last = dmgPoints.LastOrDefault();
                var beforeLast = dmgPoints.Count > 1
                    ? dmgPoints[dmgPoints.Count - 2]
                    : null;

                if (last == null)
                {
                    // no point present, adding first one at 0 damage
                    dmgPoints.Add(new DamagePoint(now, 0));
                }
                else if (beforeLast?.Damage == last.Damage)
                {
                    // if last two points identical, we can just update last one
                    // Removing and adding again to trigger collection update
                    dmgPoints.RemoveAt(dmgPoints.Count - 1);
                    last.TimeStamp = now;
                    dmgPoints.Add(last);
                }
                else
                {
                    dmgPoints.Add(new DamagePoint(now, last.Damage));
                }
            }
        }

        public override void UpdateFromConfig()
        {
            base.UpdateFromConfig();

            DontShowIfAlone = ConfigHelper.Main.Values.Overlay.TeamWidget.DontShowIfAlone;
            ShowBars = ConfigHelper.Main.Values.Overlay.TeamWidget.ShowBars;
            ShowNumbers = ConfigHelper.Main.Values.Overlay.TeamWidget.ShowNumbers;
            ShowPercents = ConfigHelper.Main.Values.Overlay.TeamWidget.ShowPercents;
            ShowChart = ConfigHelper.Main.Values.Overlay.TeamWidget.ShowChart;
        }
    }
}
