using System;
using System.Windows.Forms;

namespace MacroDeck
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            FontManager.PreloadInstalledFamilies();
            Application.Run(new MainForm());
        }
    }
}
