using System;
using System.IO;

namespace MacroDeck
{
    public sealed class AppSettings
    {
        public string Folder { get; set; }
        public string RecordHotkey { get; set; }
        public string PlayHotkey { get; set; }
        public bool DarkTheme { get; set; }
        public string BackgroundColor { get; set; }
        public string CardColor { get; set; }
        public string TextColor { get; set; }
        public string AccentColor { get; set; }
        public string FontFamily { get; set; }
        public string DarkBackgroundColor { get; set; }
        public string DarkCardColor { get; set; }
        public string DarkTextColor { get; set; }
        public string DarkAccentColor { get; set; }
        public string LightBackgroundColor { get; set; }
        public string LightCardColor { get; set; }
        public string LightTextColor { get; set; }
        public string LightAccentColor { get; set; }
        public bool CycleInfinite { get; set; }
        public int IntervalMinutes { get; set; }
        public int IntervalSeconds { get; set; }
        // Legacy fields are kept so settings from the first build migrate cleanly.
        public bool Repeat { get; set; }
        public decimal Interval { get; set; }
        public string IntervalUnit { get; set; }
        public string SelectedFile { get; set; }

        public static AppSettings Defaults()
        {
            return new AppSettings {
                Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MacroDeck", "Macros"),
                RecordHotkey = "F9", PlayHotkey = "F10", DarkTheme = true,
                BackgroundColor = "", CardColor = "", TextColor = "", AccentColor = "",
                FontFamily = "",
                DarkBackgroundColor = "", DarkCardColor = "", DarkTextColor = "", DarkAccentColor = "",
                LightBackgroundColor = "", LightCardColor = "", LightTextColor = "", LightAccentColor = "",
                CycleInfinite = false, Repeat = false, IntervalMinutes = 0, IntervalSeconds = 5,
                Interval = 5, IntervalUnit = "segundos", SelectedFile = ""
            };
        }
    }
}
