using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;

namespace SmartHunter.Core.Updates
{
    public static class UpdateFiles
    {
        public static string Hash(string path){using(var hash=SHA256.Create())using(var input=File.OpenRead(path))return BitConverter.ToString(hash.ComputeHash(input)).Replace("-","").ToLowerInvariant();}
        public static string NewWorkspace(){string path=Path.Combine(Path.GetTempPath(),"SmartHunter-Updates",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(path);return path;}
        public static Version Validate(string directory,Version expected=null)
        {
            string path=Path.Combine(directory,UpdatePackage.ManifestName);
            RejectLinks(path);
            if(new FileInfo(path).Length>65536)throw new InvalidDataException("Update manifest too large.");
            var package=UpdatePackage.Parse(File.ReadAllText(path),requireSizes:false);
            var version=package.Version;
            if(expected!=null && version!=expected)throw new InvalidDataException("Wrong update version.");
            foreach(string name in UpdatePackage.PayloadFiles)
            {
                string file=Path.Combine(directory,name.Replace('/',Path.DirectorySeparatorChar));RejectLinks(file);
                long length=new FileInfo(file).Length;
                if(length<=0 || length>UpdatePackage.MaximumFileSize || package.Sizes.TryGetValue(name,out long expectedSize) && length!=expectedSize)throw new InvalidDataException("Update file length mismatch: "+name);
                if(Hash(file)!=package.Files[name])throw new InvalidDataException("Update file checksum mismatch: "+name);
            }
            string executable=Path.Combine(directory,"SmartHunter.exe");
            if(!Version.TryParse(FileVersionInfo.GetVersionInfo(executable).FileVersion,out var actual) || actual!=version)throw new InvalidDataException("Executable version mismatch.");
            return version;
        }
        public static void RejectLinks(string path)
        {
            for(string current=Path.GetFullPath(path);!String.IsNullOrEmpty(current);current=Path.GetDirectoryName(current))
                if((File.Exists(current)||Directory.Exists(current)) && (File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new IOException("Update paths cannot contain reparse points.");
        }
        static void Copy(string source,string target)
        {
            for(int attempt=0;;attempt++)try{File.Copy(source,target,true);return;}
            catch(IOException){if(attempt>=7)throw;Thread.Sleep(250);}
        }
        // Copy only program-owned files. Config, Languages, Logs, diagnostics,
        // custom themes and unrelated files are never recursively enumerated.
        public static void Install(string stage,string destination)
        {
            Validate(stage);
            string root=Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar);
            if(!Directory.Exists(root) || !File.Exists(Path.Combine(root,"SmartHunter.exe")) || root.Length<4 || String.Equals(root,Path.GetFullPath(stage).TrimEnd(Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Invalid update destination.");
            var names=UpdatePackage.PayloadFiles.Where(n=>n!="SmartHunter.exe").Concat(new[]{"SmartHunter.exe",UpdatePackage.ManifestName}).ToArray();
            foreach(string name in names)RejectLinks(Path.Combine(root,name.Replace('/',Path.DirectorySeparatorChar)));
            string previous=Path.Combine(Path.GetDirectoryName(stage),"previous");Directory.CreateDirectory(previous);
            var existed=new HashSet<string>();var changed=new List<string>();
            // Complete the preflight copy before changing any destination file.
            foreach(string name in names)
            {
                string old=Path.Combine(root,name),saved=Path.Combine(previous,name);Directory.CreateDirectory(Path.GetDirectoryName(saved));
                if(File.Exists(old)){File.Copy(old,saved,true);existed.Add(name);}
            }
            try
            {
                foreach(string name in names){string output=Path.Combine(root,name);Directory.CreateDirectory(Path.GetDirectoryName(output));changed.Add(name);Copy(Path.Combine(stage,name),output);}
                Validate(root);
            }
            catch
            {
                foreach(string name in changed.AsEnumerable().Reverse())
                    try{if(existed.Contains(name))Copy(Path.Combine(previous,name),Path.Combine(root,name));else File.Delete(Path.Combine(root,name));}catch(Exception error){File.AppendAllText(Path.Combine(previous,"restore-error.txt"),error+Environment.NewLine);}
                throw;
            }
        }
        public static string Arguments(string destination,int process,long started)=>"--apply-update \""+Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar)+"\" "+process+" "+started;
        public static void Launch(string stage,string destination)
        {
            using(var self=Process.GetCurrentProcess())Process.Start(new ProcessStartInfo(Path.Combine(stage,"SmartHunter.exe"),Arguments(destination,self.Id,self.StartTime.ToUniversalTime().Ticks)){UseShellExecute=false,WorkingDirectory=stage,WindowStyle=ProcessWindowStyle.Hidden});
        }
        public static int RunHelper(string[] args)
        {
            string stage=AppDomain.CurrentDomain.BaseDirectory;
            try
            {
                if(args.Length!=4 || args[0]!="--apply-update" || !Int32.TryParse(args[2],out int pid) || !Int64.TryParse(args[3],out long ticks) || pid<=0)throw new InvalidDataException("Invalid update helper arguments.");
                Validate(stage);
                string identity;
                using(var hash=SHA256.Create())identity=BitConverter.ToString(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(args[1]).ToLowerInvariant()))).Replace("-","").Substring(0,24);
                using(var mutex=new Mutex(false,"Local\\SmartHunter-Update-"+identity))
                {
                    if(!mutex.WaitOne(0))throw new IOException("Another update is already running.");
                    try
                    {
                        Process parent=null;try{parent=Process.GetProcessById(pid);}catch(ArgumentException){}
                        if(parent!=null)using(parent)
                        {
                            if(parent.StartTime.ToUniversalTime().Ticks!=ticks || !String.Equals(parent.MainModule.FileName,Path.Combine(Path.GetFullPath(args[1]),"SmartHunter.exe"),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Update parent identity mismatch.");
                            if(!parent.WaitForExit(30000))throw new IOException("The application did not exit in time.");
                        }
                        Install(stage,args[1]);
                        Process.Start(new ProcessStartInfo(Path.Combine(args[1],"SmartHunter.exe")){UseShellExecute=false,WorkingDirectory=args[1]});return 0;
                    }
                    finally{mutex.ReleaseMutex();}
                }
            }
            catch(Exception error)
            {
                File.AppendAllText(Path.Combine(stage,"update-error.txt"),DateTime.UtcNow.ToString("O")+" "+error+Environment.NewLine);
                System.Windows.MessageBox.Show("SmartHunter update could not be installed. Your settings were preserved.\n"+error.Message,"SmartHunter",System.Windows.MessageBoxButton.OK,System.Windows.MessageBoxImage.Warning);return 1;
            }
        }
    }
}
