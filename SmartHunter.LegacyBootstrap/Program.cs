using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;

// Standalone .NET Framework entry point for the original single-EXE updater.
// No dependency on Newtonsoft, WPF or DLLs already present in an old folder.
static class Program
{
    [STAThread] static int Main()
    {
        try
        {
            string destination=AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            string workspace=Path.Combine(Path.GetTempPath(),"SmartHunter-Updates",Guid.NewGuid().ToString("N")),stage=Path.Combine(workspace,"incoming");Directory.CreateDirectory(stage);
            var allowed=new HashSet<string>(StringComparer.Ordinal){"SmartHunter.exe","SmartHunter.exe.config","Themes/Default.xaml","Newtonsoft.Json.dll","Microsoft.Xaml.Behaviors.dll","OxyPlot.dll","OxyPlot.Wpf.dll","UpdateManifest.json"};
            var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);long total=0;
            using(var input=Assembly.GetExecutingAssembly().GetManifestResourceStream("SmartHunter.Update.zip"))
            using(var zip=new ZipArchive(input,ZipArchiveMode.Read))foreach(var entry in zip.Entries)
            {
                if(!allowed.Contains(entry.FullName)||!seen.Add(entry.FullName)||entry.Length<0||entry.Length>32*1024*1024||(total+=entry.Length)>64*1024*1024)throw new InvalidDataException("Unexpected upgrade package content.");
                string target=Path.Combine(stage,entry.FullName);Directory.CreateDirectory(Path.GetDirectoryName(target));
                using(var content=entry.Open())using(var output=new FileStream(target,FileMode.CreateNew))
                {
                    var buffer=new byte[65536];long written=0;int read;
                    while((read=content.Read(buffer,0,buffer.Length))>0)
                    {written+=read;if(written>entry.Length)throw new InvalidDataException("Upgrade entry length mismatch.");output.Write(buffer,0,read);}
                    if(written!=entry.Length)throw new InvalidDataException("Incomplete upgrade entry.");
                }
            }
            if(seen.Count!=allowed.Count)throw new InvalidDataException("Incomplete upgrade package.");
            using(var self=Process.GetCurrentProcess())
            {
                string args="--apply-update \""+destination+"\" "+self.Id+" "+self.StartTime.ToUniversalTime().Ticks;
                Process.Start(new ProcessStartInfo(Path.Combine(stage,"SmartHunter.exe"),args){UseShellExecute=false,WorkingDirectory=stage,WindowStyle=ProcessWindowStyle.Hidden});
            }
            return 0;
        }
        catch(Exception error){System.Windows.Forms.MessageBox.Show("SmartHunter upgrade failed. Your existing settings were not removed.\n"+error.Message,"SmartHunter");return 1;}
    }
}
