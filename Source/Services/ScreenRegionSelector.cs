using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MacroDeck
{
    internal sealed class ScreenRegionSelector : Form
    {
        private Point anchor;
        private Point current;
        private bool selecting;
        private readonly string language;
        private readonly Bitmap desktopImage;

        public Rectangle SelectedRegion { get; private set; }

        public ScreenRegionSelector(string prompt, string language)
        {
            this.language = language;
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = SystemInformation.VirtualScreen;
            ShowInTaskbar = true;
            TopMost = true;
            KeyPreview = true;
            BackColor = Color.Black;
            Cursor = Cursors.Cross;
            DoubleBuffered = true;
            Text = prompt ?? "";
            desktopImage = new Bitmap(Width, Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(desktopImage))
                graphics.CopyFromScreen(Left, Top, 0, 0, Size, CopyPixelOperation.SourceCopy);
            KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); }
            };
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            anchor = PointToScreen(e.Location);
            current = anchor;
            selecting = true;
            Capture = true;
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!selecting) return;
            current = PointToScreen(e.Location);
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!selecting || e.Button != MouseButtons.Left) return;
            current = PointToScreen(e.Location);
            selecting = false;
            Capture = false;
            Rectangle selected = Normalize(anchor, current);
            if (selected.Width >= 6 && selected.Height >= 6)
            {
                SelectedRegion = selected;
                DialogResult = DialogResult.OK;
            }
            else DialogResult = DialogResult.Cancel;
            Close();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.DrawImageUnscaled(desktopImage, Point.Empty);
            using (SolidBrush dim = new SolidBrush(Color.FromArgb(90, Color.Black))) e.Graphics.FillRectangle(dim, ClientRectangle);
            string prompt = Text + "    Esc - " + Localization.Translate(language, "Cancelar");
            using (Font font = new Font("Segoe UI", 12f, FontStyle.Bold))
            using (SolidBrush brush = new SolidBrush(Color.White))
            using (SolidBrush backdrop = new SolidBrush(Color.FromArgb(210, 15, 19, 27)))
            {
                Size measured = TextRenderer.MeasureText(prompt, font, new Size(Math.Max(1, ClientSize.Width - 32), 42), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                Rectangle banner = new Rectangle(16, 16, Math.Min(ClientSize.Width - 32, measured.Width + 24), 42);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath path = RoundedControls.Path(banner, 8)) e.Graphics.FillPath(backdrop, path);
                TextRenderer.DrawText(e.Graphics, prompt, font, Rectangle.Inflate(banner, -12, 0), Color.White, TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }

            if (selecting)
            {
                Rectangle screenRect = Normalize(anchor, current);
                screenRect.Offset(-Bounds.Left, -Bounds.Top);
                e.Graphics.DrawImage(desktopImage, screenRect, screenRect, GraphicsUnit.Pixel);
                using (Pen pen = new Pen(Color.FromArgb(95, 132, 255), 2f))
                using (SolidBrush fill = new SolidBrush(Color.FromArgb(45, 95, 132, 255)))
                {
                    e.Graphics.FillRectangle(fill, screenRect);
                    e.Graphics.DrawRectangle(pen, screenRect);
                }
            }
        }

        private static Rectangle Normalize(Point first, Point second)
        {
            return Rectangle.FromLTRB(Math.Min(first.X, second.X), Math.Min(first.Y, second.Y),
                Math.Max(first.X, second.X), Math.Max(first.Y, second.Y));
        }

        public Bitmap CopySelection()
        {
            Rectangle local = SelectedRegion;
            local.Offset(-Left, -Top);
            return desktopImage.Clone(local, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && desktopImage != null) desktopImage.Dispose();
            base.Dispose(disposing);
        }
    }
}
