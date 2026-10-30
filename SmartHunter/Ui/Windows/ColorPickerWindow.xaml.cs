using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SmartHunter.Core.Data;
using SmartHunter.Game.Helpers;
using SmartHunter.Ui.Converters;
namespace SmartHunter.Ui.Windows
{
    public sealed class ColorDraft : Bindable
    {
        Color color;
        string hex;
        public string Hex {get=>hex;set {hex=value;if(OverlayColors.TryParse(value,out var parsed))color=parsed;Refresh();}}
        public bool Valid=>OverlayColors.TryParse(hex,out var parsed);
        public string Error=>Valid?"":UiText.Instance["LOC_UI_COLOR_INVALID"];
        public Brush Preview=>OverlayColors.Brush(color);
        public double Opacity {get=>color.A*100d/255;set=>Channel(0,value*255/100);}
        public double A {get=>color.A;set=>Channel(0,value);}public double R {get=>color.R;set=>Channel(1,value);}public double G {get=>color.G;set=>Channel(2,value);}public double B {get=>color.B;set=>Channel(3,value);}
        public ColorDraft(string value,string fallback){Hex=OverlayColors.TryParse(value,out var parsed)?value:fallback;}
        void Channel(int channel,double value){byte v=(byte)Math.Round(Math.Max(0,Math.Min(255,value)));color=Color.FromArgb(channel==0?v:color.A,channel==1?v:color.R,channel==2?v:color.G,channel==3?v:color.B);hex=OverlayColors.Canonical(color);Refresh();}
        void Refresh(){foreach(var name in new[]{"Hex","A","R","G","B","Preview","Valid","Error","Opacity"})NotifyPropertyChanged(name);}
    }
    public partial class ColorPickerWindow : Window
    {
        readonly ColorDraft draft;
        public string Result {get;private set;}
        public ColorPickerWindow(string value,string fallback){InitializeComponent();DataContext=draft=new ColorDraft(value,fallback);}
        void Preset_Click(object sender,RoutedEventArgs e)=>draft.Hex=(string)((Button)sender).Tag;
        void Apply_Click(object sender,RoutedEventArgs e){if(!draft.Valid)return;OverlayColors.TryParse(draft.Hex,out var color);Result=OverlayColors.Canonical(color);DialogResult=true;}
        void Default_Click(object sender,RoutedEventArgs e){Result="";DialogResult=true;}
    }
}
