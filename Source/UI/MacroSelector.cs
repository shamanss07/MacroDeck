using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MacroDeck
{
    internal sealed class MacroSelector : Control
    {
        private int selectedIndex = -1;
        private bool hovered;
        private bool expanded;
        private ThemePalette palette;
        private MacroDropdownPopup popup;
        private int updateDepth;

        public List<MacroChoice> Items { get; private set; }
        public event EventHandler SelectedIndexChanged;

        public int SelectedIndex
        {
            get { return selectedIndex; }
            set
            {
                int next = value < 0 || value >= Items.Count ? -1 : value;
                if (selectedIndex == next) { Invalidate(); return; }
                selectedIndex = next;
                Invalidate();
                if (updateDepth == 0)
                {
                    EventHandler changed = SelectedIndexChanged;
                    if (changed != null) changed(this, EventArgs.Empty);
                }
            }
        }

        public object SelectedItem { get { return selectedIndex >= 0 && selectedIndex < Items.Count ? Items[selectedIndex] : null; } }

        public MacroSelector()
        {
            Items = new List<MacroChoice>();
            Size = new Size(270, 36);
            TabStop = true;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        }

        public void BeginUpdate() { updateDepth++; }
        public void EndUpdate() { if (updateDepth > 0) updateDepth--; Invalidate(); }
        public void ClearItems()
        {
            if (popup != null && !popup.IsDisposed) popup.Close();
            Items.Clear();
            SelectedIndex = -1;
        }

        public void ApplyTheme(ThemePalette current)
        {
            palette = current;
            BackColor = current.Input;
            Invalidate();
            if (popup != null && !popup.IsDisposed) popup.ApplyTheme(current);
        }

        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location)) TogglePopup();
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter || e.KeyCode == Keys.Down)
            {
                TogglePopup(); e.Handled = true;
            }
            else if (e.KeyCode == Keys.Escape && popup != null) { popup.Close(); e.Handled = true; }
        }

        private void TogglePopup()
        {
            if (expanded) { if (popup != null) popup.Close(); return; }
            Form owner = FindForm();
            if (owner == null || owner.IsDisposed) return;
            expanded = true;
            popup = new MacroDropdownPopup(Items, selectedIndex, palette);
            popup.ChoiceChosen += delegate(int index) { SelectedIndex = index; };
            popup.FormClosed += delegate { expanded = false; popup = null; Invalidate(); };
            Rectangle anchor = RectangleToScreen(ClientRectangle);
            popup.ShowAt(owner, anchor, Width);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            ThemePalette colors = palette ?? ThemePalette.Defaults(true);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle bounds = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
            Color fill = hovered ? RoundedControls.Blend(colors.Input, Color.White, 0.04f) : colors.Input;
            using (GraphicsPath path = RoundedControls.Path(bounds, 9))
            using (SolidBrush brush = new SolidBrush(fill))
            using (Pen pen = new Pen(Focused || expanded ? colors.Accent : colors.Border))
            {
                e.Graphics.FillPath(brush, path);
                e.Graphics.DrawPath(pen, path);
            }
            MacroChoice choice = SelectedItem as MacroChoice;
            string text = choice == null ? (Items.Count == 0 ? "Nenhum macro salvo" : "Selecione um macro") : choice.Name;
            Color textColor = choice == null ? colors.Muted : colors.Text;
            TextRenderer.DrawText(e.Graphics, text, Font, new Rectangle(12, 1, Math.Max(0, Width - 48), Height - 2), textColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            Point center = new Point(Width - 19, Height / 2);
            Point[] chevron = expanded
                ? new Point[] { new Point(center.X - 4, center.Y + 2), new Point(center.X, center.Y - 2), new Point(center.X + 4, center.Y + 2) }
                : new Point[] { new Point(center.X - 4, center.Y - 2), new Point(center.X, center.Y + 2), new Point(center.X + 4, center.Y - 2) };
            using (Pen pen = new Pen(colors.Muted, 1.7f)) { pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round; e.Graphics.DrawLines(pen, chevron); }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent == null ? BackColor : Parent.BackColor);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && popup != null && !popup.IsDisposed) popup.Close();
            base.Dispose(disposing);
        }
    }

    internal sealed class MacroDropdownPopup : Form
    {
        private readonly Timer expandTimer;
        private readonly Stopwatch expandClock = new Stopwatch();
        private readonly int targetHeight;
        private readonly ModernScrollViewer viewer;
        private readonly MacroDropdownList list;
        private Color edgeColor;
        private int selectedIndex;
        private Point targetLocation;
        private int startTop;
        public event Action<int> ChoiceChosen;

        public MacroDropdownPopup(IList<MacroChoice> choices, int currentSelection, ThemePalette palette)
        {
            selectedIndex = currentSelection;
            edgeColor = palette.Border;
            const int rowHeight = 36;
            targetHeight = Math.Min(276, Math.Max(56, choices.Count * rowHeight + 8));
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.Dpi;
            KeyPreview = true;
            BackColor = palette.Input;
            ForeColor = palette.Text;
            Font = FontManager.Create(8.5f, FontStyle.Regular);
            Padding = new Padding(1);
            DoubleBuffered = true;

            viewer = new ModernScrollViewer { Dock = DockStyle.Fill, BackColor = palette.Input, Margin = Padding.Empty };
            list = new MacroDropdownList(choices, selectedIndex, palette) { Dock = DockStyle.Top, Height = Math.Max(45, choices.Count * rowHeight + 6) };
            list.ChoiceClicked += delegate(int index)
            {
                Action<int> selected = ChoiceChosen;
                if (selected != null) selected(index);
                Close();
            };
            viewer.Content = list;
            Controls.Add(viewer);
            expandTimer = new Timer { Interval = 15 };
            expandTimer.Tick += delegate
            {
                float progress = Math.Min(1f, (float)(expandClock.Elapsed.TotalMilliseconds / 150.0));
                float eased = progress * progress * (3f - 2f * progress);
                Top = startTop + (int)((targetLocation.Y - startTop) * eased);
                if (progress >= 1f) { Location = targetLocation; expandTimer.Stop(); expandClock.Stop(); }
            };
            Deactivate += delegate { if (Visible) Close(); };
        }

        public void ApplyTheme(ThemePalette palette)
        {
            BackColor = palette.Input; ForeColor = palette.Text; edgeColor = palette.Border;
            viewer.ApplyTheme(palette.Input, RoundedControls.Blend(palette.Input, palette.Background, 0.5f), palette.Accent);
            list.ApplyTheme(palette);
            Invalidate();
        }

        public void ShowAt(Form owner, Rectangle anchor, int width)
        {
            Width = Math.Max(180, width);
            Rectangle work = Screen.FromRectangle(anchor).WorkingArea;
            int height = targetHeight;
            int x = Math.Max(work.Left + 4, Math.Min(anchor.Left, work.Right - Width - 4));
            int y = anchor.Bottom;
            if (y + height > work.Bottom && anchor.Top - height >= work.Top) y = anchor.Top - height;
            y = Math.Max(work.Top + 4, Math.Min(y, work.Bottom - height - 4));
            targetLocation = new Point(x, y);
            startTop = Math.Max(work.Top + 4, y - 7);
            Location = new Point(x, startTop);
            Height = targetHeight;
            Show(owner);
            BringToFront();
            expandClock.Restart();
            expandTimer.Start();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape) { Close(); e.Handled = true; }
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (Width > 2 && Height > 2)
            {
                using (GraphicsPath path = RoundedControls.Path(new Rectangle(0, 0, Width, Height), 10))
                {
                    Region old = Region;
                    Region = new Region(path);
                    if (old != null) old.Dispose();
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = RoundedControls.Path(new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1)), 10))
            using (Pen pen = new Pen(edgeColor)) e.Graphics.DrawPath(pen, path);
        }

        protected override bool ShowWithoutActivation { get { return false; } }
        protected override void Dispose(bool disposing)
        {
            if (disposing && expandTimer != null) expandTimer.Dispose();
            base.Dispose(disposing);
        }
    }

    internal sealed class MacroDropdownList : Control
    {
        private readonly IList<MacroChoice> choices;
        private readonly int selectedIndex;
        private int hoveredIndex = -1;
        private ThemePalette palette;
        public event Action<int> ChoiceClicked;

        public MacroDropdownList(IList<MacroChoice> items, int currentSelection, ThemePalette colors)
        {
            choices = items; selectedIndex = currentSelection; palette = colors;
            BackColor = colors.Input;
            Cursor = items.Count == 0 ? Cursors.Default : Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        public void ApplyTheme(ThemePalette colors) { palette = colors; BackColor = colors.Input; Invalidate(); }
        protected override void OnMouseMove(MouseEventArgs e) { int next = choices.Count == 0 ? -1 : Math.Max(0, Math.Min(choices.Count - 1, e.Y / 36)); if (next != hoveredIndex) { hoveredIndex = next; Invalidate(); } base.OnMouseMove(e); }
        protected override void OnMouseLeave(EventArgs e) { hoveredIndex = -1; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left || choices.Count == 0) return;
            int index = e.Y / 36;
            if (index < 0 || index >= choices.Count) return;
            Action<int> handler = ChoiceClicked;
            if (handler != null) handler(index);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(palette.Input);
            if (choices.Count == 0)
            {
                TextRenderer.DrawText(e.Graphics, "Nenhum macro salvo", Font, new Rectangle(12, 0, Math.Max(0, Width - 24), 45), palette.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                return;
            }
            int first = Math.Max(0, e.ClipRectangle.Top / 36);
            int last = Math.Min(choices.Count - 1, Math.Max(first, e.ClipRectangle.Bottom / 36));
            for (int i = first; i <= last; i++)
            {
                Rectangle row = new Rectangle(4, i * 36 + 2, Math.Max(1, Width - 8), 32);
                if (i == selectedIndex || i == hoveredIndex)
                {
                    Color fill = i == selectedIndex ? RoundedControls.Blend(palette.Accent, palette.Input, 0.35f) : RoundedControls.Blend(palette.Input, Color.White, 0.07f);
                    using (GraphicsPath path = RoundedControls.Path(row, 7))
                    using (SolidBrush brush = new SolidBrush(fill)) e.Graphics.FillPath(brush, path);
                }
                TextRenderer.DrawText(e.Graphics, choices[i].Name, Font, new Rectangle(14, i * 36, Math.Max(0, Width - 28), 36), palette.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
        }
    }
}
