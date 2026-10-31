using SmartHunter.Core.Windows;
using SmartHunter.Game.Data.ViewModels;
using SmartHunter.Game.Helpers;
using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Media;

namespace SmartHunter.Ui.Windows
{
    public partial class MonsterWidgetWindow : WidgetWindow
    {
        protected override float ScaleMax { get { return ConfigHelper.Main.Values.Overlay.ScaleMax; } }
        protected override float ScaleMin { get { return ConfigHelper.Main.Values.Overlay.ScaleMin; } }
        protected override float ScaleStep { get { return ConfigHelper.Main.Values.Overlay.ScaleStep; } }

        public MonsterWidgetWindow() : base(OverlayViewModel.Instance.MonsterWidget)
        {
            InitializeComponent();

            DataContext = OverlayViewModel.Instance;
            Widget.PropertyChanged += WidgetChanged;
            OverlayViewModel.Instance.MonsterWidget.Context.PropertyChanged += ContextChanged;
            MonsterScroll.LayoutUpdated += UpdateViewport;
            Closed += (s, e) =>
            {
                Widget.PropertyChanged -= WidgetChanged;
                OverlayViewModel.Instance.MonsterWidget.Context.PropertyChanged -= ContextChanged;
                MonsterScroll.LayoutUpdated -= UpdateViewport;
            };
            SourceInitialized += (s,e)=>UpdateBounds();
            Loaded += (s,e)=>UpdateBounds();
            LocationChanged += (s,e)=>UpdateBounds();
            UpdateBounds();
        }
        void ContextChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "WindowWidth") ApplyWindowSizing();
            if (e.PropertyName == "LayoutWidth" || e.PropertyName == "ViewportHeight") UpdateViewport(sender, EventArgs.Empty);
        }
        void ApplyWindowSizing()
        {
            // WPF updates Window.Width itself after native window size changes,
            // replacing its binding. Apply the current profile on context updates.
            var context = OverlayViewModel.Instance.MonsterWidget.Context;
            if (SizeToContent != context.WindowSizing) SizeToContent = context.WindowSizing;
            double width = context.WindowWidth;
            if (Double.IsNaN(width) ? !Double.IsNaN(Width) : Double.IsNaN(Width) || Math.Abs(Width-width) > 0.5)
                Width = width;
        }
        static WrapPanel FindPanel(DependencyObject parent)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is WrapPanel panel && panel.IsItemsHost) return panel;
                var nested = FindPanel(child); if (nested != null) return nested;
            }
            return null;
        }
        void UpdateViewport(object sender, EventArgs e)
        {
            double height = OverlayViewModel.Instance.MonsterWidget.Context.ViewportHeight;
            var config = ConfigHelper.Main.Values.Overlay.MonsterWidget;
            if (OverlayViewModel.Instance.MonsterWidget.Context.IsWrapping)
            {
                var panel = FindPanel(MonsterScroll);
                if (panel != null)
                {
                    int row = 0, rows = Math.Max(1, Math.Min(12, config.Rows));
                    double top = double.NaN, bottom = 0;
                    foreach (UIElement child in panel.Children)
                    {
                        if (child.RenderSize.Height <= 0) continue;
                        var bounds = child.TransformToAncestor(panel).TransformBounds(new Rect(child.RenderSize));
                        if (row == 0 || Math.Abs(bounds.Top - top) > 0.5) { row++; top = bounds.Top; }
                        if (row > rows) break;
                        bottom = Math.Max(bottom, bounds.Bottom);
                    }
                    if (bottom > 0) height = Math.Min(height, bottom);
                }
            }
            if (Double.IsInfinity(MonsterScroll.MaxHeight) || Math.Abs(MonsterScroll.MaxHeight - height) > 0.5)
                MonsterScroll.MaxHeight = height;
        }
        void WidgetChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "X" || e.PropertyName == "Y" || e.PropertyName == "Scale") UpdateBounds();
        }
        void UpdateBounds()
        {
            var remaining=SmartHunter.Ui.Behaviors.WidgetWorkArea.Remaining(this,Widget.X,Widget.Y);
            OverlayViewModel.Instance.MonsterWidget.Context.UpdateLayoutBounds(remaining.Width,remaining.Height, Widget.Scale);
        }
        void MonsterWindow_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (!OverlayViewModel.Instance.CanManipulateWindows) return;
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
            {
                if (OverlayViewModel.Instance.MonsterWidget.Context.IsSingleRow)
                    MonsterScroll.ScrollToHorizontalOffset(Math.Max(0, MonsterScroll.HorizontalOffset - e.Delta));
                else MonsterScroll.ScrollToVerticalOffset(Math.Max(0, MonsterScroll.VerticalOffset - e.Delta));
            }
            else WidgetWindow_MouseWheel(sender, e);
            e.Handled = true;
        }
    }
}
