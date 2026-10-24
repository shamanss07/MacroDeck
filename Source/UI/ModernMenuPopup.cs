using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MacroDeck
{
    internal sealed class ModernMenuPopup : Form, IMessageFilter
    {
        private readonly RoundedButton optionsButton;
        private readonly RoundedButton aboutButton;
        private readonly Timer expandTimer;
        private readonly Stopwatch expandClock = new Stopwatch();
        private readonly int targetHeight = 98;
        private Point targetLocation;
        private int startTop;
        private Rectangle toggleBounds;
        private bool messageFilterAdded;
        private Color edge;
        public event Action<int> ItemChosen;

        public ModernMenuPopup(ThemePalette palette, string language)
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = palette.Card;
            ForeColor = palette.Text;
            Padding = new Padding(1);
            DoubleBuffered = true;
            edge = palette.Border;
            optionsButton = MakeItem(Localization.Translate(language, "Opções"));
            aboutButton = MakeItem(Localization.Translate(language, "Sobre MacroDeck"));
            optionsButton.SetBounds(6, 6, 182, 39);
            aboutButton.SetBounds(6, 51, 182, 39);
            optionsButton.Click += delegate { Choose(0); };
            aboutButton.Click += delegate { Choose(1); };
            Controls.Add(optionsButton); Controls.Add(aboutButton);
            Width = 196; Height = targetHeight;
            expandTimer = new Timer { Interval = 15 };
            expandTimer.Tick += delegate
            {
                float progress = Math.Min(1f, (float)(expandClock.Elapsed.TotalMilliseconds / 140.0));
                float eased = progress * progress * (3f - 2f * progress);
                Top = startTop + (int)((targetLocation.Y - startTop) * eased);
                if (progress >= 1f) { Location = targetLocation; expandTimer.Stop(); expandClock.Stop(); UiAnimationClock.End(); }
            };
            ApplyTheme(palette);
        }

        private RoundedButton MakeItem(string text)
        {
            return new RoundedButton { Text = text, CornerRadius = 8, Font = FontManager.Create(9f, FontStyle.Regular), TextAlign = ContentAlignment.MiddleLeft };
        }

        private void Choose(int index)
        {
            Dismiss();
            Action<int> handler = ItemChosen;
            if (handler != null) handler(index);
        }

        public void Dismiss()
        {
            if (messageFilterAdded) { Application.RemoveMessageFilter(this); messageFilterAdded = false; }
            bool wasAnimating = expandTimer.Enabled;
            expandTimer.Stop();
            expandClock.Stop();
            if (wasAnimating) UiAnimationClock.End();
            if (Visible) Hide();
        }

        public void ApplyTheme(ThemePalette palette)
        {
            BackColor = palette.Card; ForeColor = palette.Text; edge = palette.Border;
            optionsButton.Font = FontManager.Create(9f, FontStyle.Regular);
            aboutButton.Font = FontManager.Create(9f, FontStyle.Regular);
            optionsButton.BackColor = palette.Card; optionsButton.ForeColor = palette.Text;
            aboutButton.BackColor = palette.Card; aboutButton.ForeColor = palette.Text;
            Invalidate();
        }

        public void ShowAt(Form owner, Rectangle anchor)
        {
            if (Visible) { Dismiss(); return; }
            toggleBounds = anchor;
            Rectangle work = Screen.FromRectangle(anchor).WorkingArea;
            int x = Math.Max(work.Left + 4, Math.Min(anchor.Right - Width, work.Right - Width - 4));
            int y = anchor.Bottom + 7;
            y = Math.Max(work.Top + 4, Math.Min(y, work.Bottom - targetHeight - 4));
            targetLocation = new Point(x, y);
            startTop = Math.Max(work.Top + 4, y - 8);
            Size = new Size(196, targetHeight);
            Location = new Point(x, startTop);
            Show(owner);
            BringToFront();
            Application.AddMessageFilter(this);
            messageFilterAdded = true;
            expandClock.Restart();
            UiAnimationClock.Begin();
            expandTimer.Start();
        }

        public bool PreFilterMessage(ref Message message)
        {
            if (!Visible || message.Msg != 0x0201 && message.Msg != 0x0204 && message.Msg != 0x0207 && message.Msg != 0x00A1) return false;
            Point point = Cursor.Position;
            if (!Bounds.Contains(point) && !toggleBounds.Contains(point)) Dismiss();
            return false;
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (Width < 2 || Height < 2) return;
            using (GraphicsPath path = RoundedControls.Path(new Rectangle(0, 0, Width, Height), 10))
            {
                Region old = Region; Region = new Region(path); if (old != null) old.Dispose();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = RoundedControls.Path(new Rectangle(1, 1, Math.Max(1, Width - 3), Math.Max(1, Height - 3)), 9))
            using (SolidBrush brush = new SolidBrush(BackColor))
            using (Pen pen = new Pen(edge)) { e.Graphics.FillPath(brush, path); e.Graphics.DrawPath(pen, path); }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (messageFilterAdded) { Application.RemoveMessageFilter(this); messageFilterAdded = false; }
                if (expandTimer != null && expandTimer.Enabled) { expandTimer.Stop(); UiAnimationClock.End(); }
                if (expandTimer != null) expandTimer.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
