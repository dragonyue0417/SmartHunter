using System;
using System.Windows;
using System.Windows.Interop;
namespace SmartHunter.Ui.Behaviors
{
    internal static class WidgetWorkArea
    {
        internal static Size Remaining(Window window,double x,double y)
        {
            var handle=new WindowInteropHelper(window).Handle;
            if(handle!=IntPtr.Zero && PresentationSource.FromVisual(window)?.CompositionTarget!=null)
            {
                // Screen uses physical monitor pixels. PointFromScreen handles
                // the window's DPI and monitor origin, including negative X/Y.
                var area=System.Windows.Forms.Screen.FromHandle(handle).WorkingArea;
                try
                {
                    var edge=window.PointFromScreen(new Point(area.Right,area.Bottom));
                    return new Size(Math.Max(0,edge.X-80),Math.Max(0,edge.Y-100));
                }
                catch(InvalidOperationException) { /* The window is attaching or detaching. */ }
            }
            var work=SystemParameters.WorkArea;
            return new Size(Math.Max(0,work.Right-Math.Max(work.Left,x)-80),Math.Max(0,work.Bottom-Math.Max(work.Top,y)-100));
        }
    }
}
