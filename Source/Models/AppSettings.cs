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
        public string Language { get; set; }
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
        public string ReaderMonitorDevice { get; set; }
        public bool VaryPlayback { get; set; }
        public int VariationPixels { get; set; }
        public bool VaryActionTiming { get; set; }
        public int ActionVariationSeconds { get; set; }
        public bool VaryCyclePause { get; set; }
        public int CycleVariationSeconds { get; set; }
        public bool PauseBetweenCycles { get; set; }
        public int? CyclePauseChancePercent { get; set; }
        public int? CyclePauseDenominator { get; set; }
        public int? CyclePauseMinutes { get; set; }
        public int? CyclePauseMinimumCycles { get; set; }
        public int ClickVariationPercent { get; set; }
        public bool? VaryClickCount { get; set; }
        public bool SmoothCursorReturn { get; set; }
        public bool AlwaysRunAsAdministrator { get; set; }

        public static AppSettings Defaults()
        {
            return new AppSettings {
                Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MacroDeck", "Macros"),
                RecordHotkey = "F9", PlayHotkey = "F10", DarkTheme = true,
                BackgroundColor = "", CardColor = "", TextColor = "", AccentColor = "",
                FontFamily = "", Language = Localization.DetectDefaultLanguage(),
                DarkBackgroundColor = "", DarkCardColor = "", DarkTextColor = "", DarkAccentColor = "",
                LightBackgroundColor = "", LightCardColor = "", LightTextColor = "", LightAccentColor = "",
                CycleInfinite = false, Repeat = false, IntervalMinutes = 0, IntervalSeconds = 5,
                Interval = 5, IntervalUnit = "segundos", SelectedFile = ""
            };
        }
    }
}
