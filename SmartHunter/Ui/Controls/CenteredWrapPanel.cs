using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace SmartHunter.Ui.Controls
{
    public sealed class CenteredWrapPanel : WrapPanel
    {
        public static readonly DependencyProperty CenterRowsProperty = DependencyProperty.Register(nameof(CenterRows), typeof(bool), typeof(CenteredWrapPanel),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsArrange));
        public bool CenterRows { get => (bool)GetValue(CenterRowsProperty); set => SetValue(CenterRowsProperty, value); }
        protected override Size ArrangeOverride(Size size)
        {
            var result = base.ArrangeOverride(size);
            if (!CenterRows || Orientation != Orientation.Horizontal) return result;
            var row = new List<FrameworkElement>(); double top = Double.NaN;
            Action center = () =>
            {
                if (row.Count == 0) return;
                var first = LayoutInformation.GetLayoutSlot(row[0]);
                var last = LayoutInformation.GetLayoutSlot(row[row.Count - 1]);
                double offset = Math.Max(0, (size.Width - (last.Right - first.Left)) / 2) - first.Left;
                foreach (var child in row) { var rectangle = LayoutInformation.GetLayoutSlot(child); rectangle.Offset(offset, 0); child.Arrange(rectangle); }
                row.Clear();
            };
            foreach (UIElement element in InternalChildren)
            {
                if (!(element is FrameworkElement child) || child.Visibility == Visibility.Collapsed) continue;
                var slot = LayoutInformation.GetLayoutSlot(child);
                if (row.Count > 0 && Math.Abs(slot.Top - top) > 0.5) center();
                top = slot.Top; row.Add(child);
            }
            center(); return result;
        }
    }
}
