using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Threading;
using Microsoft.Xaml.Behaviors;
using OxyPlot;
using OxyPlot.Wpf;
using SmartHunter.Game.Data;
using SmartHunter.Game.Data.ViewModels;

namespace SmartHunter.Ui.Behaviors
{
    /// <summary>
    /// OxyPlot doesn't support WPF bindings well, so it should be updated from code.
    /// This behavior exists so chart can be customizable by skin and fully detached from code-behind.
    ///
    /// Using DataPoints instead of DamagePoint for performance.
    /// </summary>
    public class UpdateOxyPlotBehavior : Behavior<Plot>
    {
        static List<DamagePoint> Empty = new List<DamagePoint>();
        private DispatcherTimer timer;
        public int TickInterval { get; set; }

        public UpdateOxyPlotBehavior()
        {
            timer = new DispatcherTimer();
            timer.Tick += TimerOnTick;
        }

        protected override void OnAttached()
        {
            base.OnAttached();
            if (LicenseManager.UsageMode == LicenseUsageMode.Designtime)
            {
                return;
            }

            OverlayViewModel.Instance.TeamWidget.Context.PlayersDamageUpdated += ContextOnPlayersDamageUpdated;
            SetSeriesDataPointsMapping();

            if (TickInterval > 0)
            {
                timer.Interval = TimeSpan.FromMilliseconds(TickInterval);
                timer.Start();
            }
            AssociatedObject.ActualController.UnbindAll();
        }

        protected override void OnDetaching()
        {
            base.OnDetaching();
            timer.Stop();
            OverlayViewModel.Instance.TeamWidget.Context.PlayersDamageUpdated -= ContextOnPlayersDamageUpdated;
        }

        private void SetSeriesDataPointsMapping()
        {
            foreach (var series in AssociatedObject.Series)
            {
                if (series is DataPointSeries pointSeries)
                {
                    pointSeries.Mapping = MapPoint;
                }
            }
        }

        private void ContextOnPlayersDamageUpdated(object sender, EventArgs e)
        {
            // Timed plots already redraw on their timer; don't lay out the whole
            // plot again on every 20-60 Hz memory update.
            if (TickInterval <= 0) UpdatePlot();
        }

        private void TimerOnTick(object sender, EventArgs e)
        {
            if (!OverlayViewModel.Instance.TeamWidget.Context.ShowChart) return;

            OverlayViewModel.Instance.TeamWidget.Context.PadDamagePoints(TickInterval);
            UpdatePlot();
        }

        private void UpdatePlot()
        {
            if (!OverlayViewModel.Instance.TeamWidget.Context.ShowChart) return;

            var plot = AssociatedObject;
            var players = OverlayViewModel.Instance.TeamWidget.Context.Players;

            for (var i = 0; i < plot.Series.Count; i++)
            {
                var series = plot.Series[i];

                var player = players.FirstOrDefault(p => p.Index == i);
                if (player == null)
                {
                    series.ItemsSource = Empty;
                }
                else
                {
                    series.ItemsSource = player.DamagePoints;
                }
            }
            plot.UpdateLayout();
        }

        private static DataPoint MapPoint(object point)
        {
            var damagePoint = (DamagePoint)point;
            return new DataPoint(damagePoint.TimeStamp, damagePoint.Damage);
        }
    }
}
