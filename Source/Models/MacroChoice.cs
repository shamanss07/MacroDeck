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

        public bool HasPlaybackSettings
        {
            get { return CycleInfinite.HasValue && IntervalMinutes.HasValue && IntervalSeconds.HasValue; }
        }

        public override string ToString() { return Name; }
    }
}
