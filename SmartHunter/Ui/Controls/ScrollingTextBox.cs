using System;
using System.Windows.Controls;
using System.Windows;

namespace SmartHunter.Ui.Controls
{
    /// <summary>
    /// This textbox will scroll to end when text is changed
    /// </summary>
    public class ScrollingTextBox : TextBox {
        public static readonly DependencyProperty AutoScrollProperty = DependencyProperty.Register(
            nameof(AutoScroll), typeof(bool), typeof(ScrollingTextBox), new PropertyMetadata(true));
        public bool AutoScroll { get => (bool)GetValue(AutoScrollProperty); set => SetValue(AutoScrollProperty, value); }

        protected override void OnInitialized(EventArgs e)
        {
            base.OnInitialized(e);
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        }

        protected override void OnTextChanged(TextChangedEventArgs e)
        {
            base.OnTextChanged(e);
            if (AutoScroll) { CaretIndex = Text.Length; ScrollToEnd(); }
        }
    }
}
