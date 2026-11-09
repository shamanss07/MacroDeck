using System;
using System.Windows.Forms;

namespace MacroDeck
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (!Administration.AllowStartup(args)) return;
            Application.Run(new MainForm());
        }
    }
}
