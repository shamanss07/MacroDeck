using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MacroDeck
{
    internal sealed class ModernToolTip : Form
    {
        private readonly Label textLabel;
        private readonly Timer timer;
        private Control target;
        private Form owner;
        private string currentText;
        private Color background = Color.FromArgb(37, 42, 51);
        private Color foreground = Color.White;
        private Color border = Color.FromArgb(68, 76, 90);

        public ModernToolTip()
        {
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; TopMost = true;
            AutoScaleMode = AutoScaleMode.Dpi; BackColor = background; Padding = new Padding(1); DoubleBuffered = true;
            textLabel = new Label { AutoSize = false, Padding = new Padding(10, 7, 10, 7), Font = FontManager.Create(8f, FontStyle.Regular), BackColor = background, ForeColor = foreground };
            Controls.Add(textLabel);
            timer = new Timer { Interval = 950 }; timer.Tick += delegate { timer.Stop(); ShowTip(); };
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (Width < 2 || Height < 2) return;
            using (GraphicsPath path = RoundedControls.Path(new Rectangle(0, 0, Width, Height), 8))
            {
                Region previous = Region;
                Region = new Region(path);
                if (previous != null) previous.Dispose();
            }
        }

        public void SetToolTip(Control control, string text, Form ownerForm)
        {
            if (control == null) return;
            control.MouseEnter += delegate { target = control; owner = ownerForm; currentText = text; timer.Stop(); timer.Start(); };
            control.MouseLeave += delegate { if (target == control) HideTip(); };
            control.MouseDown += delegate { HideTip(); };
            control.Disposed += delegate { if (target == control) HideTip(); };
        }

        public void ApplyTheme(Color fill, Color text, Color edge)
        {
            background = fill; foreground = text; border = edge; BackColor = fill; textLabel.BackColor = fill; textLabel.ForeColor = text; Invalidate();
        }

        public void ApplyFont(Font font)
        {
            if (font == null) return;
            textLabel.Font = font;
            if (Visible && target != null) ShowTip();
        }

        private void ShowTip()
        {
            if (target == null || target.IsDisposed || String.IsNullOrEmpty(currentText) || owner == null || owner.IsDisposed) return;
            textLabel.Text = currentText;
            Size measured = TextRenderer.MeasureText(currentText, textLabel.Font, new Size(360, 500), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
            Size = new Size(Math.Min(380, measured.Width + 28), measured.Height + 23);
            textLabel.SetBounds(4, 4, Math.Max(0, Width - 8), Math.Max(0, Height - 8));
            Point cursor = Cursor.Position;
            Rectangle work = Screen.FromPoint(cursor).WorkingArea;
            int x = Math.Min(work.Right - Width - 8, Math.Max(work.Left + 8, cursor.X + 14));
            int y = Math.Min(work.Bottom - Height - 8, Math.Max(work.Top + 8, cursor.Y + 20));
            Location = new Point(x, y);
            if (!Visible) Show(owner); else BringToFront();
        }

        private void HideTip() { timer.Stop(); if (Visible) Hide(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle bounds = new Rectangle(1, 1, Math.Max(1, Width - 3), Math.Max(1, Height - 3));
            using (GraphicsPath path = RoundedControls.Path(bounds, 7))
            using (SolidBrush brush = new SolidBrush(background))
            using (Pen pen = new Pen(border)) { e.Graphics.FillPath(brush, path); e.Graphics.DrawPath(pen, path); }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(background);
        }

        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams { get { CreateParams cp = base.CreateParams; cp.ExStyle |= 0x00000080 | 0x08000000; return cp; } }
        protected override void Dispose(bool disposing) { if (disposing && timer != null) timer.Dispose(); base.Dispose(disposing); }
    }
}
