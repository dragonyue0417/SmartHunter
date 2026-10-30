using System.Windows;
using System.Windows.Interop;

namespace SmartHunter.Core.Helpers
{
    public static class WindowHelper
    {
        static uint TopMostWindowSizePositions
        {
            get
            {
                return (uint)WindowsApi.WindowSizePositionFlag.SWP_NOMOVE
                    | (uint)WindowsApi.WindowSizePositionFlag.SWP_NOSIZE
                    | (uint)WindowsApi.WindowSizePositionFlag.SWP_SHOWWINDOW
                    | (uint)WindowsApi.WindowSizePositionFlag.SWP_NOACTIVATE;
            }
        }

        static uint TopMostSelectableWindowStyleFlags
        {
            get
            {
                return ((uint)WindowsApi.WindowStyleFlag.WS_EX_LAYERED
                    | (uint)WindowsApi.WindowStyleFlag.WS_EX_TOPMOST
                    | (uint)WindowsApi.WindowStyleFlag.WS_EX_TOOLWINDOW)
                    & ~(uint)WindowsApi.WindowStyleFlag.WS_EX_APPWINDOW;
            }
        }

        static uint TopMostTransparentWindowStyleFlags
        {
            get
            {
                return ((uint)WindowsApi.WindowStyleFlag.WS_EX_LAYERED
                    | (uint)WindowsApi.WindowStyleFlag.WS_EX_TRANSPARENT
                    | (uint)WindowsApi.WindowStyleFlag.WS_EX_TOPMOST
                    | (uint)WindowsApi.WindowStyleFlag.WS_EX_TOOLWINDOW)
                    & ~(uint)WindowsApi.WindowStyleFlag.WS_EX_APPWINDOW;
            }
        }

        public static void SetTopMostSelectable(Window window)
        {
            var handle = new WindowInteropHelper(window).EnsureHandle();
            WindowsApi.SetWindowLong(handle, (int)WindowsApi.WindowLongGroup.GWL_EXSTYLE, TopMostSelectableWindowStyleFlags);
            WindowsApi.SetWindowPos(handle, -1, 0, 0, 0, 0, TopMostWindowSizePositions);
        }

        public static void SetTopMostTransparent(Window window)
        {
            var handle = new WindowInteropHelper(window).EnsureHandle();
            WindowsApi.SetWindowLong(handle, (int)WindowsApi.WindowLongGroup.GWL_EXSTYLE, TopMostTransparentWindowStyleFlags);
            WindowsApi.SetWindowPos(handle, -1, 0, 0, 0, 0, TopMostWindowSizePositions);
        }

    }
}
