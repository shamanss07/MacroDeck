using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MacroDeck
{
    internal sealed class RoundedPanel : Panel
    {
        private Color borderColor;
        public int CornerRadius { get; set; }
        public Color BorderColor
        {
            get { return borderColor; }
            set
            {
                if (borderColor == value) return;
                borderColor = value;
                Invalidate(true);
                if (Parent != null) Parent.Invalidate(Bounds, true);
            }
        }

        public RoundedPanel()
        {
            CornerRadius = 14;
            BorderColor = Color.Transparent;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnBackColorChanged(EventArgs e)
        {
            base.OnBackColorChanged(e);
            Invalidate(true);
            if (Parent != null) Parent.Invalidate(Bounds, true);
        }

        protected override void OnParentBackColorChanged(EventArgs e)
        {
            base.OnParentBackColorChanged(e);
            Invalidate(true);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(Parent == null ? SystemColors.Control : Parent.BackColor);
            Rectangle bounds = new Rectangle(0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
            using (GraphicsPath path = RoundedControls.Path(bounds, CornerRadius))
            using (SolidBrush brush = new SolidBrush(BackColor)) e.Graphics.FillPath(brush, path);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (BorderColor.A == 0 || Width < 2 || Height < 2) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = RoundedControls.Path(bounds, CornerRadius))
            using (Pen pen = new Pen(BorderColor, 1f)) e.Graphics.DrawPath(pen, path);
        }
    }

    internal sealed class RoundedButton : Button
    {
        private bool hovered;
        private bool pressed;
        public int CornerRadius { get; set; }
        private string icon = "";
        public string Icon
        {
            get { return icon; }
            set { icon = value ?? ""; Invalidate(); }
        }

        public RoundedButton()
        {
            CornerRadius = 10;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnMouseEnter(System.EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(System.EventArgs e) { hovered = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(Parent == null ? SystemColors.Control : Parent.BackColor);
            Rectangle bounds = new Rectangle(0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
            Color fill = BackColor;
            if (!Enabled) fill = RoundedControls.Blend(fill, Parent == null ? Color.Gray : Parent.BackColor, 0.46f);
            else if (pressed) fill = RoundedControls.Blend(fill, Color.Black, 0.16f);
            else if (hovered) fill = RoundedControls.Blend(fill, Color.White, 0.08f);
            using (GraphicsPath path = RoundedControls.Path(bounds, CornerRadius))
            using (SolidBrush brush = new SolidBrush(fill)) e.Graphics.FillPath(brush, path);

            Color textColor = Enabled ? ForeColor : RoundedControls.Blend(ForeColor, BackColor, 0.42f);
            if (!String.IsNullOrEmpty(Icon))
            {
                float iconSize = Math.Max(10f, Math.Min(17f, Height * 0.42f));
                if (String.IsNullOrEmpty(Text))
                {
                    DrawIcon(e.Graphics, Icon, new RectangleF((Width - iconSize) / 2f, (Height - iconSize) / 2f, iconSize, iconSize), textColor);
                }
                else
                {
                    Size textSize = TextRenderer.MeasureText(Text, Font, new Size(Math.Max(1, Width - 24), Height), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                    float gap = 8f;
                    float groupWidth = iconSize + gap + textSize.Width;
                    float groupLeft = (Width - groupWidth) / 2f;
                    DrawIcon(e.Graphics, Icon, new RectangleF(groupLeft, (Height - iconSize) / 2f, iconSize, iconSize), textColor);
                    Rectangle textBounds = new Rectangle((int)(groupLeft + iconSize + gap), 1, Math.Max(0, (int)(Width - groupLeft - iconSize - gap - 6)), Height - 2);
                    TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, textColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
                }
            }
            else TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(8, 2, Math.Max(0, Width - 16), Math.Max(0, Height - 4)), textColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            if (Focused && ShowFocusCues)
            {
                Rectangle focus = Rectangle.Inflate(bounds, -5, -5);
                using (GraphicsPath path = RoundedControls.Path(focus, Math.Max(4, CornerRadius - 4)))
                using (Pen pen = new Pen(textColor, 1f)) { pen.DashStyle = DashStyle.Dot; e.Graphics.DrawPath(pen, path); }
            }
        }

        private static void DrawIcon(Graphics graphics, string name, RectangleF bounds, Color color)
        {
            float cx = bounds.Left + bounds.Width / 2f;
            float cy = bounds.Top + bounds.Height / 2f;
            float s = Math.Min(bounds.Width, bounds.Height);
            float r = s * 0.31f;
            using (Pen pen = new Pen(color, Math.Max(1.35f, s * 0.105f)))
            using (SolidBrush brush = new SolidBrush(color))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                if (name == "close")
                {
                    graphics.DrawLine(pen, cx - r, cy - r, cx + r, cy + r);
                    graphics.DrawLine(pen, cx + r, cy - r, cx - r, cy + r);
                }
                else if (name == "minimize") graphics.DrawLine(pen, cx - r, cy + 1, cx + r, cy + 1);
                else if (name == "maximize") graphics.DrawRectangle(pen, cx - r, cy - r, r * 2, r * 2);
                else if (name == "restore")
                {
                    graphics.DrawRectangle(pen, cx - r + 2, cy - r, r * 1.55f, r * 1.55f);
                    graphics.DrawLine(pen, cx - r + 2, cy - r + 3, cx - r + 2, cy - r + r * 1.55f);
                    graphics.DrawLine(pen, cx - r + 2, cy - r + r * 1.55f, cx + r, cy - r + r * 1.55f);
                }
                else if (name == "chevron")
                {
                    graphics.DrawLine(pen, cx - r * 0.65f, cy - r * 0.25f, cx, cy + r * 0.4f);
                    graphics.DrawLine(pen, cx, cy + r * 0.4f, cx + r * 0.65f, cy - r * 0.25f);
                }
                else if (name == "record") graphics.FillEllipse(brush, cx - r * 0.75f, cy - r * 0.75f, r * 1.5f, r * 1.5f);
                else if (name == "stop") graphics.FillRectangle(brush, cx - r * 0.58f, cy - r * 0.58f, r * 1.16f, r * 1.16f);
                else if (name == "play")
                {
                    PointF[] points = { new PointF(cx - r * 0.52f, cy - r * 0.82f), new PointF(cx + r * 0.88f, cy), new PointF(cx - r * 0.52f, cy + r * 0.82f) };
                    graphics.FillPolygon(brush, points);
                }
                else if (name == "refresh")
                {
                    RectangleF arc = new RectangleF(cx - r, cy - r, r * 2, r * 2);
                    graphics.DrawArc(pen, arc, 42, 285);
                    PointF[] arrow = { new PointF(cx + r * 0.52f, cy - r * 0.92f), new PointF(cx + r * 0.92f, cy - r * 0.86f), new PointF(cx + r * 0.82f, cy - r * 0.48f) };
                    graphics.FillPolygon(brush, arrow);
                }
                else if (name == "trash" || name == "trash-all")
                {
                    float left = cx - r * 0.72f;
                    float right = cx + r * 0.72f;
                    float top = cy - r * 0.48f;
                    float bottom = cy + r * 0.82f;
                    graphics.DrawLine(pen, left - 1, top, right + 1, top);
                    graphics.DrawLine(pen, cx - r * 0.32f, top - r * 0.28f, cx + r * 0.32f, top - r * 0.28f);
                    graphics.DrawLine(pen, cx - r * 0.22f, top - r * 0.28f, cx - r * 0.22f, top);
                    graphics.DrawLine(pen, cx + r * 0.22f, top - r * 0.28f, cx + r * 0.22f, top);
                    graphics.DrawLine(pen, left, top, left + r * 0.12f, bottom);
                    graphics.DrawLine(pen, right, top, right - r * 0.12f, bottom);
                    graphics.DrawLine(pen, left + r * 0.12f, bottom, right - r * 0.12f, bottom);
                    if (name == "trash-all")
                    {
                        graphics.DrawLine(pen, cx - r * 0.3f, top + r * 0.36f, cx + r * 0.3f, top + r * 0.36f);
                        graphics.DrawLine(pen, cx - r * 0.26f, top + r * 0.72f, cx + r * 0.26f, top + r * 0.72f);
                    }
                }
                else if (name == "sun")
                {
                    graphics.DrawEllipse(pen, cx - r * 0.48f, cy - r * 0.48f, r * 0.96f, r * 0.96f);
                    for (int i = 0; i < 8; i++)
                    {
                        double angle = Math.PI * i / 4.0;
                        float x1 = cx + (float)Math.Cos(angle) * r * 0.74f;
                        float y1 = cy + (float)Math.Sin(angle) * r * 0.74f;
                        float x2 = cx + (float)Math.Cos(angle) * r;
                        float y2 = cy + (float)Math.Sin(angle) * r;
                        graphics.DrawLine(pen, x1, y1, x2, y2);
                    }
                }
                else if (name == "moon")
                {
                    using (GraphicsPath moon = new GraphicsPath())
                    {
                        moon.AddBezier(cx + r * 0.55f, cy - r * 0.95f, cx - r * 0.85f, cy - r * 0.8f, cx - r * 0.85f, cy + r * 0.8f, cx + r * 0.55f, cy + r * 0.95f);
                        moon.AddBezier(cx + r * 0.55f, cy + r * 0.95f, cx - r * 0.05f, cy + r * 0.42f, cx - r * 0.05f, cy - r * 0.42f, cx + r * 0.55f, cy - r * 0.95f);
                        moon.CloseFigure();
                        graphics.FillPath(brush, moon);
                    }
                }
            }
        }

    }

    internal sealed class AnimatedToggle : Control
    {
        private bool isChecked;
        private bool hovered;
        private float position;
        private float targetPosition;
        private readonly Timer animation;
        private Color trackOff = Color.FromArgb(72, 79, 92);
        private Color trackOn = Color.FromArgb(87, 112, 224);
        private Color labelColor = Color.White;

        public event EventHandler CheckedChanged;
        public bool Checked
        {
            get { return isChecked; }
            set
            {
                if (isChecked == value) return;
                isChecked = value;
                targetPosition = value ? 1f : 0f;
                animation.Start();
                Invalidate();
                EventHandler changed = CheckedChanged;
                if (changed != null) changed(this, EventArgs.Empty);
            }
        }

        public AnimatedToggle()
        {
            Size = new Size(160, 30);
            Cursor = Cursors.Hand;
            TabStop = true;
            AccessibleRole = AccessibleRole.CheckButton;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            animation = new Timer { Interval = 8 };
            animation.Tick += delegate
            {
                float delta = targetPosition - position;
                if (Math.Abs(delta) < 0.06f) { position = targetPosition; animation.Stop(); }
                else position += delta * 0.38f;
                Invalidate();
            };
        }

        public void ApplyTheme(Color track, Color text)
        {
            trackOn = track;
            labelColor = text;
            Invalidate();
        }

        public void SetInitialChecked(bool value)
        {
            isChecked = value;
            position = targetPosition = value ? 1f : 0f;
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location)) Checked = !Checked;
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) { Checked = !Checked; e.Handled = true; }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent == null ? BackColor : Parent.BackColor);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle trackBounds = new Rectangle(1, (Height - 20) / 2, 38, 20);
            Color track = RoundedControls.Blend(trackOff, trackOn, position);
            if (hovered) track = RoundedControls.Blend(track, Color.White, 0.08f);
            using (GraphicsPath path = RoundedControls.Path(trackBounds, 10))
            using (SolidBrush brush = new SolidBrush(track)) e.Graphics.FillPath(brush, path);
            float knobX = 3 + position * 18;
            using (SolidBrush knob = new SolidBrush(Color.White)) e.Graphics.FillEllipse(knob, knobX, trackBounds.Y + 2, 16, 16);
            Rectangle textBounds = new Rectangle(48, 0, Math.Max(0, Width - 48), Height);
            TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, Enabled ? labelColor : RoundedControls.Blend(labelColor, BackColor, 0.5f), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (Focused && ShowFocusCues)
            {
                Rectangle focus = new Rectangle(0, 1, Math.Max(1, Width - 1), Height - 3);
                using (GraphicsPath path = RoundedControls.Path(focus, 7))
                using (Pen pen = new Pen(trackOn)) { pen.DashStyle = DashStyle.Dot; e.Graphics.DrawPath(pen, path); }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && animation != null) animation.Dispose();
            base.Dispose(disposing);
        }
    }

    internal sealed class ModernTextBox : UserControl
    {
        private readonly TextBox input;
        private Color borderColor = Color.FromArgb(72, 79, 92);
        private Color focusColor = Color.FromArgb(95, 125, 238);

        public override string Text
        {
            get { return input == null ? base.Text : input.Text; }
            set { if (input == null) base.Text = value; else input.Text = value ?? ""; }
        }
        public bool ReadOnly { get { return input.ReadOnly; } set { input.ReadOnly = value; } }

        public ModernTextBox()
        {
            Height = 32;
            BackColor = Color.FromArgb(41, 46, 56);
            ForeColor = Color.White;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            input = new TextBox { AutoSize = false, BorderStyle = BorderStyle.None, Multiline = false, Location = new Point(9, 2), Width = Math.Max(1, Width - 18), Height = Math.Max(1, Height - 4), BackColor = BackColor, ForeColor = ForeColor, Font = FontManager.Create(8.5f, FontStyle.Regular), TabStop = true };
            input.TextChanged += delegate { OnTextChanged(EventArgs.Empty); };
            input.GotFocus += delegate { Invalidate(); };
            input.LostFocus += delegate { Invalidate(); };
            Controls.Add(input);
            Resize += delegate { PositionInput(); };
            Click += delegate { input.Focus(); };
            PositionInput();
        }

        public void ApplyTheme(Color fill, Color border, Color text)
        {
            BackColor = fill; borderColor = border; ForeColor = text;
            input.BackColor = fill; input.ForeColor = text;
            Invalidate();
        }

        public void SelectAll() { input.SelectAll(); }

        private void PositionInput()
        {
            if (input == null) return;
            int editHeight = Math.Min(Math.Max(1, Height - 4), Math.Max(1, input.PreferredHeight));
            input.SetBounds(9, (Height - editHeight) / 2, Math.Max(1, Width - 18), editHeight);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(Parent == null ? BackColor : Parent.BackColor);
            Rectangle bounds = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
            using (GraphicsPath path = RoundedControls.Path(bounds, 7))
            using (SolidBrush brush = new SolidBrush(BackColor)) e.Graphics.FillPath(brush, path);
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            if (input != null) { input.Font = Font; PositionInput(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle bounds = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
            using (GraphicsPath path = RoundedControls.Path(bounds, 7))
            using (Pen pen = new Pen(input.Focused ? focusColor : borderColor)) e.Graphics.DrawPath(pen, path);
        }
    }

    internal static class RoundedControls
    {
        public static GraphicsPath Path(Rectangle bounds, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int d = System.Math.Min(radius * 2, System.Math.Min(bounds.Width, bounds.Height));
            if (d < 2) { path.AddRectangle(bounds); return path; }
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static Color Blend(Color a, Color b, float amount)
        {
            amount = System.Math.Max(0f, System.Math.Min(1f, amount));
            return Color.FromArgb(
                (int)(a.R * (1f - amount) + b.R * amount),
                (int)(a.G * (1f - amount) + b.G * amount),
                (int)(a.B * (1f - amount) + b.B * amount));
        }
    }
}
