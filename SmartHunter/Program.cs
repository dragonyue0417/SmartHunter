using System;
using SmartHunter.Core.Updates;

namespace SmartHunter
{
    public static class Program
    {
        [STAThread] public static int Main(string[] args)
        {
            // Installation must run before WPF resources or configuration
            // containers can initialize in the temporary update directory.
            if(args.Length>0 && args[0]=="--apply-update")return UpdateFiles.RunHelper(args);
            var app=new App();app.InitializeComponent();return app.Run();
        }
    }
}
