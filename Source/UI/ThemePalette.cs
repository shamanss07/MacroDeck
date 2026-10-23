using System;
using System.Drawing;

namespace MacroDeck
{
    internal sealed class ThemePalette
    {
        public Color Background;
        public Color Card;
        public Color Text;
        public Color Muted;
        public Color Accent;
        public Color Input;
        public Color Border;

        public static ThemePalette Defaults(bool dark)
        {
            return dark
                ? new ThemePalette {
                    Background = Color.FromArgb(19, 22, 28), Card = Color.FromArgb(31, 35, 43),
                    Text = Color.FromArgb(235, 238, 243), Muted = Color.FromArgb(166, 174, 187),
                    Accent = Color.FromArgb(87, 112, 224), Input = Color.FromArgb(41, 46, 56),
                    Border = Color.FromArgb(57, 63, 74)
                }
                : new ThemePalette {
                    Background = Color.FromArgb(243, 246, 250), Card = Color.White,
                    Text = Color.FromArgb(31, 39, 51), Muted = Color.FromArgb(94, 105, 120),
                    Accent = Color.FromArgb(75, 105, 220), Input = Color.White,
                    Border = Color.FromArgb(220, 226, 235)
                };
        }

        public static ThemePalette From(AppSettings settings)
        {
            ThemePalette palette = Defaults(settings.DarkTheme);
            string background = settings.DarkTheme ? settings.DarkBackgroundColor : settings.LightBackgroundColor;
            string card = settings.DarkTheme ? settings.DarkCardColor : settings.LightCardColor;
            string text = settings.DarkTheme ? settings.DarkTextColor : settings.LightTextColor;
            string accent = settings.DarkTheme ? settings.DarkAccentColor : settings.LightAccentColor;
            palette.Background = ReadColor(String.IsNullOrWhiteSpace(background) ? settings.BackgroundColor : background, palette.Background);
            palette.Card = ReadColor(String.IsNullOrWhiteSpace(card) ? settings.CardColor : card, palette.Card);
            palette.Text = ReadColor(String.IsNullOrWhiteSpace(text) ? settings.TextColor : text, palette.Text);
            palette.Accent = ReadColor(String.IsNullOrWhiteSpace(accent) ? settings.AccentColor : accent, palette.Accent);
            palette.Muted = Blend(palette.Text, palette.Background, 0.68f);
            palette.Input = settings.DarkTheme ? Blend(palette.Card, palette.Background, 0.56f) : Color.White;
            palette.Border = Blend(palette.Card, palette.Text, 0.12f);
            return palette;
        }

        private static Color ReadColor(string value, Color fallback)
        {
            try { return String.IsNullOrWhiteSpace(value) ? fallback : ColorTranslator.FromHtml(value); }
            catch { return fallback; }
        }

        private static Color Blend(Color a, Color b, float amount)
        {
            amount = Math.Max(0f, Math.Min(1f, amount));
            return Color.FromArgb(
                (int)(a.R * (1f - amount) + b.R * amount),
                (int)(a.G * (1f - amount) + b.G * amount),
                (int)(a.B * (1f - amount) + b.B * amount));
        }

        public static string Hex(Color color)
        {
            return String.Format("#{0:X2}{1:X2}{2:X2}", color.R, color.G, color.B);
        }
    }
}
