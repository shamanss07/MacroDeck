using System;

namespace MacroDeck
{
    internal sealed class MacroChoice
    {
        public string Path;
        public string Name;
        public bool? CycleInfinite;
        public int? IntervalMinutes;
        public int? IntervalSeconds;
        public bool? VaryPlayback;
        public int? VariationPixels;
        public bool? VaryActionTiming;
        public int? ActionVariationSeconds;
        public bool? VaryCyclePause;
        public int? CycleVariationSeconds;
        public bool? PauseBetweenCycles;
        public int? CyclePauseChancePercent;
        public int? CyclePauseMinutes;
        public int? CyclePauseMinimumCycles;
        public int? ClickVariationPercent;
        public bool? VaryClickCount;
        public bool? SmoothCursorReturn;

        public bool HasPlaybackSettings
        {
            get { return CycleInfinite.HasValue && IntervalMinutes.HasValue && IntervalSeconds.HasValue; }
        }

        public override string ToString() { return Name; }
    }
}
