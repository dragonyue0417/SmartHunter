using System;
using System.ComponentModel;
using System.IO;
using System.Security.AccessControl;

namespace SmartHunter.Core
{
    public static class Log
    {
        static string s_FileName = FileContainer.GetFullPathFileName("Log.SmartHunter.txt");

        public static event EventHandler<GenericEventArgs<string>> LineReceived;

        public static void WriteLine(string message)
        {
            string line = String.Format("[{0:yyyy-MM-dd HH:mm:ss}] {1}", DateTimeOffset.Now.ToUniversalTime(), message);
            try { Console.WriteLine(line); } catch (IOException) { }

            bool isDesignInstance = LicenseManager.UsageMode == LicenseUsageMode.Designtime;
            if (!isDesignInstance)
            {
                try
                {
                    using (FileStream fileStream = new FileStream(s_FileName, FileMode.OpenOrCreate, FileSystemRights.AppendData, FileShare.Write, 4096, FileOptions.None))
                    {
                        using (StreamWriter streamWriter = new StreamWriter(fileStream))
                        {
                            streamWriter.AutoFlush = true;
                            streamWriter.WriteLine(line);
                        }
                    }
                }
                catch (Exception) { }
            }
            // Keep the on-disk exception even if a UI log subscriber fails.
            var listeners = LineReceived;
            if (listeners != null)
                foreach (EventHandler<GenericEventArgs<string>> listener in listeners.GetInvocationList())
                    try { listener(null, new GenericEventArgs<string>(line)); }
                    catch (Exception error) { System.Diagnostics.Trace.WriteLine(error); }
        }

        public static void WriteException(Exception exception)
        {
            WriteLine(exception.ToString());
        }
    }
}
