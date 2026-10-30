using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using SmartHunter.Core;
using SmartHunter.Core.Helpers;
using SmartHunter.Core.Windows;
using SmartHunter.Game.Helpers;
using SmartHunter.Game.Data.ViewModels;

namespace SmartHunter.Game
{
    public class WildsOverlay : Overlay
    {
        SmartHunter.World.UnifiedMemoryUpdater m_MemoryUpdater;
        public WildsOverlay(Window mainWindow, params WidgetWindow[] widgetWindows) : base(mainWindow, widgetWindows)
        {
            ConfigHelper.Main.Loaded += (s, e) => { UpdateWidgetsFromConfig(); };
            ConfigHelper.SettingsSaved += (s, e) => { UpdateWidgetsFromConfig(); };
            ConfigHelper.Localization.Loaded += (s, e) => { RefreshWidgetsLayout(); };
            ConfigHelper.MonsterData.Loaded += (s, e) => { RefreshWidgetsLayout(); };
            ConfigHelper.PlayerData.Loaded += (s, e) => { RefreshWidgetsLayout(); };

            m_MemoryUpdater = new SmartHunter.World.UnifiedMemoryUpdater();
        }

        public void Stop() { m_MemoryUpdater?.Dispose(); StopInput(); }

        protected override void InputReceived(Key key, bool isDown)
        {
            foreach (var controlKeyPair in ConfigHelper.Main.Values.Keybinds.Where(keybind => keybind.Value == key))
            {
                HandleControl(controlKeyPair.Key, isDown);
            }
        }

        private void HandleControl(InputControl control, bool isDown)
        {
            if (control == InputControl.ManipulateWidget && isDown && !OverlayViewModel.Instance.CanManipulateWindows)
            {
                OverlayViewModel.Instance.CanManipulateWindows = true;

                // Make all the windows selectable
                foreach (var widgetWindow in WidgetWindows)
                {
                    WindowHelper.SetTopMostSelectable(widgetWindow as Window);
                }
            }
            else if (control == InputControl.ManipulateWidget && !isDown && OverlayViewModel.Instance.CanManipulateWindows)
            {
                OverlayViewModel.Instance.CanManipulateWindows = false;

                bool canSaveConfig = false;

                // Return all windows to their click through state
                foreach (var widgetWindow in WidgetWindows)
                {
                    WindowHelper.SetTopMostTransparent(widgetWindow as Window);

                    if (widgetWindow.Widget.CanSaveConfig)
                    {
                        canSaveConfig = true;
                        widgetWindow.Widget.CanSaveConfig = false;
                    }
                }

                if (canSaveConfig)
                {
                    ConfigHelper.Main.Save();
                }
            }
            else if (control == InputControl.HideWidgets)
            {
                OverlayViewModel.Instance.HideWidgetsRequested = isDown;
            }
        }
    }
}
