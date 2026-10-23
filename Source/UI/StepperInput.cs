using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MacroDeck
{
    internal sealed class StepperInput : UserControl
    {
        private readonly TextBox valueBox;
        private readonly StepperArrowButton upButton;
        private readonly StepperArrowButton downButton;
        private readonly Font suffixFont;
        private int value;
        private int minimum;
        private int maximum = 9999;
        private string suffix = "";
        private Color borderColor = Color.FromArgb(72, 79, 92);

        public event EventHandler ValueChanged;
        public string Suffix
        {
            get { return suffix; }
            set { suffix = value ?? ""; PositionChildren(); Invalidate(); }
        }
        public int Minimum { get { return minimum; } set { minimum = value; if (this.value < minimum) Value = minimum; } }
        public int Maximum { get { return maximum; } set { maximum = value; if (this.value > maximum) Value = maximum; } }
        public int Value
        {
            get { return value; }
            set { int next = Math.Max(minimum, Math.Min(maximum, value)); if (this.value == next) { valueBox.Text = next.ToString(); return; } this.value = next; valueBox.Text = next.ToString(); EventHandler changed = ValueChanged; if (changed != null) changed(this, EventArgs.Empty); }
        }

        public StepperInput()
        {
            Height = 34; Width = 104; BackColor = Color.FromArgb(41, 46, 56); ForeColor = Color.White;
            suffixFont = FontManager.Create(8.5f, FontStyle.Regular);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            valueBox = new TextBox { AutoSize = false, BorderStyle = BorderStyle.None, Text = "0", TextAlign = HorizontalAlignment.Center, Location = new Point(7, 6), Width = 54, Height = 22, BackColor = BackColor, ForeColor = ForeColor, Font = FontManager.Create(9.5f, FontStyle.Regular) };
            upButton = new StepperArrowButton(true) { BackColor = Color.FromArgb(57, 63, 74), ForeColor = Color.White, TabStop = false, AccessibleName = "Aumentar valor" };
            downButton = new StepperArrowButton(false) { BackColor = Color.FromArgb(57, 63, 74), ForeColor = Color.White, TabStop = false, AccessibleName = "Diminuir valor" };
            Controls.Add(valueBox); Controls.Add(upButton); Controls.Add(downButton);
            upButton.StepRequested += delegate { Value = Value + 1; };
            downButton.StepRequested += delegate { Value = Value - 1; };
            valueBox.KeyPress += delegate(object sender, KeyPressEventArgs e) { if (!Char.IsControl(e.KeyChar) && !Char.IsDigit(e.KeyChar)) e.Handled = true; };
            valueBox.Validated += delegate { int parsed; Value = Int32.TryParse(valueBox.Text, out parsed) ? parsed : Value; };
            valueBox.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Up) { Value++; e.Handled = true; } else if (e.KeyCode == Keys.Down) { Value--; e.Handled = true; } else if (e.KeyCode == Keys.Enter) { int parsed; Value = Int32.TryParse(valueBox.Text, out parsed) ? parsed : Value; e.Handled = true; } };
            Resize += delegate { PositionChildren(); };
            PositionChildren();
        }

        public void ApplyTheme(Color fill, Color border, Color text, Color buttonFill)
        {
            BackColor = fill; ForeColor = text; borderColor = border; valueBox.BackColor = fill; valueBox.ForeColor = text;
            upButton.BackColor = buttonFill; downButton.BackColor = buttonFill;
            upButton.ForeColor = text; downButton.ForeColor = text;
            Invalidate();
        }

        private void PositionChildren()
        {
            int buttonWidth = 23;
            int arrowLeft = Width - buttonWidth - 3;
            int suffixWidth = String.IsNullOrEmpty(suffix) ? 0 : 26;
            int suffixLeft = arrowLeft - suffixWidth - (suffixWidth == 0 ? 0 : 5);
            int valueRight = suffixWidth == 0 ? arrowLeft - 6 : suffixLeft - 5;
            int editHeight = Math.Max(valueBox.PreferredHeight + 4, Font.Height + 7);
            valueBox.SetBounds(8, Math.Max(2, (Height - editHeight) / 2), Math.Max(20, valueRight - 8), editHeight);
            upButton.SetBounds(arrowLeft, 3, buttonWidth, Math.Max(11, (Height - 9) / 2));
            downButton.SetBounds(Width - buttonWidth - 3, upButton.Bottom + 2, buttonWidth, Math.Max(11, Height - upButton.Bottom - 5));
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(Parent == null ? BackColor : Parent.BackColor);
            using (GraphicsPath path = RoundedControls.Path(new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1)), 8))
            using (SolidBrush brush = new SolidBrush(BackColor)) e.Graphics.FillPath(brush, path);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle bounds = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
            using (GraphicsPath path = RoundedControls.Path(bounds, 8))
            using (Pen pen = new Pen(Focused ? Color.FromArgb(95, 125, 238) : borderColor, 1f)) e.Graphics.DrawPath(pen, path);
            if (!String.IsNullOrEmpty(suffix))
            {
                int arrowLeft = Width - 23 - 3;
                Rectangle suffixBounds = new Rectangle(arrowLeft - 31, 1, 26, Height - 2);
                TextRenderer.DrawText(e.Graphics, suffix, suffixFont, suffixBounds, ForeColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && suffixFont != null) suffixFont.Dispose();
            base.Dispose(disposing);
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            if (valueBox != null) { valueBox.Font = Font; PositionChildren(); }
        }
    }

    internal sealed class StepperArrowButton : Control
    {
        private readonly bool pointsUp;
        private bool hovered;
        private bool pressed;
        public event EventHandler StepRequested;

        public StepperArrowButton(bool up)
        {
            pointsUp = up;
            Size = new Size(23, 13);
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        }

        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                pressed = true;
                EventHandler step = StepRequested;
                if (step != null) step(this, EventArgs.Empty);
            }
            Invalidate(); base.OnMouseDown(e);
        }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(Parent == null ? BackColor : Parent.BackColor);
            Rectangle bounds = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
            Color fill = pressed ? RoundedControls.Blend(BackColor, Color.Black, 0.18f) : (hovered ? RoundedControls.Blend(BackColor, Color.White, 0.12f) : BackColor);
            using (GraphicsPath path = RoundedControls.Path(bounds, 5))
            using (SolidBrush brush = new SolidBrush(fill)) e.Graphics.FillPath(brush, path);
            int cx = Width / 2;
            int cy = Height / 2 + (pointsUp ? 1 : -1);
            using (Pen pen = new Pen(ForeColor, 1.7f))
            {
                pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round;
                if (pointsUp)
                {
                    e.Graphics.DrawLine(pen, cx - 4, cy + 2, cx, cy - 2);
                    e.Graphics.DrawLine(pen, cx, cy - 2, cx + 4, cy + 2);
                }
                else
                {
                    e.Graphics.DrawLine(pen, cx - 4, cy - 2, cx, cy + 2);
                    e.Graphics.DrawLine(pen, cx, cy + 2, cx + 4, cy - 2);
                }
            }
        }
    }
}
