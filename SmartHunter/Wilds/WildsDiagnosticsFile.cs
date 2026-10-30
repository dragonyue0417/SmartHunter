using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
namespace SmartHunter.Wilds
{
    internal static class WildsDiagnosticsFile
    {
        public static void Write(string path,WildsNetworkReport report,int maximumBytes=16*1024*1024)
        {
            string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            try
            {
                while(true)
                {
                    using(var stream=new FileStream(temporary,FileMode.Create,FileAccess.Write,FileShare.None,65536))
                    using(var writer=new StreamWriter(stream,new UTF8Encoding(false),65536))
                    using(var json=new JsonTextWriter(writer){Formatting=Formatting.Indented})
                        JsonSerializer.CreateDefault().Serialize(json,report);
                    if(new FileInfo(temporary).Length<=maximumBytes || report.Checks.Count<=1)break;
                    report.Checks.RemoveAt(0);
                }
                if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);
            }
            finally{if(File.Exists(temporary))File.Delete(temporary);}
        }
    }
}
