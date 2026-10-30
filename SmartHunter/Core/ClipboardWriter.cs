using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using SmartHunter.Game.Helpers;

namespace SmartHunter.Core
{
    public sealed class ClipboardWriter
    {
        readonly Action<string> write;
        readonly Func<int,Task> delay;
        int generation;
        public ClipboardWriter(Action<string> write,Func<int,Task> delay) {this.write=write;this.delay=delay;}
        public async Task<bool> TryWrite(string text)
        {
            int request=++generation;
            for(int attempt=0;attempt<6;attempt++)
            {
                if(request!=generation) return false;
                try {write(text);return true;}
                catch(ExternalException error) when(error.ErrorCode==unchecked((int)0x800401D0))
                {if(attempt<5) await delay(50*(attempt+1));}
            }
            return false;
        }
    }
    public static class ClipboardService
    {
        internal static System.Windows.Forms.DataObject TextData(string text)
        {
            var data=new System.Windows.Forms.DataObject();
            data.SetText(text,System.Windows.Forms.TextDataFormat.UnicodeText);
            return data;
        }
        internal static ClipboardWriter Writer=new ClipboardWriter(text=>System.Windows.Forms.Clipboard.SetDataObject(TextData(text),true,0,0),ms=>Task.Delay(ms));
        static int sequence;
        public static void Copy(string text)
        {
            if(String.IsNullOrEmpty(text)) return;
            // Clipboard/OLE work must run after the native input callback returns.
            int request=++sequence;
            Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Background,new Action(async ()=>
            {
                if(request!=sequence) return;
                try
                {
                    if(!await Writer.TryWrite(text) && request==sequence) Log.WriteLine(LocalizationHelper.GetString("LOC_LOG_CLIPBOARD_BUSY"));
                }
                catch(Exception error) {Log.WriteException(error);}
            }));
        }
    }
}
