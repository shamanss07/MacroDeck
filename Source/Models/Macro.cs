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
    }
    public sealed class MacroFile
    {
        public int Version { get; set; }
        public string Name { get; set; }
        public DateTime CreatedAt { get; set; }
        public List<MacroEvent> Events { get; set; }
    }
}
