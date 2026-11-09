using System;
using System.Drawing;
using System.Windows.Forms;

namespace MacroDeck
{
    internal sealed class TrayIconController : IDisposable
    {
        private readonly NotifyIcon notifyIcon;
        private readonly ContextMenuStrip menu;
        private readonly ToolStripMenuItem openItem;
        private readonly ToolStripMenuItem exitItem;
        private readonly Icon icon;
        private readonly Func<string, string> translate;
        private readonly Action restore;
        private bool disposed;

        public TrayIconController(Icon applicationIcon, Func<string, string> translate, Action restore, Action exit)
        {
            this.translate = translate;
            this.restore = restore;
            icon = (Icon)(applicationIcon ?? SystemIcons.Application).Clone();
            openItem = new ToolStripMenuItem();
            openItem.Click += delegate { if (!disposed) restore(); };
            exitItem = new ToolStripMenuItem();
            exitItem.Click += delegate { if (!disposed) exit(); };
            menu = new ContextMenuStrip();
            menu.Items.Add(openItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(exitItem);
            notifyIcon = new NotifyIcon { Text = "MacroDeck", Icon = icon, ContextMenuStrip = menu };
            notifyIcon.MouseClick += OnMouseClick;
            RefreshLanguage();
        }

        public void Show()
        {
            if (!disposed) notifyIcon.Visible = true;
        }

        public void RefreshLanguage()
        {
            if (disposed) return;
            openItem.Text = translate("Abrir MacroDeck");
            exitItem.Text = translate("Sair");
        }

        private void OnMouseClick(object sender, MouseEventArgs e)
        {
            if (!disposed && e.Button == MouseButtons.Left) restore();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            notifyIcon.Visible = false;
            notifyIcon.Dispose();
            menu.Dispose();
            icon.Dispose();
        }
    }
}
