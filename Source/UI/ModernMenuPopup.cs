using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MacroDeck
{
    // This is deliberately a child panel rather than a separate borderless Form.
    // A top-level popup can briefly show its native white surface before WinForms
    // paints it; a panel is painted by the already-visible MacroDeck window.
    internal sealed class ModernMenuPopup : Panel, IMessageFilter
    {
        private readonly RoundedButton optionsButton;
        private readonly RoundedButton aboutButton;
        private readonly int targetHeight = 98;
        private readonly Timer revealTimer;
        private readonly Stopwatch revealClock = new Stopwatch();
        private Rectangle toggleBounds;
        private bool messageFilterAdded;
        private Color edge;
        public event Action<int> ItemChosen;

        public ModernMenuPopup(ThemePalette palette, string language)
        {
            BackColor = palette.Card;
            ForeColor = palette.Text;
            Padding = new Padding(1);
            Size = new Size(196, targetHeight);
            Visible = false;
            DoubleBuffered = true;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            edge = palette.Border;
            revealTimer = new Timer { Interval = 15 };
            revealTimer.Tick += AnimateReveal;

            optionsButton = MakeItem(Localization.Translate(language, "Opções"));
            aboutButton = MakeItem(Localization.Translate(language, "Sobre MacroDeck"));
            optionsButton.SetBounds(6, 6, 182, 39);
            aboutButton.SetBounds(6, 51, 182, 39);
            optionsButton.Click += delegate { Choose(0); };
            aboutButton.Click += delegate { Choose(1); };
            Controls.Add(optionsButton);
            Controls.Add(aboutButton);
            ApplyTheme(palette);
        }

        private RoundedButton MakeItem(string text)
        {
            return new RoundedButton
            {
                Text = text,
                CornerRadius = 8,
                Font = FontManager.Create(9f, FontStyle.Regular),
                TextAlign = ContentAlignment.MiddleLeft
            };
        }

        private void Choose(int index)
        {
            Dismiss();
            Action<int> handler = ItemChosen;
            if (handler != null) handler(index);
        }

        public void Dismiss()
        {
            if (messageFilterAdded)
            {
                Application.RemoveMessageFilter(this);
                messageFilterAdded = false;
            }
            StopReveal();
            Visible = false;
        }

        public void ApplyTheme(ThemePalette palette)
        {
            BackColor = palette.Card;
            ForeColor = palette.Text;
            edge = palette.Border;
            optionsButton.Font = FontManager.Create(9f, FontStyle.Regular);
            aboutButton.Font = FontManager.Create(9f, FontStyle.Regular);
            optionsButton.BackColor = palette.Card;
            optionsButton.ForeColor = palette.Text;
            aboutButton.BackColor = palette.Card;
            aboutButton.ForeColor = palette.Text;
            Invalidate();
        }

        public void ShowAt(Form owner, Rectangle anchor)
        {
            if (Visible) { Dismiss(); return; }
            if (Parent == null)
            {
                owner.Controls.Add(this);
                owner.Controls.SetChildIndex(this, 0);
            }

            toggleBounds = anchor;
            Point anchorPoint = owner.PointToClient(anchor.Location);
            int x = Math.Max(4, Math.Min(anchorPoint.X + anchor.Width - Width, owner.ClientSize.Width - Width - 4));
            int y = Math.Max(4, Math.Min(anchorPoint.Y + anchor.Height + 7, owner.ClientSize.Height - targetHeight - 4));
            Location = new Point(x, y);
            Height = 2;
            BringToFront();
            Visible = true;
            Invalidate(true);
            Update();
            Application.AddMessageFilter(this);
            messageFilterAdded = true;
            revealClock.Restart();
            UiAnimationClock.Begin();
            revealTimer.Start();
        }

        private void AnimateReveal(object sender, EventArgs e)
        {
            float progress = Math.Min(1f, (float)(revealClock.Elapsed.TotalMilliseconds / 135.0));
            float eased = progress * progress * (3f - 2f * progress);
            Height = Math.Max(2, (int)(2 + (targetHeight - 2) * eased));
            if (progress < 1f) return;
            Height = targetHeight;
            StopReveal();
        }

        private void StopReveal()
        {
            if (!revealTimer.Enabled) return;
            revealTimer.Stop();
            revealClock.Stop();
            UiAnimationClock.End();
        }

        public bool PreFilterMessage(ref Message message)
        {
            if (!Visible || (message.Msg != 0x0201 && message.Msg != 0x0204 && message.Msg != 0x0207 && message.Msg != 0x00A1)) return false;
            Rectangle popupBounds = Parent == null ? Rectangle.Empty : Parent.RectangleToScreen(Bounds);
            Point point = Cursor.Position;
            if (!popupBounds.Contains(point) && !toggleBounds.Contains(point)) Dismiss();
            return false;
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (Width < 2 || Height < 2) return;
            using (GraphicsPath path = RoundedControls.Path(new Rectangle(0, 0, Width, Height), 10))
            {
                Region previous = Region;
                Region = new Region(path);
                if (previous != null) previous.Dispose();
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = RoundedControls.Path(new Rectangle(1, 1, Math.Max(1, Width - 3), Math.Max(1, Height - 3)), 9))
            using (SolidBrush brush = new SolidBrush(BackColor))
            using (Pen pen = new Pen(edge))
            {
                e.Graphics.FillPath(brush, path);
                e.Graphics.DrawPath(pen, path);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (messageFilterAdded) Application.RemoveMessageFilter(this);
                StopReveal();
                revealTimer.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
