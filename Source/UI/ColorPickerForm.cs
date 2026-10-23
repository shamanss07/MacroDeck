using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MacroDeck
{
    internal sealed class ColorPickerForm : ChromeForm
    {
        private readonly ThemePalette palette;
        private readonly ColorHsvSlider[] sliders = new ColorHsvSlider[3];
        private readonly Label preview;
        private readonly TextBox hexBox;
        private Color selected;
        private bool updating;
        private float hue;
        private int saturation;
        private int brightness;
        public Color SelectedColor { get { return selected; } }

        public static Color? PickColor(IWin32Window owner, Color initial, ThemePalette palette)
        {
            using (ColorPickerForm dialog = new ColorPickerForm(initial, palette))
                return dialog.ShowDialog(owner) == DialogResult.OK ? (Color?)dialog.SelectedColor : null;
        }

        private ColorPickerForm(Color initial, ThemePalette currentPalette) : base(false)
        {
            palette = currentPalette; selected = initial;
            Text = "Escolher cor"; ClientSize = new Size(540, 516); BackColor = palette.Background; ForeColor = palette.Text;
            MinimumSize = new Size(480, 478); MaximizeBox = false; MinimizeBox = false;
            ApplyChromeTheme(palette.Background, palette.Text, palette.Card);

            Label title = new Label { Text = "Personalizar cor", AutoSize = true, Location = new Point(24, 20), Font = FontManager.Create(18f, FontStyle.Bold), ForeColor = palette.Text };
            Label subtitle = new Label { Text = "Digite um valor hexadecimal ou ajuste matiz, saturação e brilho.", AutoSize = true, Location = new Point(26, 55), Font = FontManager.Create(8.5f, FontStyle.Regular), ForeColor = palette.Muted };
            RoundedPanel card = new RoundedPanel { Location = new Point(20, 88), Size = new Size(500, 310), BackColor = palette.Card, BorderColor = palette.Border, CornerRadius = 15 };
            ContentPanel.Controls.Add(title); ContentPanel.Controls.Add(subtitle); ContentPanel.Controls.Add(card);

            preview = new Label { Text = "Prévia", TextAlign = ContentAlignment.MiddleCenter, Location = new Point(20, 18), Size = new Size(220, 62), Font = FontManager.Create(10f, FontStyle.Bold), BorderStyle = BorderStyle.None };
            card.Controls.Add(preview);
            Label hexLabel = new Label { Text = "HEX", AutoSize = true, Location = new Point(260, 22), ForeColor = palette.Muted, Font = FontManager.Create(8f, FontStyle.Bold) };
            hexBox = new TextBox { Location = new Point(258, 43), Size = new Size(216, 30), Font = FontManager.Create(10f, FontStyle.Regular), BorderStyle = BorderStyle.FixedSingle, MaxLength = 7, BackColor = palette.Input, ForeColor = palette.Text, Text = ThemePalette.Hex(initial) };
            card.Controls.Add(hexLabel); card.Controls.Add(hexBox);

            string[] labels = { "Matiz", "Saturação", "Brilho" };
            for (int i = 0; i < 3; i++)
            {
                int y = 99 + i * 51;
                Label channelLabel = new Label { Text = labels[i], AutoSize = true, Location = new Point(22, y + 4), Font = FontManager.Create(8f, FontStyle.Bold), ForeColor = palette.Text };
                sliders[i] = new ColorHsvSlider(i) { Location = new Point(112, y), Size = new Size(358, 26), CurrentColor = selected };
                sliders[i].ValueChanged += SliderChanged;
                card.Controls.Add(channelLabel); card.Controls.Add(sliders[i]);
            }

            Label presetsLabel = new Label { Text = "Cores rápidas", AutoSize = true, Location = new Point(22, 259), Font = FontManager.Create(8f, FontStyle.Bold), ForeColor = palette.Muted };
            card.Controls.Add(presetsLabel);
            Color[] presets = { Color.FromArgb(87,112,224), Color.FromArgb(34,197,154), Color.FromArgb(244,114,92), Color.FromArgb(247,185,85), Color.FromArgb(171,115,234), Color.FromArgb(239,68,110), Color.FromArgb(31,39,51), Color.White };
            for (int i = 0; i < presets.Length; i++)
            {
                int index = i;
                RoundedButton swatch = new RoundedButton { Location = new Point(22 + i * 54, 278), Size = new Size(40, 22), CornerRadius = 7, BackColor = presets[i], AccessibleName = ThemePalette.Hex(presets[i]), Text = "" };
                swatch.Click += delegate { SetSelected(presets[index]); };
                card.Controls.Add(swatch);
            }

            RoundedButton cancel = new RoundedButton { Text = "Cancelar", Location = new Point(316, 416), Size = new Size(96, 38), CornerRadius = 10, BackColor = palette.Input, ForeColor = palette.Text, Font = FontManager.Create(8.5f, FontStyle.Bold), DialogResult = DialogResult.Cancel };
            RoundedButton apply = new RoundedButton { Text = "Usar cor", Location = new Point(424, 416), Size = new Size(96, 38), CornerRadius = 10, BackColor = palette.Accent, ForeColor = Color.White, Font = FontManager.Create(8.5f, FontStyle.Bold), DialogResult = DialogResult.OK };
            ContentPanel.Controls.Add(cancel); ContentPanel.Controls.Add(apply);
            AcceptButton = apply; CancelButton = cancel;
            hexBox.TextChanged += HexChanged;
            hexBox.Validated += delegate { ParseHex(); };
            UpdateColorControls();
        }

        private void SliderChanged(object sender, EventArgs e)
        {
            ColorHsvSlider slider = (ColorHsvSlider)sender;
            if (slider.Channel == 0) hue = slider.ChannelValue;
            else if (slider.Channel == 1) saturation = slider.ChannelValue;
            else brightness = slider.ChannelValue;
            selected = ColorHsvSlider.FromHsv(hue, saturation / 100f, brightness / 100f);
            UpdateColorControls(false);
        }

        private void HexChanged(object sender, EventArgs e)
        {
            if (updating || hexBox.Text.Length < 7) return;
            ParseHex();
        }

        private void ParseHex()
        {
            string value = hexBox.Text.Trim(); if (!value.StartsWith("#")) value = "#" + value;
            try { if (value.Length == 7) SetSelected(ColorTranslator.FromHtml(value)); } catch { }
        }

        private void SetSelected(Color color) { selected = color; UpdateColorControls(); }

        private void UpdateColorControls() { UpdateColorControls(true); }

        private void UpdateColorControls(bool deriveHsv)
        {
            updating = true;
            preview.BackColor = selected; preview.ForeColor = (selected.R * 299 + selected.G * 587 + selected.B * 114) / 1000 > 145 ? Color.FromArgb(24, 28, 34) : Color.White;
            hexBox.Text = ThemePalette.Hex(selected);
            if (deriveHsv)
            {
                float nextHue; float nextSaturation; float nextBrightness;
                ColorHsvSlider.ToHsv(selected, out nextHue, out nextSaturation, out nextBrightness);
                if (nextSaturation > 0.001f) hue = nextHue;
                saturation = (int)Math.Round(nextSaturation * 100f);
                brightness = (int)Math.Round(nextBrightness * 100f);
            }
            sliders[0].Value = (int)Math.Round(hue);
            sliders[1].Value = saturation;
            sliders[2].Value = brightness;
            for (int i = 0; i < sliders.Length; i++) { sliders[i].CurrentColor = selected; sliders[i].Hue = hue; }
            updating = false;
        }
    }

    internal sealed class ColorHsvSlider : Control
    {
        private readonly int channel;
        private bool dragging;
        private Color currentColor;
        private int value;
        private float hue;
        public int Channel { get { return channel; } }
        public int ChannelValue { get { return value; } }
        public Color CurrentColor { get { return currentColor; } set { currentColor = value; Invalidate(); } }
        public float Hue { get { return hue; } set { hue = value; Invalidate(); } }
        public int Value { get { return value; } set { int max = channel == 0 ? 359 : 100; this.value = Math.Max(0, Math.Min(max, value)); Invalidate(); } }
        public event EventHandler ValueChanged;

        public ColorHsvSlider(int hsvChannel)
        {
            channel = hsvChannel; SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); Cursor = Cursors.Hand;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle track = new Rectangle(0, 8, Math.Max(1, Width - 12), 10);
            float unusedHue; float currentSaturation; float currentBrightness;
            ToHsv(currentColor, out unusedHue, out currentSaturation, out currentBrightness);
            Color start; Color end;
            if (channel == 0)
            {
                Color[] rainbow = { Color.Red, Color.Yellow, Color.Lime, Color.Cyan, Color.Blue, Color.Magenta, Color.Red };
                ColorBlend blend = new ColorBlend(); blend.Colors = rainbow; blend.Positions = new float[] { 0f, .1667f, .3333f, .5f, .6667f, .8333f, 1f };
                using (GraphicsPath path = RoundedControls.Path(track, 5))
                using (LinearGradientBrush gradient = new LinearGradientBrush(track, Color.Red, Color.Red, LinearGradientMode.Horizontal))
                using (Pen pen = new Pen(Color.FromArgb(100, 255, 255, 255))) { gradient.InterpolationColors = blend; e.Graphics.FillPath(gradient, path); e.Graphics.DrawPath(pen, path); }
            }
            else
            {
                start = channel == 1 ? FromHsv(hue, 0f, Math.Max(0.65f, currentBrightness)) : Color.Black;
                end = channel == 1 ? FromHsv(hue, 1f, Math.Max(0.65f, currentBrightness)) : FromHsv(hue, currentSaturation, 1f);
                using (GraphicsPath path = RoundedControls.Path(track, 5))
                using (LinearGradientBrush gradient = new LinearGradientBrush(track, start, end, LinearGradientMode.Horizontal))
                using (Pen pen = new Pen(Color.FromArgb(100, 255, 255, 255))) { e.Graphics.FillPath(gradient, path); e.Graphics.DrawPath(pen, path); }
            }
            int max = channel == 0 ? 359 : 100;
            int knobX = track.Left + (track.Width - 1) * ChannelValue / max;
            Rectangle knob = new Rectangle(knobX - 7, 3, 14, 20);
            using (SolidBrush brush = new SolidBrush(Color.White)) using (Pen pen = new Pen(Color.FromArgb(38, 44, 54), 2f)) { e.Graphics.FillEllipse(brush, knob); e.Graphics.DrawEllipse(pen, knob); }
        }

        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) { dragging = true; Capture = true; SetAt(e.X); } }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (dragging) SetAt(e.X); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); dragging = false; Capture = false; }
        private void SetAt(int x)
        {
            int max = channel == 0 ? 359 : 100;
            int next = Math.Max(0, Math.Min(max, x * max / Math.Max(1, Width - 12)));
            if (next == value) return;
            value = next; Invalidate(); EventHandler handler = ValueChanged; if (handler != null) handler(this, EventArgs.Empty);
        }

        public static Color FromHsv(float hue, float saturation, float value)
        {
            hue = (hue % 360f + 360f) % 360f;
            saturation = Math.Max(0f, Math.Min(1f, saturation)); value = Math.Max(0f, Math.Min(1f, value));
            int hi = (int)Math.Floor(hue / 60f) % 6;
            float f = hue / 60f - (float)Math.Floor(hue / 60f);
            float p = value * (1f - saturation), q = value * (1f - f * saturation), t = value * (1f - (1f - f) * saturation);
            float r = value, g = value, b = value;
            switch (hi) { case 0: g = t; b = p; break; case 1: r = q; b = p; break; case 2: r = p; b = t; break; case 3: r = p; g = q; break; case 4: r = t; g = p; break; default: g = p; b = q; break; }
            return Color.FromArgb((int)Math.Round(r * 255f), (int)Math.Round(g * 255f), (int)Math.Round(b * 255f));
        }

        public static void ToHsv(Color color, out float hue, out float saturation, out float value)
        {
            float r = color.R / 255f, g = color.G / 255f, b = color.B / 255f;
            float max = Math.Max(r, Math.Max(g, b)); float min = Math.Min(r, Math.Min(g, b)); float delta = max - min;
            value = max; saturation = max <= 0f ? 0f : delta / max; hue = 0f;
            if (delta <= 0f) return;
            if (max == r) hue = 60f * (((g - b) / delta) % 6f);
            else if (max == g) hue = 60f * (((b - r) / delta) + 2f);
            else hue = 60f * (((r - g) / delta) + 4f);
            if (hue < 0f) hue += 360f;
        }
    }
}
