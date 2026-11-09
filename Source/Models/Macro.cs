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
        public bool? SmoothCursorReturn { get; set; }
    }
}
