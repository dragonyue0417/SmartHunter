using System.Windows;
using SmartHunter.Game.Data.ViewModels;

namespace SmartHunter.Ui.Windows
{
    public partial class ConsoleWindow : Window
    {
        public ConsoleWindow()
        {
            InitializeComponent();
            Navigation.SelectionChanged += (s, e) =>
            {
                SettingsScroll.ScrollToTop();
                if(Navigation.SelectedItem!=null)Navigation.ScrollIntoView(Navigation.SelectedItem);
            };
            Loaded += (s,e) => {if(Navigation.SelectedItem!=null)Navigation.ScrollIntoView(Navigation.SelectedItem);};
        }
        void PickColor_Click(object sender,RoutedEventArgs e)
        {
            var setting=(SmartHunter.Core.Setting)((FrameworkElement)sender).DataContext;
            var picker=new ColorPickerWindow(setting.Text,setting.ColorDefault()) {Owner=this};
            if(picker.ShowDialog()==true) setting.Text=picker.Result;
        }
        void ClearSearch_Click(object sender, RoutedEventArgs e) => SettingsViewModel.Instance.Search = "";
        void ClearLogs_Click(object sender, RoutedEventArgs e) => ConsoleViewModel.Instance.Clear();
        void CopyLogs_Click(object sender, RoutedEventArgs e)
        {
            SmartHunter.Core.ClipboardService.Copy(ConsoleViewModel.Instance.Output);
        }
    }
}
