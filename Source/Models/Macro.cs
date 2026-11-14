using System;
using System.Collections.Generic;

namespace MacroDeck
{
    public sealed class MacroEvent
    {
        public string Kind { get; set; }
        public int Code { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Wheel { get; set; }
        public long TimeMs { get; set; }
        public long? TimeUs { get; set; }
    }
    public sealed class MacroFile
    {
        public int Version { get; set; }
        public string Name { get; set; }
        public DateTime CreatedAt { get; set; }
        public List<MacroEvent> Events { get; set; }
        public bool? CycleInfinite { get; set; }
        public int? IntervalMinutes { get; set; }
        public int? IntervalSeconds { get; set; }
        public int? StartMouseX { get; set; }
        public int? StartMouseY { get; set; }
        public bool? VaryPlayback { get; set; }
        public int? VariationPixels { get; set; }
        public bool? VaryActionTiming { get; set; }
        public int? ActionVariationSeconds { get; set; }
        public bool? VaryCyclePause { get; set; }
        public int? CycleVariationSeconds { get; set; }
        public bool? PauseBetweenCycles { get; set; }
        public int? CyclePauseChancePercent { get; set; }
        public int? CyclePauseDenominator { get; set; }
        public int? CyclePauseMinutes { get; set; }
        public int? CyclePauseMinimumCycles { get; set; }
        public int? ClickVariationPercent { get; set; }
        public bool? VaryClickCount { get; set; }
        public bool? SmoothCursorReturn { get; set; }
    }
}
