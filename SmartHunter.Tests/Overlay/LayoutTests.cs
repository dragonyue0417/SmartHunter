using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SmartHunter.Config;
using SmartHunter.Core;
using SmartHunter.Core.Config;
using SmartHunter.Game.Config;
using SmartHunter.Game.Data;
using SmartHunter.Game.Data.ViewModels;
using SmartHunter.Game.Data.WidgetContexts;
using SmartHunter.Game.Helpers;
using SmartHunter.Ui.Controls;
using SmartHunter.Ui.Windows;
using SmartHunter.Wilds;
using SmartHunter.World;
using static TestSupport;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

static class AdaptiveLayoutTests
{
internal static void Wrap() => WithWindow((window, root, context, apply) =>
    {
        var config = ConfigHelper.Main.Values.Overlay.MonsterWidget;
        config.WrapHorizontal = true; config.SelectedDetailsOnly = false; config.Rows = 2; context.UpdateFromConfig();
        Assert(new MonsterWidgetConfig(0, 0).WrapHorizontal, "automatic wrapping defaults on for existing and fresh settings");
        var scroll = (ScrollViewer)window.FindName("MonsterScroll");
        foreach (int count in new[] { 6, 7, 12 })
        {
            apply.Apply(WildsSnapshotApplier.Preview(count)); OverlayViewModel.Instance.IsGameActive = true;
            context.UpdateLayoutBounds(1420, 650, 1); Render(root, "wilds-wrap-" + count + ".png", 1500, 720);
            var panel = Visuals<WrapPanel>(root).Single(p => p.IsItemsHost);
            Assert(panel.Children.Count == count && context.LayoutWidth == 1392, "six columns use available space and retain every actor");
            Assert(scroll.ScrollableWidth < 1 && scroll.ComputedHorizontalScrollBarVisibility == Visibility.Collapsed, "wrapping has no horizontal scrollbar");
            if (count >= 7) Assert(((FrameworkElement)panel.Children[6]).TransformToAncestor(panel).Transform(new Point()).Y > 0, "the seventh actor starts a new row");
            Assert(scroll.ScrollableHeight < 1, "two complete rows fit without vertical scrolling");
        }
        apply.Apply(WildsSnapshotApplier.Preview(20)); OverlayViewModel.Instance.IsGameActive = true;
        context.UpdateLayoutBounds(1420, 650, 1); Render(root, "wilds-wrap-overflow.png", 1500, 720);
        Assert(scroll.ScrollableHeight > 0 && scroll.ScrollableWidth < 1, "exceeding the visible rows scrolls vertically only");
        scroll.ScrollToEnd(); Drain(); root.UpdateLayout();
        var host = Visuals<WrapPanel>(root).Single(p => p.IsItemsHost); var last = (FrameworkElement)host.Children[19];
        var bounds = last.TransformToAncestor(scroll).TransformBounds(new Rect(last.RenderSize));
        Assert(bounds.Top < scroll.ActualHeight && bounds.Bottom > 0, "the final actor remains reachable");
        context.UpdateLayoutBounds(700, 400, 2); Render(root, "wilds-wrap-zoom.png", 850, 600);
        Assert(context.LayoutWidth == 232 && host.Children.Count == 20, "narrow space and zoom reduce columns without dropping data");
        var toggle = SettingsViewModel.Instance.Settings.Single(s => s.Key == "LOC_UI_MONSTER_AUTO_WRAP"); toggle.Value = false;
        Assert(context.IsSingleRow && !config.WrapHorizontal, "the original single-row mode remains selectable"); toggle.Value = true;
    });
internal static void Focus() => WithWindow((window, root, context, apply) =>
    {
        var config = ConfigHelper.Main.Values.Overlay.MonsterWidget;
        config.WrapHorizontal = true; config.SelectedDetailsOnly = true; context.UpdateFromConfig();
        var data = WildsSnapshotApplier.Preview(7); apply.Apply(data); OverlayViewModel.Instance.IsGameActive = true;
        context.UpdateLayoutBounds(1420, 650, 1); Render(root, "wilds-focused-preview.png", 1500, 720);
        var cards = Visuals<StackPanel>(root).Where(p => p.Name == "Root" && p.DataContext is Monster).ToArray();
        Assert(cards.Length == 7 && context.HasSelectedMonster, "all monsters remain present");
        foreach (var card in cards)
        {
            bool selected = ((Monster)card.DataContext).IsSelected;
            Assert(Visuals<ItemsControl>(card).Where(c => c.Name == "PartsControl" || c.Name == "MonsterStatusControl").All(c => c.Visibility == (selected ? Visibility.Visible : Visibility.Collapsed)), "only the selected monster expands details");
            Assert(Visuals<TextBlock>(card).Where(t => t.Name == "HealthBlock").All(t => t.FontSize == 16), "health text is not shrunk");
        }
        data.Monsters[0].Selected = false; data.Monsters[6].Selected = true; apply.Apply(data); Drain(); root.UpdateLayout();
        Assert(Visuals<ItemsControl>(cards[6]).Single(c => c.Name == "PartsControl").Visibility == Visibility.Visible && Visuals<ItemsControl>(cards[0]).Single(c => c.Name == "PartsControl").Visibility == Visibility.Collapsed, "changing targets immediately swaps expanded details");
        data.Monsters[6].Selected = false; apply.Apply(data); Drain(); root.UpdateLayout();
        Assert(!context.HasSelectedMonster && cards.All(c => Visuals<ItemsControl>(c).Single(i => i.Name == "PartsControl").Visibility == Visibility.Visible), "without a target all details remain accessible");
        SettingsViewModel.Instance.Settings.Single(s => s.Key == "LOC_UI_MONSTER_SELECTED_DETAILS").Value = false;
        Assert(!config.SelectedDetailsOnly, "the detail mode can be disabled in settings");
    });
    static void WithWindow(Action<MonsterWidgetWindow, FrameworkElement, SmartHunter.Game.Data.WidgetContexts.MonsterWidgetContext, WildsSnapshotApplier> action)
    {
        var config = ConfigHelper.Main.Values.Overlay.MonsterWidget;
        var fields = new[] { "Layout", "Rows", "CardWidth", "WrapHorizontal", "SelectedDetailsOnly", "ShowParts", "ShowStatusEffects", "ShowUnchangedParts", "ShowUnchangedStatusEffects" }.Select(n => config.GetType().GetField(n)).ToArray();
        var saved = fields.Select(f => f.GetValue(config)).ToArray(); var vm = OverlayViewModel.Instance; bool preview = vm.IsPreview;
        config.Layout = MonsterLayout.Horizontal; config.CardWidth = 220; config.ShowParts = config.ShowStatusEffects = config.ShowUnchangedParts = config.ShowUnchangedStatusEffects = true;
        vm.IsPreview = false; var apply = new WildsSnapshotApplier(); var window = new MonsterWidgetWindow(); var trace = new StringWriter(); var listener = new TextWriterTraceListener(trace);
        PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
        try { action(window, (FrameworkElement)window.Content, vm.MonsterWidget.Context, apply); listener.Flush(); Assert(trace.ToString().Length == 0, "adaptive overlay binding errors: " + trace); }
        finally { PresentationTraceSources.DataBindingSource.Listeners.Remove(listener); listener.Dispose(); window.Close(); apply.Clear(); for (int i = 0; i < fields.Length; i++) fields[i].SetValue(config, saved[i]); vm.IsPreview = preview; ConfigHelper.SaveSettings(); vm.MonsterWidget.Context.UpdateFromConfig(); }
    }
    static void Render(FrameworkElement root, string name, int width, int height)
    {
        root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout(); Drain();
        var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (s, e) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame); root.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var output = File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name))) encoder.Save(output);
    }
}

static class OverlayLayoutTests
{
internal static void LayoutSettings()
    {
        var config = ConfigHelper.Main.Values.Overlay.MonsterWidget; var context = OverlayViewModel.Instance.MonsterWidget.Context;
        var oldLayout = config.Layout; int oldColumns = config.Columns, oldRows = config.Rows;
        bool oldPriority = config.PrioritizeSelectedMonster;
        int oldWidth = config.CardWidth;
        Assert(new MonsterWidgetConfig(0, 0).Layout == MonsterLayout.Horizontal, "horizontal is the default for fresh and legacy configs");
        var legacy = new MainConfig(); Newtonsoft.Json.JsonConvert.PopulateObject("{\"Overlay\":{\"MonsterWidget\":{\"ShowBars\":false}}}", legacy);
        Assert(legacy.Overlay.MonsterWidget.Layout == MonsterLayout.Horizontal && !legacy.Overlay.MonsterWidget.ShowBars, "legacy settings preserve values and get horizontal layout");
        var vm = SettingsViewModel.Instance;
        var layout = vm.Settings.Single(s => s.Key == "LOC_UI_MONSTER_LAYOUT");
        var columns = vm.Settings.Single(s => s.Key == "LOC_UI_MONSTER_COLUMNS");
        var rows = vm.Settings.Single(s => s.Key == "LOC_UI_MONSTER_ROWS");
        var priority = vm.Settings.Single(s => s.Key == "LOC_UI_PRIORITIZE_SELECTED_MONSTER");
        var width = vm.Settings.Single(s => s.Key == "LOC_UI_MONSTER_CARD_WIDTH");
        try
        {
            Assert(layout.IsChoice && !layout.IsToggle && columns.Options.Count == 12 && rows.Options.Count == 12, "searchable layout and size choices available");
            layout.SelectedOption = layout.Options.Single(o => o.Id == "Grid");
            columns.SelectedOption = columns.Options.Single(o => o.Id == "4"); rows.SelectedOption = rows.Options.Single(o => o.Id == "3");
            var saved = JObject.Parse(File.ReadAllText(ConfigHelper.Main.FullPathFileName))["Overlay"]["MonsterWidget"];
            Assert((string)saved["Layout"] == "Grid" && (int)saved["Columns"] == 4 && (int)saved["Rows"] == 3, "layout, columns and rows are saved together");
            Assert(priority.IsToggle && !priority.IsChoice, "target priority has a regular toggle");
            width.SelectedOption = width.Options.Single(o => o.Id == "180");
            Assert((int)JObject.Parse(File.ReadAllText(ConfigHelper.Main.FullPathFileName))["Overlay"]["MonsterWidget"]["CardWidth"] == 180, "compact width saves from the settings picker");
            priority.Value = false;
            Assert(!(bool)JObject.Parse(File.ReadAllText(ConfigHelper.Main.FullPathFileName))["Overlay"]["MonsterWidget"]["PrioritizeSelectedMonster"], "target priority off persists");
            priority.Value = true;
            Assert((bool)JObject.Parse(File.ReadAllText(ConfigHelper.Main.FullPathFileName))["Overlay"]["MonsterWidget"]["PrioritizeSelectedMonster"], "target priority on persists");
            context.UpdateLayoutBounds(2000, 700, 1); context.UpdateFromConfig();
            vm.SelectedLanguage = vm.Languages.First(l => l.FileName == "en-US.Wilds.json");
            Assert(layout.SelectedOption.Name == "Custom grid" && columns.SelectedOption.Name == "4 columns", "options translate without losing selection");
            vm.SelectedLanguage = vm.Languages.First(l => l.FileName == "zh-TW.Wilds.json");
            Assert(layout.SelectedOption.Name == "自訂網格" && rows.SelectedOption.Name == "3 列", "Chinese labels restored");
        }
        finally { config.Layout = oldLayout; config.Columns = oldColumns; config.Rows = oldRows; config.CardWidth = oldWidth; config.PrioritizeSelectedMonster = oldPriority; ConfigHelper.SaveSettings(); context.UpdateFromConfig(); }
    }
internal static void RenderUi()
    {
        Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/SmartHunter;component/Ui/Resources/Default.xaml", UriKind.Relative) });
        var trace = new StringWriter(); var listener = new TextWriterTraceListener(trace);
        PresentationTraceSources.DataBindingSource.Listeners.Add(listener); PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
        var window = new ConsoleWindow(); var vm = SettingsViewModel.Instance;
        try
        {
            Assert(window.Title == "SmartHunter", "Wilds branding");
            vm.Search = "軟化"; Assert(vm.NoResults, "no Iceborne softening option");
            vm.Search = "分享"; Assert(!vm.NoResults && !vm.FilteredSettings.Cast<Setting>().Any(s=>s.Key=="LOC_SETTING_USE_SERVER"), "sharing search includes new features without the legacy World-only server option"); vm.Search = "";
            Render((FrameworkElement)window.Content, "wilds-settings-zh-TW.png", 1120, 740);
            vm.SelectedSection = vm.Sections.Single(s => s.Id == "Monsters");
            Render((FrameworkElement)window.Content, "wilds-monster-settings-zh-TW.png", 1120, 740);
            vm.SelectedSection = vm.Sections.Single(s => s.Id == "Logs");
            var logText = (TextBox)window.FindName("LogText");
            Assert(logText.FontSize == 16 && logText.TextWrapping == TextWrapping.Wrap && logText.HorizontalScrollBarVisibility == ScrollBarVisibility.Disabled, "readable wrapped logs");
            Render((FrameworkElement)window.Content, "wilds-logs-zh-TW.png", 1120, 740);
            vm.SelectedLanguage = vm.Languages.First(l => l.FileName == "en-US.Wilds.json"); Drain();
            Assert(((ComboBox)window.FindName("LanguagePicker")).Text.Contains("English"), "language label retained");
            vm.SelectedSection = vm.Sections.Single(s => s.Id == "General");
            Render((FrameworkElement)window.Content, "wilds-settings-en-US.png", 850, 560);
            vm.SelectedLanguage = vm.Languages.First(l => l.FileName == "zh-TW.Wilds.json");
            var applier = new WildsSnapshotApplier(); applier.Apply(WildsSnapshotApplier.Preview()); OverlayViewModel.Instance.IsGameActive = true; OverlayViewModel.Instance.IsPreview = true;
            var monsterWindow = new MonsterWidgetWindow();
            try { Render((FrameworkElement)monsterWindow.Content, "wilds-monster-preview.png", 1120, 600); }
            finally { monsterWindow.Close(); applier.Clear(); }
            Drain(); listener.Flush(); Assert(trace.ToString().Length == 0, "WPF binding errors: " + trace);
        }
        finally { PresentationTraceSources.DataBindingSource.Listeners.Remove(listener); listener.Dispose(); window.Close(); }
    }
internal static void DynamicLayout()
    {
        var vm = OverlayViewModel.Instance; var applier = new WildsSnapshotApplier(); applier.Apply(WildsSnapshotApplier.Preview(12));
        vm.IsGameActive = true; vm.IsPreview = true;
        var config = ConfigHelper.Main.Values.Overlay.MonsterWidget;
        var oldLayout = config.Layout; int oldColumns = config.Columns, oldRows = config.Rows;
        int savedCardWidth = config.CardWidth;
        config.Layout = MonsterLayout.Grid; config.Columns = 3; config.Rows = 1; config.CardWidth = 220;
        var window = new MonsterWidgetWindow(); var root = (FrameworkElement)window.Content; var context = vm.MonsterWidget.Context; context.UpdateFromConfig();
        var trace = new StringWriter(); var listener = new TextWriterTraceListener(trace); PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
        try
        {
            context.UpdateLayoutBounds(1100, 700, 1);
            Render(root, "wilds-dynamic-preview.png", 1120, 600);
            var items = Descendants<ItemsControl>(root).Single(c => c.Name == "MonstersControl");
            var panel = Descendants<WrapPanel>(items).Single(p => p.IsItemsHost);
            Assert(items.Items.Count == 12 && panel.Children.Count == 12, "all twelve actors have rendered cards");
            var cards = Descendants<StackPanel>(panel).Where(p => p.Name == "Root" && p.DataContext is SmartHunter.Game.Data.Monster).ToArray();
            Assert(cards.Length == 12 && cards.All(p => p.Visibility == Visibility.Visible && p.Opacity > 0.99 && p.ActualHeight > 0), "all cards finish appearing and contain visible content");
            Assert(context.LayoutWidth == 696 && panel.ActualWidth <= 696, "three compact columns bound the layout only: context=" + context.LayoutWidth + ", panel=" + panel.ActualWidth);
            double first = ((FrameworkElement)panel.Children[0]).TransformToAncestor(panel).Transform(new Point()).Y;
            double fourth = ((FrameworkElement)panel.Children[3]).TransformToAncestor(panel).Transform(new Point()).Y;
            Assert(fourth > first, "fourth actor wraps onto another row");
            var scroll = (ScrollViewer)window.FindName("MonsterScroll");
            double oneRow = scroll.MaxHeight;
            Assert(Math.Abs(oneRow - fourth) < 1, "one configured row determines the viewport height");
            config.Rows = 2; context.UpdateFromConfig();
            Render(root, "wilds-grid-preview.png", 1120, 600);
            Assert(scroll.MaxHeight > oneRow && scroll.MaxHeight < 600, "two configured rows grow the viewport without hiding tracked actors");
            config.Rows = 12; context.UpdateFromConfig(); context.UpdateLayoutBounds(1100, 160, 1); root.UpdateLayout(); Drain();
            Assert(scroll.ScrollableHeight > 0, "many rows create a bounded scroll viewport");
            scroll.ScrollToEnd(); Drain(); root.UpdateLayout();
            Assert(scroll.VerticalOffset > 0, "can reach later rows");
            var last = (FrameworkElement)panel.Children[11];
            var bounds = last.TransformToAncestor(scroll).TransformBounds(new Rect(last.RenderSize));
            Assert(bounds.Top < scroll.ActualHeight && bounds.Bottom > 0, "last actor is reachable inside the viewport");
            Assert(!Descendants<TextBlock>(window).Any(t => t.Text.StartsWith("大型魔物：")), "monster count header removed");
            context.UpdateLayoutBounds(700, 280, 1); root.UpdateLayout(); Drain();
            Assert(context.LayoutWidth == 464 && panel.Children.Count == 12, "narrow screen reduces columns without dropping actors");
            context.UpdateLayoutBounds(700, 280, 2); root.UpdateLayout(); Drain();
            Assert(context.LayoutWidth == 232 && panel.Children.Count == 12, "zoom reduces columns without dropping actors");
            applier.Clear(); Drain(); root.UpdateLayout();
            Assert(items.Items.Count == 0 && !vm.IsVisible, "departure clears cards and hides overlay");
            listener.Flush(); Assert(trace.ToString().Length == 0, "dynamic layout binding errors: " + trace);
        }
        finally
        {
            PresentationTraceSources.DataBindingSource.Listeners.Remove(listener); listener.Dispose(); window.Close(); applier.Clear();
            config.Layout = oldLayout; config.Columns = oldColumns; config.Rows = oldRows; config.CardWidth = savedCardWidth; context.UpdateFromConfig();
        }
    }
internal static void LayoutModes()
    {
        var vm = OverlayViewModel.Instance; var config = ConfigHelper.Main.Values.Overlay.MonsterWidget;
        var oldLayout = config.Layout; bool oldWrap = config.WrapHorizontal; config.Layout = MonsterLayout.Horizontal; config.WrapHorizontal = false;
        var applier = new WildsSnapshotApplier(); var data = WildsSnapshotApplier.Preview(5); applier.Apply(data); vm.IsGameActive = true; vm.IsPreview = true;
        var window = new MonsterWidgetWindow(); var root = (FrameworkElement)window.Content; var context = vm.MonsterWidget.Context; context.UpdateFromConfig();
        try
        {
            context.UpdateLayoutBounds(1900, 500, 1); Render(root, "wilds-horizontal-preview.png", 1800, 240);
            var items = Descendants<ItemsControl>(root).Single(c => c.Name == "MonstersControl");
            var panel = Descendants<WrapPanel>(items).Single(p => p.IsItemsHost);
            Assert(panel.Children.Count == 5 && context.LayoutWidth == 1160, "five compact actors occupy one full horizontal row");
            Assert(panel.Children.Cast<FrameworkElement>().All(c => c.TransformToAncestor(panel).Transform(new Point()).Y == 0), "no forced three-column wrapping");
            var bars = Descendants<ContentControl>(panel).Where(c => c.Name == "HealthBar").ToArray();
            Assert(bars.Length == 5 && ((SolidColorBrush)bars[0].Foreground).Color == Color.FromRgb(204, 0, 0), "selected target retains the SmartHunter red health bar");
            Assert(bars.Skip(1).All(c => ((SolidColorBrush)c.Foreground).Color == Color.FromRgb(127, 152, 174)), "other monsters use a distinct color");
            data.Monsters[0].Selected = false; data.Monsters[4].Selected = true; applier.Apply(data); Drain(); root.UpdateLayout();
            Assert(((SolidColorBrush)bars[4].Foreground).Color == Color.FromRgb(204, 0, 0) && ((SolidColorBrush)bars[0].Foreground).Color != Color.FromRgb(204, 0, 0), "changing target updates both bar colors without requiring a respawn");
            var scroll = (ScrollViewer)window.FindName("MonsterScroll");
            context.UpdateLayoutBounds(700, 280, 1); Render(root, "wilds-horizontal-scroll-preview.png", 850, 400);
            Assert(scroll.ScrollableWidth > 0 && scroll.ActualWidth <= 700, "long horizontal row stays within the screen");
            scroll.ScrollToHorizontalOffset(scroll.ScrollableWidth); Drain(); root.UpdateLayout();
            var last = (FrameworkElement)panel.Children[4]; var bounds = last.TransformToAncestor(scroll).TransformBounds(new Rect(last.RenderSize));
            Assert(scroll.HorizontalOffset > 0 && bounds.Left < scroll.ActualWidth && bounds.Right > 0, "final actor remains reachable by horizontal scrolling");
            config.Layout = MonsterLayout.Vertical; context.UpdateFromConfig(); Render(root, "wilds-vertical-preview.png", 500, 700);
            Assert(context.LayoutWidth == 232 && panel.Children.Count == 5 && scroll.ScrollableHeight > 0, "vertical mode retains all actors and allows scrolling");
            Assert(((FrameworkElement)panel.Children[1]).TransformToAncestor(panel).Transform(new Point()).Y > 0, "vertical mode places the next monster below");
        }
        finally { window.Close(); applier.Clear(); config.Layout = oldLayout; config.WrapHorizontal = oldWrap; context.UpdateFromConfig(); }
    }
internal static void CompactDetails()
    {
        var config = ConfigHelper.Main.Values.Overlay.MonsterWidget;
        var fields = new[] { "CardWidth", "Layout", "ShowParts", "ShowUnchangedParts", "ShowStatusEffects", "ShowUnchangedStatusEffects", "ShowNumbers", "ShowPercents" }.Select(n => config.GetType().GetField(n)).ToArray();
        var saved = fields.Select(f => f.GetValue(config)).ToArray();
        config.Layout = MonsterLayout.Horizontal; config.ShowParts = config.ShowUnchangedParts = config.ShowStatusEffects = config.ShowUnchangedStatusEffects = config.ShowNumbers = config.ShowPercents = true;
        var applier = new WildsSnapshotApplier(); var data = WildsSnapshotApplier.Preview(5);
        data.Monsters[0].Parts[0].NameId = "LOC_PART_UNKNOWN"; data.Monsters[0].Parts[0].Count = 2;
        data.Monsters[0].Ailments[0].Active = true; data.Monsters[0].Ailments[0].Remaining = 10; data.Monsters[0].Ailments[0].MaxDuration = 30;
        data.Monsters[0].Ailments.Add(new WildsAilmentSnapshot { Address = 0xF000, Id = 4, Buildup = 1234, MaxBuildup = 12345 });
        var vm = OverlayViewModel.Instance; var context = vm.MonsterWidget.Context;
        applier.Apply(data); vm.IsGameActive = true; vm.IsPreview = true;
        var window = new MonsterWidgetWindow(); var root = (FrameworkElement)window.Content;
        var trace = new StringWriter(); var listener = new TextWriterTraceListener(trace); PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
        try
        {
            foreach (var language in new[] { "en-US.json", "zh-TW.json" })
            {
                SettingsViewModel.Instance.SelectedLanguage = SettingsViewModel.Instance.Languages.First(l => l.FileName == LanguageCatalog.ProfileName(language));
                string oldName = ConfigHelper.Localization.Values.Strings["LOC_PART_UNKNOWN"];
                string monsterNameKey = ConfigHelper.MonsterData.Values.Monsters[context.Monsters.First().Id].NameStringId;
                string oldMonsterName = ConfigHelper.Localization.Values.Strings[monsterNameKey];
                ConfigHelper.Localization.Values.Strings["LOC_PART_UNKNOWN"] = "A deliberately long part name for compact layout";
                ConfigHelper.Localization.Values.Strings[monsterNameKey] = "A deliberately long monster name that must not shrink the readable font";
                try
                {
                    foreach (int width in new[] { 180, 220, 300 })
                    {
                        config.CardWidth = width; context.UpdateFromConfig(); context.UpdateLayoutBounds(1900, 650, 1);
                        foreach (var monster in context.Monsters) monster.UpdateLocalization();
                        Render(root, "wilds-compact-" + language + "-" + width + ".png", 1650, 420);
                        var cards = Descendants<StackPanel>(root).Where(p => p.Name == "Root" && p.DataContext is SmartHunter.Game.Data.Monster).ToArray();
                        Assert(cards.Length == 5 && cards.All(c => Math.Abs(c.ActualWidth - width) < 0.1), "all monster columns use the configured width");
                        var first = cards[0];
                        var headings = Descendants<TextBlock>(first).Where(t => t.ToolTip != null && ReferenceEquals(t.DataContext, first.DataContext)).ToArray();
                        Assert(headings.Length == 2 && headings.All(t => t.FontSize == 20), "legacy monster name size independent of column width");
                        Assert(headings.All(t => t.ActualWidth <= width + 0.1 && t.TextTrimming == TextTrimming.CharacterEllipsis), "long monster name stays inside one column without shrinking");
                        var sizeGrid = Descendants<Grid>(first).Single(g => g.Name == "Size");
                        var sizeBounds = sizeGrid.TransformToAncestor(first).TransformBounds(new Rect(sizeGrid.RenderSize));
                        var nameBoundsInCard = headings[0].TransformToAncestor(first).TransformBounds(new Rect(headings[0].RenderSize));
                        Assert(nameBoundsInCard.Top >= sizeBounds.Bottom + 0.5, "size and monster name have separate readable lines");
                        var bars = Descendants<ContentControl>(first).Where(c => c.Name == "HealthBar" || c.Name == "Bar").ToArray();
                        Assert(bars.Length >= 6 && bars.All(c => Math.Abs(c.ActualWidth - width) < 0.1), "health, parts and statuses share one compact width");
                        foreach (var detail in Descendants<StackPanel>(first).Where(p => p.Name == "Root" && !(p.DataContext is SmartHunter.Game.Data.Monster)))
                        {
                            var name = Descendants<TextBlock>(detail).First(t => t.Name == "NameBlock");
                            var number = Descendants<TextBlock>(detail).First(t => (t.Name == "HealthBlock" || t.Name == "DurationBlock" || t.Name == "BuildupBlock") && t.Visibility == Visibility.Visible);
                            var nameBounds = name.TransformToAncestor(detail).TransformBounds(new Rect(name.RenderSize));
                            var numberBounds = number.TransformToAncestor(detail).TransformBounds(new Rect(number.RenderSize));
                            Assert(name.FontSize == 16 && number.FontSize == 16 && name.TextTrimming == TextTrimming.CharacterEllipsis, "legacy detail font remains readable at every column width");
                            Assert(nameBounds.Right <= numberBounds.Left + 0.5 && numberBounds.Right <= width + 0.5, "long labels do not overlap numbers or neighboring monsters");
                        }
                    }
                }
                finally { ConfigHelper.Localization.Values.Strings["LOC_PART_UNKNOWN"] = oldName; ConfigHelper.Localization.Values.Strings[monsterNameKey] = oldMonsterName; }
            }
            config.CardWidth = 220; context.UpdateFromConfig(); applier.Clear(); applier.Apply(WildsSnapshotApplier.Preview(5)); vm.IsGameActive = true;
            Render(root, "wilds-compact-preview.png", 1280, 360);
            listener.Flush(); Assert(trace.ToString().Length == 0, "compact layout binding errors: " + trace);
        }
        finally
        {
            PresentationTraceSources.DataBindingSource.Listeners.Remove(listener); listener.Dispose(); window.Close(); applier.Clear();
            for (int i = 0; i < fields.Length; i++) fields[i].SetValue(config, saved[i]); context.UpdateFromConfig();
            SettingsViewModel.Instance.SelectedLanguage = SettingsViewModel.Instance.Languages.First(l => l.FileName == "zh-TW.Wilds.json");
        }
    }
internal static void ExternalSkin()
    {
        System.Reflection.Assembly.Load(typeof(OxyPlot.Wpf.Plot).Assembly.GetName());
        System.Reflection.Assembly.Load(typeof(Microsoft.Xaml.Behaviors.Behavior).Assembly.GetName());
        var settings = new System.Xaml.XamlXmlReaderSettings { LocalAssembly = typeof(ConsoleWindow).Assembly };
        using (var stream = File.OpenRead(FileContainer.GetFullPathFileName("Default.xaml")))
        using (var reader = new System.Xaml.XamlXmlReader(stream, System.Windows.Markup.XamlReader.GetWpfSchemaContext(), settings))
        {
            var resources = System.Windows.Markup.XamlReader.Load(reader) as ResourceDictionary;
            Assert(resources != null, "external skin loaded");
            var font = new Typeface((FontFamily)resources["Roboto"], FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
            Assert(font.TryGetGlyphTypeface(out var glyphs) && glyphs.FontUri.OriginalString.IndexOf("Roboto", StringComparison.OrdinalIgnoreCase) >= 0, "embedded Iceborne Roboto font resolves without system fallback");
        }
    }
}

static class DesktopDisplayTests
{
internal static void Background()
    {
        var vm = SettingsViewModel.Instance; string saved = ConfigHelper.Main.Values.BackgroundImagePath;
        string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "自訂 background " + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            vm.SetBackground(""); var builtIn = (BitmapSource)vm.BackgroundImage;
            Assert(builtIn.PixelWidth == 1920 && builtIn.IsFrozen, "offline built-in image present by default");
            byte[] pixels = Enumerable.Repeat((byte)120, 32 * 20 * 4).ToArray();
            var bitmap = BitmapSource.Create(32, 20, 96, 96, PixelFormats.Bgra32, null, pixels, 32 * 4);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var file = File.Create(path)) encoder.Save(file);
            vm.SetBackground(path); Assert(!ReferenceEquals(builtIn, vm.BackgroundImage), "custom background applies immediately");
            using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            Assert(ConfigHelper.Main.Values.BackgroundImagePath == path, "custom path persisted");
            File.Delete(path); ConfigHelper.Main.Values.BackgroundImagePath = path + ".missing";
            Assert(((BitmapSource)vm.BackgroundImage).PixelWidth == 1920, "missing custom image falls back to built-in");
            vm.Settings.Single(s => s.Key == "LOC_UI_BACKGROUND_IMAGE").Reset.Execute(null);
            Assert(ConfigHelper.Main.Values.BackgroundImagePath == "", "restore-default button persists");
            var window = new ConsoleWindow();
            try
            {
                var image = (Image)window.FindName("WildsBackdrop");
                window.Measure(new System.Windows.Size(1120, 740)); window.Arrange(new System.Windows.Rect(0, 0, 1120, 740)); window.UpdateLayout();
                Assert(image.Source != null && image.Effect is System.Windows.Media.Effects.BlurEffect, "image supplies the glass backdrop");
                Assert(((TextBox)window.FindName("LogText")).Effect == null, "log text remains sharp");
            }
            finally { window.Close(); }
        }
        finally { if (File.Exists(path)) File.Delete(path); vm.SetBackground(saved); }
    }
internal static void FontSizes()
    {
        var vm = SettingsViewModel.Instance;
        var setting = vm.Settings.Single(s => s.Key == "LOC_UI_INTERFACE_FONT_SIZE");
        var saved = setting.SelectedOption;
        var window = new ConsoleWindow();
        try
        {
            foreach (var option in setting.Options)
            {
                setting.SelectedOption = option;
                System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
                int size = Int32.Parse(option.Id);
                Assert(window.FontSize == size && ((ListBox)window.FindName("Navigation")).FontSize == size && ((TextBox)window.FindName("LogText")).FontSize == size, "live size " + size);
                Assert((int)Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(ConfigHelper.Main.FullPathFileName))["InterfaceFontSize"] == size, "size persisted");
                window.Measure(new System.Windows.Size(850, 600)); window.Arrange(new System.Windows.Rect(0, 0, 850, 600)); window.UpdateLayout();
            }
            var style = (System.Windows.Style)System.Windows.Application.Current.Resources["A_Style_TextBlock_MonsterDetail"];
            var detail = new TextBlock { Style = style };
            detail.Measure(new System.Windows.Size(1000, 1000));
            Assert(detail.FontSize == 16, "overlay keeps its original default font size independently of interface text");
        }
        finally { setting.SelectedOption = saved; window.Close(); }
    }
internal static void CampDisplay()
    {
        Assert(new MonsterWidgetConfig(0, 0).ShowInCamp, "camp display defaults on");
        var game = new FakeGame(); game.Monster(16); game.M.Byte(game.Stage + 0xC5, 1); game.M.Int(game.Stage + 0x88, 14);
        var reader = new WildsReader(game.M, game.Addresses, game.Base); var apply = new WildsSnapshotApplier();
        var camp = reader.Read(showInCamp: true); apply.Apply(camp);
        Assert(!camp.InHuntingZone && camp.CanDisplayData && camp.Monsters.Count == 1 && OverlayViewModel.Instance.MonsterWidget.Context.Monsters.Count == 1, "camp flag does not block live monsters");
        var check = WildsRuntimeCheck.From(camp); Assert(!check.InHuntingZone && check.CanDisplayData && !check.IsLoading, "diagnostics distinguish camp display from hunting-zone state");
        apply.Apply(reader.Read(showInCamp: false)); Assert(OverlayViewModel.Instance.MonsterWidget.Context.Monsters.Count == 0, "camp option can be disabled");
        game.M.Int(game.F("PauseManager", "PauseState"), 6);
        var loading = reader.Read(showInCamp: true); apply.Apply(loading);
        Assert(loading.IsLoading && !loading.CanDisplayData && loading.Monsters.Count == 0, "loading cannot expose stale monsters");
        game.M.Int(game.F("PauseManager", "PauseState"), 0); game.SetPopulation();
        apply.Apply(reader.Read(showInCamp: true)); Assert(OverlayViewModel.Instance.MonsterWidget.Context.Monsters.Count == 0, "unloaded camp actors are removed"); apply.Clear();
    }
}

static class ViewportTests
{
internal static void WindowWidth()
    {
        var game=GameProfile.Active;var main=ConfigHelper.Main.Values;var vm=OverlayViewModel.Instance;var world=main.Overlays.World;var wilds=main.Overlays.Wilds;bool active=vm.IsGameActive,hidden=vm.HideWidgetsRequested;
        try{main.Overlays.World=GameOverlayConfig.WorldDefaults();main.Overlays.Wilds=new OverlayConfig();vm.IsGameActive=true;vm.HideWidgetsRequested=false;
            foreach(var choice in new[]{GameChoice.World,GameChoice.Wilds})
            {
                GameProfile.Active=choice;ConfigHelper.SwitchGameData(choice);var config=main.Overlay.MonsterWidget;config.X=config.Y=0;config.Scale=1;config.WindowWidth=620;config.Rows=2;var context=vm.MonsterWidget.Context;context.UpdateFromConfig();
                string monsterId=ConfigHelper.MonsterData.Values.Monsters.Keys.First();for(int i=0;i<12;i++)context.UpdateAndGetMonster((ulong)(0x100000+i*0x10000),monsterId,10000,5000,1,1);
                int card=config.CardWidth,font=config.FontSize;var window=new MonsterWidgetWindow{Opacity=0,ShowActivated=false};window.Show();
                try{Drain();window.UpdateLayout();Assert(Math.Abs(window.ActualWidth-context.WindowWidth)<1 && Math.Abs(window.ActualWidth-620)<1,"fixed whole-window width: "+choice+" / "+window.ActualWidth);TestSupport.Render((FrameworkElement)window.Content,"unified-"+choice+"-window-620.png",620,(int)Math.Ceiling(window.ActualHeight));config.WindowWidth=820;context.UpdateFromConfig();Drain();window.UpdateLayout();
                    Assert(Math.Abs(window.ActualWidth-context.WindowWidth)<1 && context.Monsters.Count==12 && config.CardWidth==card && config.FontSize==font,"resizing retains all actors, card width and font size: "+choice+" width="+window.ActualWidth+" expected="+context.WindowWidth+" actors="+context.Monsters.Count+" card="+config.CardWidth+"/"+card+" font="+config.FontSize+"/"+font);var scroll=(ScrollViewer)window.FindName("MonsterScroll");if(choice==GameChoice.Wilds)Assert(scroll.ComputedHorizontalScrollBarVisibility!=Visibility.Visible && scroll.ComputedVerticalScrollBarVisibility!=Visibility.Visible,"custom width never exposes Wilds scrollbars");
                    var list=Descendants<ItemsControl>(scroll).First(c=>c.Name=="MonstersControl");
                    var bounds=list.TransformToAncestor(scroll).TransformBounds(new Rect(list.RenderSize));
                    Assert(Math.Abs(bounds.Left-(scroll.ViewportWidth-list.ActualWidth)/2)<2,"custom viewport centers the complete row block: "+choice+" offset="+bounds.Left+" expected="+(scroll.ViewportWidth-list.ActualWidth)/2);
                    config.CenterMonsters=false;context.UpdateFromConfig();Drain();window.UpdateLayout();bounds=list.TransformToAncestor(scroll).TransformBounds(new Rect(list.RenderSize));Assert(Math.Abs(bounds.Left)<2,"left alignment remains selectable");config.CenterMonsters=true;context.UpdateFromConfig();Drain();
                    TestSupport.Render((FrameworkElement)window.Content,"unified-"+choice+"-window-820.png",820,(int)Math.Ceiling(window.ActualHeight));config.WindowWidth=0;context.UpdateFromConfig();Drain();Assert(window.SizeToContent==SizeToContent.WidthAndHeight && Double.IsNaN(window.Width),"automatic sizing restored");
                }finally{window.Close();context.Monsters.Clear();}
            }
        }finally{main.Overlays.World=world;main.Overlays.Wilds=wilds;GameProfile.Active=game;vm.IsGameActive=active;vm.HideWidgetsRequested=hidden;ConfigHelper.SwitchGameData(game);}
    }
internal static void WidthEditor()
    {
        var main=ConfigHelper.Main.Values;var game=GameProfile.Active;var editing=main.SettingsGame;int world=main.Overlays.World.MonsterWidget.WindowWidth,wilds=main.Overlays.Wilds.MonsterWidget.WindowWidth;var vm=SettingsViewModel.Instance;
        try{GameProfile.Active=GameChoice.Wilds;main.SettingsGame=GameChoice.World;main.Overlays.Wilds.MonsterWidget.WindowWidth=0;vm.RefreshGameProfile();var setting=vm.Settings.Single(s=>s.Key=="LOC_UI_MONSTER_WINDOW_WIDTH");setting.Text="741";
            Assert(main.Overlays.World.MonsterWidget.WindowWidth==741 && main.Overlays.Wilds.MonsterWidget.WindowWidth==0,"arbitrary width edits the selected profile only");foreach(string invalid in new[]{"x","399","7681","-1"}){setting.Text=invalid;Assert(setting.ValidationMessage!="" && main.Overlays.World.MonsterWidget.WindowWidth==741,"invalid width cannot replace the saved one");}setting.Text="0";Assert(main.Overlays.World.MonsterWidget.WindowWidth==0 && setting.ValidationMessage=="","zero resets to automatic");
        }finally{main.Overlays.World.MonsterWidget.WindowWidth=world;main.Overlays.Wilds.MonsterWidget.WindowWidth=wilds;main.SettingsGame=editing;GameProfile.Active=game;ConfigHelper.SaveSettings();vm.RefreshGameProfile();}
    }
}

static class WindowLayoutTests
{
internal static void Centering()
    {
        var panel=new CenteredWrapPanel();for(int i=0;i<5;i++) panel.Children.Add(new Border{Width=232,Height=80});
        panel.Measure(new Size(696,400));panel.Arrange(new Rect(0,0,696,400));
        Assert(LayoutInformation.GetLayoutSlot((FrameworkElement)panel.Children[0]).Left==0 && LayoutInformation.GetLayoutSlot((FrameworkElement)panel.Children[3]).Left==116,"complete first row and centered incomplete second row");
        panel.CenterRows=false;panel.Arrange(new Rect(0,0,696,400));Assert(LayoutInformation.GetLayoutSlot((FrameworkElement)panel.Children[3]).Left==0,"centering toggle restores left alignment");
        var vm=OverlayViewModel.Instance;var cfg=ConfigHelper.Main.Values.Overlay.MonsterWidget;var layout=cfg.Layout;bool wrap=cfg.WrapHorizontal,center=cfg.CenterMonsters;var applier=new SmartHunter.Wilds.WildsSnapshotApplier();
        try {cfg.Layout=MonsterLayout.Horizontal;cfg.WrapHorizontal=false;cfg.CenterMonsters=true;
            foreach(int count in new[]{1,2}) {applier.Apply(SmartHunter.Wilds.WildsSnapshotApplier.Preview(count));vm.MonsterWidget.Context.UpdateLayoutBounds(1100,500,1);Assert(vm.MonsterWidget.Context.LayoutWidth==vm.MonsterWidget.Context.CardStride*3,"reserve three-card span for "+count+" monsters");}
            Assert(vm.MonsterWidget.Context.FontSize==cfg.FontSize,"width never changes text size");
        }finally {applier.Clear();cfg.Layout=layout;cfg.WrapHorizontal=wrap;cfg.CenterMonsters=center;vm.MonsterWidget.Context.UpdateFromConfig();}
    }
internal static void Header()
    {
        var vm=SettingsViewModel.Instance;int size=ConfigHelper.Main.Values.InterfaceFontSize;var section=vm.SelectedSection;var window=new SmartHunter.Ui.Windows.ConsoleWindow();
        try {vm.SelectedSection=vm.Sections.First(s=>s.Id=="General");foreach(int font in new[]{16,20}) {
            var setting=vm.Settings.Single(s=>s.Key=="LOC_UI_INTERFACE_FONT_SIZE");setting.SelectedOption=setting.Options.Single(o=>o.Id==font.ToString());
            TestSupport.Render((FrameworkElement)window.Content,"unified-compact-header-"+font+".png",850,740);
            var picker=(ComboBox)window.FindName("LanguagePicker");var box=(TextBox)window.FindName("SearchBox");var root=(FrameworkElement)window.Content;
            var p=picker.TransformToAncestor(root).Transform(new Point());var b=box.TransformToAncestor(root).Transform(new Point());
            Assert(p.X+picker.ActualWidth<=850 && b.Y<font*12 && picker.FontSize==font,"compact visible controls at readable size "+font);
        }}finally {ConfigHelper.Main.Values.InterfaceFontSize=size;ConfigHelper.SaveSettings();vm.SelectedSection=section;window.Close();}
    }
}
