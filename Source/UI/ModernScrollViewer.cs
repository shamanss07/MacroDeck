using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MacroDeck
{
    internal sealed class ModernScrollViewer : UserControl
    {
        private readonly BufferedPanel viewport;
        private readonly Timer scrollTimer;
        private Control content;
        private int scrollPosition;
        private int maximum;
        private bool dragging;
        private int dragOffset;
        private Color trackColor = Color.FromArgb(23, 27, 34);
        private Color thumbColor = Color.FromArgb(83, 93, 111);

        public ModernScrollViewer()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Color.FromArgb(19, 22, 28);
            viewport = new BufferedPanel { Location = Point.Empty, BackColor = BackColor, TabStop = true };
            Controls.Add(viewport);
            scrollTimer = new Timer { Interval = 16 };
            scrollTimer.Tick += delegate { scrollTimer.Stop(); ApplyScrollPosition(); };
            SizeChanged += delegate { LayoutViewport(); UpdateScrollRange(); };
            viewport.MouseWheel += ScrollWheel;
        }

        public Control Content
        {
            get { return content; }
            set
            {
                if (content != null) { UnhookWheel(content); viewport.Controls.Remove(content); content.SizeChanged -= ContentSizeChanged; }
                content = value;
                if (content != null)
                {
                    content.Dock = DockStyle.None; content.Location = Point.Empty; viewport.Controls.Add(content); content.SizeChanged += ContentSizeChanged;
                    HookWheel(content); content.BringToFront();
                }
                UpdateScrollRange();
            }
        }

        public void ApplyTheme(Color background, Color track, Color thumb)
        {
            BackColor = background; viewport.BackColor = background; trackColor = track; thumbColor = thumb; Invalidate();
        }

        private void LayoutViewport()
        {
            viewport.SetBounds(0, 0, Math.Max(0, ClientSize.Width - 12), ClientSize.Height);
            if (content != null) content.Width = viewport.ClientSize.Width;
        }
        private void ContentSizeChanged(object sender, EventArgs e) { UpdateScrollRange(); }
        private void UpdateScrollRange()
        {
            maximum = content == null ? 0 : Math.Max(0, content.Height - viewport.ClientSize.Height);
            scrollPosition = Math.Max(0, Math.Min(maximum, scrollPosition));
            pendingScroll = scrollPosition;
            ApplyScrollPosition();
            Invalidate();
        }

        private void HookWheel(Control control)
        {
            control.MouseWheel += ScrollWheel;
            control.ControlAdded += ChildAdded;
            foreach (Control child in control.Controls) HookWheel(child);
        }
        private void UnhookWheel(Control control)
        {
            control.MouseWheel -= ScrollWheel; control.ControlAdded -= ChildAdded;
            foreach (Control child in control.Controls) UnhookWheel(child);
        }
        private void ChildAdded(object sender, ControlEventArgs e) { HookWheel(e.Control); }
        private void ScrollWheel(object sender, MouseEventArgs e) { SetScroll(scrollPosition - e.Delta / 3); }
        private int pendingScroll;
        private void SetScroll(int value)
        {
            pendingScroll = Math.Max(0, Math.Min(maximum, value));
            scrollPosition = pendingScroll;
            if (!scrollTimer.Enabled) scrollTimer.Start();
            Invalidate();
        }
        private void ApplyScrollPosition()
        {
            if (content != null) content.SetBounds(0, -pendingScroll, viewport.ClientSize.Width, content.Height);
            viewport.Invalidate();
        }

        private Rectangle ThumbBounds()
        {
            if (maximum <= 0 || ClientSize.Height <= 0) return Rectangle.Empty;
            int trackHeight = ClientSize.Height - 8;
            int thumbHeight = Math.Max(34, trackHeight * viewport.ClientSize.Height / Math.Max(1, content.Height));
            thumbHeight = Math.Min(trackHeight, thumbHeight);
            int available = trackHeight - thumbHeight;
            int y = 4 + (maximum == 0 ? 0 : available * scrollPosition / maximum);
            return new Rectangle(ClientSize.Width - 9, y, 5, thumbHeight);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || e.X < ClientSize.Width - 13 || maximum <= 0) return;
            Rectangle thumb = ThumbBounds();
            if (thumb.Contains(e.Location)) { dragging = true; dragOffset = e.Y - thumb.Y; Capture = true; }
            else SetScroll(scrollPosition + (e.Y < thumb.Y ? -viewport.Height : viewport.Height));
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!dragging || maximum <= 0) return;
            Rectangle thumb = ThumbBounds(); int trackHeight = ClientSize.Height - 8; int available = Math.Max(1, trackHeight - thumb.Height);
            int y = Math.Max(4, Math.Min(4 + available, e.Y - dragOffset));
            SetScroll((y - 4) * maximum / available);
        }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); dragging = false; Capture = false; if (scrollTimer.Enabled) scrollTimer.Stop(); ApplyScrollPosition(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); if (maximum <= 0) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush track = new SolidBrush(trackColor)) e.Graphics.FillRectangle(track, ClientSize.Width - 12, 0, 12, ClientSize.Height);
            Rectangle thumb = ThumbBounds();
            using (GraphicsPath path = RoundedControls.Path(thumb, 3))
            using (SolidBrush brush = new SolidBrush(thumbColor)) e.Graphics.FillPath(brush, path);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && scrollTimer != null) scrollTimer.Dispose();
            base.Dispose(disposing);
        }
    }

    internal sealed class BufferedPanel : Panel
    {
        public BufferedPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
    }
}
