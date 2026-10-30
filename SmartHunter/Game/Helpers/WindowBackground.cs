using System;
using System.IO;
using System.Windows.Media.Imaging;

namespace SmartHunter.Game.Helpers
{
    public static class WindowBackground
    {
        public static BitmapImage Load(string path)
        {
            var image = new BitmapImage();
            if (String.IsNullOrWhiteSpace(path))
            {
                image.BeginInit();
                image.UriSource = new Uri("pack://application:,,,/SmartHunter;component/Ui/Images/WildsBackdrop.jpg");
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.EndInit();
            }
            else
            {
                string fullPath = Path.IsPathRooted(path) ? path : Path.Combine(Core.FileContainer.GetFullPath(), path);
                using (var file = File.OpenRead(fullPath))
                {
                    if (file.Length > 20 * 1024 * 1024) throw new IOException("Background image must be smaller than 20 MB.");
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.DecodePixelWidth = 1920;
                    image.StreamSource = file;
                    image.EndInit();
                }
            }
            image.Freeze();
            return image;
        }
    }
}
