using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace MacroDeck
{
    internal static class ClipboardImageImport
    {
        // A supplied data object also allows validation without changing the user's clipboard.
        public static Bitmap ReadImage(IDataObject data)
        {
            if (data == null) return null;
            if (data.GetDataPresent("PNG"))
            {
                object png = data.GetData("PNG");
                Stream stream = png as Stream;
                byte[] bytes = png as byte[];
                if (stream != null)
                {
                    long original = stream.CanSeek ? stream.Position : 0;
                    try
                    {
                        if (stream.CanSeek) stream.Position = 0;
                        using (Image image = Image.FromStream(stream)) return new Bitmap(image);
                    }
                    finally { if (stream.CanSeek) stream.Position = original; }
                }
                if (bytes != null)
                    using (MemoryStream buffer = new MemoryStream(bytes))
                    using (Image image = Image.FromStream(buffer)) return new Bitmap(image);
            }
            if (data.GetDataPresent(DataFormats.Bitmap, true))
            {
                Image image = data.GetData(DataFormats.Bitmap, true) as Image;
                if (image != null) return new Bitmap(image);
            }
            if (data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] paths = data.GetData(DataFormats.FileDrop) as string[];
                if (paths != null && paths.Length == 1 && File.Exists(paths[0]))
                {
                    string extension = Path.GetExtension(paths[0]).ToLowerInvariant();
                    if (extension == ".png" || extension == ".jpg" || extension == ".jpeg" || extension == ".bmp")
                        using (Image image = Image.FromFile(paths[0])) return new Bitmap(image);
                }
            }
            return null;
        }

        public static string SaveReference(Image image, string prefix)
        {
            if (image == null) throw new ArgumentNullException("image");
            string directory = Path.Combine(Path.GetTempPath(), "MacroDeckVisualTargets");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, prefix + "-" + Guid.NewGuid().ToString("N") + ".png");
            using (Bitmap copy = new Bitmap(image)) copy.Save(path, ImageFormat.Png);
            return path;
        }
    }
}
