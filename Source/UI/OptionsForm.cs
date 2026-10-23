using System;
using System.Drawing;
using System.Windows.Forms;

namespace MacroDeck
{
    internal sealed class OptionsForm : ChromeForm
    {
        private readonly AppSettings settings;
        private ThemePalette palette;
        private readonly RoundedPanel colorsCard;
        private readonly RoundedButton[] swatches = new RoundedButton[4];
        private readonly Color[] selected = new Color[4];
        private bool resetColors;
        private readonly MacroSelector fontSelector;

        public string BackgroundHex { get; private set; }
        public string CardHex { get; private set; }
        public string TextHex { get; private set; }
        public string AccentHex { get; private set; }
        public string SelectedFontFamily { get; private set; }

        public OptionsForm(AppSettings currentSettings, ThemePalette currentPalette) : base(false)
        {
            settings = currentSettings;
            palette = currentPalette;
            selected[0] = palette.Background; selected[1] = palette.Card;
            selected[2] = palette.Text; selected[3] = palette.Accent;

            Text = "Opções · MacroDeck";
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false; MaximizeBox = false; MinimizeBox = false;
            ClientSize = new Size(560, 510); BackColor = palette.Background; ForeColor = palette.Text;
            Font = FontManager.Create(9f, FontStyle.Regular);
            ApplyChromeTheme(palette.Background, palette.Text, palette.Card);

            Label title = new Label { Text = "Opções", AutoSize = true, Location = new Point(26, 22), Font = FontManager.Create(20f, FontStyle.Bold), ForeColor = palette.Text };
            Label subtitle = new Label { Text = "Personalize as cores do tema atual.", AutoSize = true, Location = new Point(28, 58), ForeColor = palette.Muted };
            ContentPanel.Controls.Add(title); ContentPanel.Controls.Add(subtitle);

            colorsCard = new RoundedPanel { Location = new Point(22, 96), Size = new Size(516, 276), BackColor = palette.Card, BorderColor = palette.Border, CornerRadius = 16 };
            ContentPanel.Controls.Add(colorsCard);
            string[] labels = { "Fundo do aplicativo", "Caixas e cartões", "Textos", "Cor de destaque" };
            for (int i = 0; i < labels.Length; i++)
            {
                int y = 18 + i * 50;
                Label label = new Label { Text = labels[i], AutoSize = true, Location = new Point(20, y + 8), ForeColor = palette.Text };
                swatches[i] = new RoundedButton { Text = ThemePalette.Hex(selected[i]), Location = new Point(354, y), Size = new Size(138, 34), CornerRadius = 9, Tag = "secondary", Font = FontManager.Create(8.5f, FontStyle.Bold) };
                swatches[i].Click += ChooseColor;
                colorsCard.Controls.Add(label); colorsCard.Controls.Add(swatches[i]);
            }

            Label fontLabel = new Label { Text = "Fonte do aplicativo", AutoSize = true, Location = new Point(20, 229), ForeColor = palette.Text };
            fontSelector = new MacroSelector { Location = new Point(242, 218), Size = new Size(250, 36), Font = FontManager.Create(9f, FontStyle.Regular) };
            fontSelector.ApplyTheme(palette);
            fontSelector.BeginUpdate();
            string[] installedFonts = FontManager.InstalledFamilies();
            int selectedFontIndex = 0;
            for (int i = 0; i < installedFonts.Length; i++)
            {
                fontSelector.Items.Add(new MacroChoice { Name = installedFonts[i], Path = "" });
                if (String.Equals(installedFonts[i], currentSettings.FontFamily, StringComparison.OrdinalIgnoreCase)) selectedFontIndex = i;
            }
            fontSelector.SelectedIndex = selectedFontIndex;
            fontSelector.EndUpdate();
            fontSelector.SelectedIndexChanged += delegate { CaptureSelectedFont(); };
            CaptureSelectedFont();
            colorsCard.Controls.Add(fontLabel); colorsCard.Controls.Add(fontSelector);

            RoundedButton reset = new RoundedButton { Text = "Restaurar cores padrão", Location = new Point(24, 396), Size = new Size(190, 38), CornerRadius = 10, Tag = "secondary", Font = FontManager.Create(8.5f, FontStyle.Bold) };
            reset.Click += delegate
            {
                resetColors = true;
                ThemePalette defaults = ThemePalette.Defaults(settings.DarkTheme);
                selected[0] = defaults.Background; selected[1] = defaults.Card;
                selected[2] = defaults.Text; selected[3] = defaults.Accent;
                UpdateSwatches();
            };
            RoundedButton cancel = new RoundedButton { Text = "Cancelar", Location = new Point(316, 396), Size = new Size(96, 38), CornerRadius = 10, Tag = "secondary", Font = FontManager.Create(8.5f, FontStyle.Bold), DialogResult = DialogResult.Cancel };
            RoundedButton save = new RoundedButton { Text = "Salvar", Location = new Point(424, 396), Size = new Size(112, 38), CornerRadius = 10, Tag = "primary", Font = FontManager.Create(8.5f, FontStyle.Bold), DialogResult = DialogResult.OK };
            save.Click += delegate
            {
                CaptureSelectedFont();
                BackgroundHex = resetColors ? "" : ThemePalette.Hex(selected[0]);
                CardHex = resetColors ? "" : ThemePalette.Hex(selected[1]);
                TextHex = resetColors ? "" : ThemePalette.Hex(selected[2]);
                AccentHex = resetColors ? "" : ThemePalette.Hex(selected[3]);
            };
            ContentPanel.Controls.Add(reset); ContentPanel.Controls.Add(cancel); ContentPanel.Controls.Add(save);
            AcceptButton = save; CancelButton = cancel;
            ApplyTheme();
        }

        public void RefreshPalette(ThemePalette currentPalette)
        {
            palette = currentPalette;
            selected[0] = palette.Background; selected[1] = palette.Card;
            selected[2] = palette.Text; selected[3] = palette.Accent;
            resetColors = false;
            string wantedFont = String.IsNullOrWhiteSpace(settings.FontFamily) ? "Montserrat" : settings.FontFamily;
            fontSelector.BeginUpdate();
            fontSelector.SelectedIndex = 0;
            for (int i = 0; i < fontSelector.Items.Count; i++)
            {
                MacroChoice choice = fontSelector.Items[i];
                if (String.Equals(choice.Name, wantedFont, StringComparison.OrdinalIgnoreCase)) { fontSelector.SelectedIndex = i; break; }
            }
            fontSelector.EndUpdate();
            CaptureSelectedFont();
            ApplyTheme();
        }

        private void ChooseColor(object sender, EventArgs e)
        {
            int index = Array.IndexOf(swatches, sender);
            if (index < 0) return;
            Color? result = ColorPickerForm.PickColor(this, selected[index], palette);
            if (!result.HasValue) return;
            selected[index] = result.Value; resetColors = false; UpdateSwatches();
        }

        private void UpdateSwatches()
        {
            for (int i = 0; i < swatches.Length; i++)
            {
                swatches[i].Text = ThemePalette.Hex(selected[i]);
                swatches[i].BackColor = selected[i];
                swatches[i].ForeColor = IsLight(selected[i]) ? Color.FromArgb(25, 28, 34) : Color.White;
            }
        }

        private void CaptureSelectedFont()
        {
            MacroChoice choice = fontSelector == null ? null : fontSelector.SelectedItem as MacroChoice;
            if (choice != null) SelectedFontFamily = choice.Name;
        }

        private void ApplyTheme()
        {
            BackColor = palette.Background; ForeColor = palette.Text;
            colorsCard.BackColor = palette.Card; colorsCard.BorderColor = palette.Border;
            if (fontSelector != null) fontSelector.ApplyTheme(palette);
            UpdateSwatches();
            for (int i = 0; i < colorsCard.Controls.Count; i++)
            {
                Label label = colorsCard.Controls[i] as Label;
                if (label != null) label.ForeColor = palette.Text;
                RoundedButton button = colorsCard.Controls[i] as RoundedButton;
                if (button != null && button.BackColor == Color.Empty) button.BackColor = palette.Input;
            }
            foreach (Control c in ContentPanel.Controls)
            {
                Label label = c as Label;
                if (label != null) label.ForeColor = label.Top < 50 ? palette.Text : palette.Muted;
                RoundedButton button = c as RoundedButton;
                if (button != null) { button.BackColor = button.Tag != null && (string)button.Tag == "primary" ? palette.Accent : palette.Input; button.ForeColor = palette.Text; }
            }
            for (int i = 0; i < swatches.Length; i++) swatches[i].ForeColor = IsLight(selected[i]) ? Color.FromArgb(25, 28, 34) : Color.White;
        }

        private static bool IsLight(Color color) { return (color.R * 299 + color.G * 587 + color.B * 114) / 1000 > 145; }
    }

    internal sealed class AboutForm : ChromeForm
    {
        private ThemePalette palette;

        public AboutForm(ThemePalette currentPalette) : base(false)
        {
            palette = currentPalette;
            Text = "Sobre MacroDeck";
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false; MaximizeBox = false; MinimizeBox = false;
            ClientSize = new Size(500, 394); BackColor = palette.Background; ForeColor = palette.Text;
            Font = FontManager.Create(9f, FontStyle.Regular);
            ApplyChromeTheme(palette.Background, palette.Text, palette.Card);

            RoundedPanel card = new RoundedPanel { Location = new Point(20, 20), Size = new Size(460, 312), BackColor = palette.Card, BorderColor = palette.Border, CornerRadius = 16 };
            ContentPanel.Controls.Add(card);
            Label title = new Label { Text = "MacroDeck", AutoSize = true, Location = new Point(28, 26), Font = FontManager.Create(22f, FontStyle.Bold), ForeColor = palette.Text };
            Label version = new Label { Text = "Versão 1.0.0", AutoSize = true, Location = new Point(30, 70), ForeColor = palette.Muted };
            Label author = new Label { Text = "Criado por shaman", AutoSize = true, Location = new Point(30, 112), Font = FontManager.Create(10f, FontStyle.Bold), ForeColor = palette.Text };
            Label description = new Label { Text = "Gravador e reprodutor de macros para Windows.\r\nSeus macros e preferências ficam no computador.", AutoSize = false, Location = new Point(30, 146), Size = new Size(400, 52), ForeColor = palette.Muted };
            Label fontCredit = new Label { Text = "Fonte Montserrat · SIL Open Font License 1.1", AutoSize = true, Location = new Point(30, 218), ForeColor = palette.Muted };
            RoundedButton license = new RoundedButton { Text = "Ver licença da fonte", Location = new Point(30, 250), Size = new Size(166, 36), CornerRadius = 9, Tag = "secondary", Font = FontManager.Create(8.5f, FontStyle.Bold) };
            license.Click += delegate { using (LicenseForm licenseDialog = new LicenseForm(palette)) licenseDialog.ShowDialog(this); };
            RoundedButton close = new RoundedButton { Text = "Fechar", Location = new Point(342, 250), Size = new Size(88, 36), CornerRadius = 9, Tag = "primary", Font = FontManager.Create(8.5f, FontStyle.Bold), DialogResult = DialogResult.OK };
            card.Controls.Add(title); card.Controls.Add(version); card.Controls.Add(author); card.Controls.Add(description); card.Controls.Add(fontCredit); card.Controls.Add(license); card.Controls.Add(close);
            ApplyButtonColors(card);
            AcceptButton = close;
        }

        public void RefreshPalette(ThemePalette currentPalette)
        {
            palette = currentPalette;
            BackColor = palette.Background; ForeColor = palette.Text;
            ApplyChromeTheme(palette.Background, palette.Text, palette.Card);
            ApplyAboutPalette(ContentPanel);
        }

        private void ApplyAboutPalette(Control parent)
        {
            foreach (Control control in parent.Controls)
            {
                RoundedPanel card = control as RoundedPanel;
                if (card != null) { card.BackColor = palette.Card; card.BorderColor = palette.Border; }
                Label label = control as Label;
                if (label != null)
                {
                    bool muted = label.Text.StartsWith("Versão", StringComparison.Ordinal) || label.Text.StartsWith("Fonte", StringComparison.Ordinal) || label.Text.StartsWith("Gravador", StringComparison.Ordinal);
                    label.ForeColor = muted ? palette.Muted : palette.Text;
                }
                RoundedButton button = control as RoundedButton;
                if (button != null)
                {
                    bool primary = button.Tag != null && (string)button.Tag == "primary";
                    button.BackColor = primary ? palette.Accent : palette.Input;
                    button.ForeColor = palette.Text;
                }
                ApplyAboutPalette(control);
            }
        }

        private void ApplyButtonColors(Control root)
        {
            foreach (Control c in root.Controls)
            {
                RoundedButton button = c as RoundedButton;
                if (button != null) { button.BackColor = button.Tag != null && (string)button.Tag == "primary" ? palette.Accent : palette.Input; button.ForeColor = palette.Text; }
            }
        }
    }

    internal sealed class LicenseForm : ChromeForm
    {
        public LicenseForm(ThemePalette palette) : base(false)
        {
            Text = "Licença Montserrat"; StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false; MinimizeBox = false;
            ClientSize = new Size(700, 578); BackColor = palette.Background; ForeColor = palette.Text;
            Font = FontManager.Create(9f, FontStyle.Regular);
            ApplyChromeTheme(palette.Background, palette.Text, palette.Card);
            ContentPanel.Padding = new Padding(18, 16, 18, 16);
            ModernScrollViewer viewer = new ModernScrollViewer { Dock = DockStyle.Fill, BackColor = palette.Background };
            BufferedPanel document = new BufferedPanel { BackColor = palette.Card, Padding = new Padding(18) };
            Label license = new Label
            {
                AutoSize = false,
                Text = FontManager.LicenseText(),
                BackColor = palette.Card,
                ForeColor = palette.Text,
                Font = FontManager.Create(9f, FontStyle.Regular),
                Location = new Point(18, 18)
            };
            document.Controls.Add(license);
            Action layoutDocument = delegate
            {
                int textWidth = Math.Max(100, document.ClientSize.Width - 36);
                Size measured = TextRenderer.MeasureText(license.Text, license.Font, new Size(textWidth, Int32.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
                license.SetBounds(18, 18, textWidth, measured.Height + 4);
                int contentHeight = Math.Max(viewer.ClientSize.Height, license.Bottom + 18);
                if (document.Height != contentHeight) document.Height = contentHeight;
            };
            document.Resize += delegate { layoutDocument(); };
            viewer.SizeChanged += delegate { layoutDocument(); };
            document.Width = Math.Max(100, viewer.ClientSize.Width);
            layoutDocument();
            viewer.Content = document;
            viewer.ApplyTheme(palette.Background, palette.Background, palette.Accent);
            ContentPanel.Controls.Add(viewer);
        }
    }
}
