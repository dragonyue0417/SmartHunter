using System;
using System.IO;
using System.Text;
using System.Threading;

namespace SmartHunter.Core
{
    internal static class AtomicFile
    {
        internal static void Write(string path, string text)
        {
            bool unchanged = false;
            Retry(() => unchanged = File.Exists(path) && File.ReadAllText(path) == text);
            if (unchanged) return;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, text, new UTF8Encoding(false));
                Retry(() =>
                {
                    if (File.Exists(path)) File.Replace(temporary, path, null);
                    else File.Move(temporary, path);
                });
            }
            catch (IOException error)
            {
                throw new IOException("Unable to write file: " + Path.GetFullPath(path), error);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        static void Retry(Action operation)
        {
            for (int attempt = 0; ; attempt++)
            {
                try { operation(); return; }
                catch (IOException error) when (attempt < 5 && IsBusy(error))
                {
                    // Another process may briefly hold a file during startup or
                    // an external edit. Keep the original and retry atomically.
                    Thread.Sleep(50);
                }
            }
        }

        static bool IsBusy(IOException error)
        {
            int code = error.HResult & 0xffff;
            return code == 32 || code == 33 || code == 1175;
        }
    }
}
